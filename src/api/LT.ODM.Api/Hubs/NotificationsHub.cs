using System.Text.Json;
using LT.ODM.Infrastructure.Tms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Hubs;

/// <summary>
/// Real-time notifications. Requires a signed-in user; WebSocket clients pass the JWT as ?access_token=.
///
/// Garment-quotation notifications (ported from the TMS NotificationHub, same method and event names):
/// merchandisers are in group "TMS", factory users in "FTY_{location}". Unlike TMS, the group, sender and
/// history scope come from the sign-in token; the client's values are ignored or checked.
/// </summary>
[Authorize]
public sealed class NotificationsHub(TmsNotificationRepository notifications, ILogger<NotificationsHub> logger) : Hub
{
    private const int MaxText = 2000;

    public override async Task OnConnectedAsync()
    {
        if (TmsNotificationRules.GroupFor(Context.User!) is { } group)
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }

    /// <summary>TMS compatibility. Arguments are ignored: the group comes from the sign-in token.</summary>
    public async Task RegisterUser(string? userGroup, string? location)
    {
        var group = TmsNotificationRules.GroupFor(Context.User!);
        if (group is null)
        {
            await Clients.Caller.SendAsync("RegisterUserError", "Your account has no factory location; ask an administrator to set it.");
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await Clients.Caller.SendAsync("RegisterUserSuccess", new { groupName = group });
    }

    /// <summary>
    /// Same signature as TMS. <paramref name="sender"/> and <paramref name="senderDisplay"/> are replaced by the
    /// signed-in user; the recipient group is checked against the sender's group.
    /// </summary>
    public async Task SendNotification(string type, string styleId, string packName, string factoryId, string factoryName,
        string sender, string senderDisplay, string recipientGroup, string messageText)
    {
        var senderGroup = TmsNotificationRules.GroupFor(Context.User!);
        if (senderGroup is null || !TmsNotificationRules.Types.Contains(type)
            || !TmsNotificationRules.CanSend(senderGroup, recipientGroup ?? "", factoryId ?? ""))
        {
            logger.LogWarning("Refused notification {Type} from {User} ({Group}) to {Recipient}.", type, Context.User?.Identity?.Name, senderGroup, recipientGroup);
            await Clients.Caller.SendAsync("NotificationError", "This notification is not allowed.");
            return;
        }

        var userName = Context.User!.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";
        var displayName = Context.User!.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? userName;
        var text = (messageText ?? "").Length > MaxText ? messageText![..MaxText] : messageText ?? "";

        try
        {
            await notifications.SaveAsync(type, styleId ?? "", packName ?? "", factoryId ?? "", factoryName ?? "",
                userName, displayName, recipientGroup!, text, Context.ConnectionAborted);

            await Clients.Group(recipientGroup!).SendAsync("GqNotification", new
            {
                id = $"gqn_{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}",
                type,
                styleId,
                packName,
                // TMS left this out, so factory users' client-side filter dropped every live notification.
                factoryId,
                factoryName,
                sender = userName,
                text,
                ts = DateTime.Now.ToString("MMM dd, hh:mm tt"),
                read = false,
            });
            await Clients.Caller.SendAsync("NotificationSent", new { success = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Saving notification {Type} for style {StyleId} failed.", type, styleId);
            await Clients.Caller.SendAsync("NotificationError", "The notification could not be sent.");
        }
    }

    /// <summary>Same signature as TMS, but only the caller's own group history is returned.</summary>
    public async Task LoadNotificationHistory(string? recipientGroup, int limitDays = 7)
    {
        var group = TmsNotificationRules.GroupFor(Context.User!);
        if (group is null)
        {
            await Clients.Caller.SendAsync("NotificationHistory", "[]");
            return;
        }
        try
        {
            var rows = await notifications.HistoryAsync(group, Math.Clamp(limitDays, 1, 90), Context.ConnectionAborted);
            // TMS sent a JSON string (serialised DataTable); keep that for the ported client.
            await Clients.Caller.SendAsync("NotificationHistory", JsonSerializer.Serialize(rows));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Loading notification history for {Group} failed.", group);
            await Clients.Caller.SendAsync("NotificationHistoryError", "Notification history is not available.");
        }
    }
}
