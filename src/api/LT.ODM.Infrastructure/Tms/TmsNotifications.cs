using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Tms;

/// <summary>
/// Rules for the garment-quotation notifications ported from the TMS NotificationHub.
/// Unlike TMS, groups and senders come from the sign-in token, never from the client.
/// </summary>
public static class TmsNotificationRules
{
    /// <summary>Merchandisers.</summary>
    public const string MerchandiserGroup = "TMS";
    public const string FactoryUserGroup = "FTY";

    public static readonly IReadOnlySet<string> Types =
        new HashSet<string>(["remark", "sent", "submission", "approval", "rejection", "recall", "revision"], StringComparer.Ordinal);

    /// <summary>"TMS" for merchandisers, "FTY_{location}" for factory users, null when a factory user has no location.</summary>
    public static string? GroupFor(string? userGroup, string? location)
        => string.Equals(userGroup, FactoryUserGroup, StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(location) ? null : $"FTY_{location}")
            : MerchandiserGroup;

    public static string? GroupFor(ClaimsPrincipal user)
        => GroupFor(user.FindFirst("user_group")?.Value, user.FindFirst("location")?.Value);

    /// <summary>
    /// Merchandisers may notify any factory group (or other merchandisers); factory users may only notify
    /// merchandisers, and only about their own factory.
    /// </summary>
    public static bool CanSend(string senderGroup, string recipientGroup, string factoryId)
    {
        if (senderGroup == MerchandiserGroup)
            return recipientGroup == MerchandiserGroup || (recipientGroup.StartsWith("FTY_", StringComparison.Ordinal) && recipientGroup.Length > 4);

        return recipientGroup == MerchandiserGroup && senderGroup == $"FTY_{factoryId}";
    }
}

/// <summary>Calls web_rd_gq_save_notification / web_rd_gq_notification_history (TMS garment-quotation schema).</summary>
public sealed class TmsNotificationRepository(TmsConnectionString connectionString, IOptions<TmsProcedureOptions> options)
{
    private string Proc(string name) => $"[{options.Value.Schema}].[{name}]";

    public async Task SaveAsync(string type, string styleId, string packName, string factoryId, string factoryName,
        string sender, string senderDisplay, string recipientGroup, string messageText, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString.Value);
        await conn.ExecuteAsync(new CommandDefinition(Proc("web_rd_gq_save_notification"), new
        {
            type,
            style_id = styleId,
            pack_name = packName,
            factory_id = factoryId,
            factory_name = factoryName,
            sender,
            sender_display = senderDisplay,
            recipient_group = recipientGroup,
            message_text = messageText,
        }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    /// <summary>Rows as column -> value, the shape TMS serialised from its DataTable.</summary>
    public async Task<List<IDictionary<string, object?>>> HistoryAsync(string recipientGroup, int limitDays, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString.Value);
        var rows = await conn.QueryAsync(new CommandDefinition(Proc("web_rd_gq_notification_history"),
            new { recipient_group = recipientGroup, limit_days = limitDays }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(r => (IDictionary<string, object?>)r).ToList();
    }
}
