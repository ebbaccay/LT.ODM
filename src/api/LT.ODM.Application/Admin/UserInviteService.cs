using System.Net;
using System.Security.Cryptography;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using Microsoft.Extensions.Options;

namespace LT.ODM.Application.Admin;

/// <summary>
/// Settings > User roles: creates users and sends "set your password" links. Administrators never choose or see a
/// password; the user sets one from the emailed link (same reset page and password policy as "Forgot password").
/// </summary>
public sealed class UserInviteService(
    IAccessRepository access,
    IPasswordHasher hasher,
    IEmailQueue emailQueue,
    IOptions<AuthOptions> options,
    TimeProvider time)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<int> InviteAsync(CreateUserRequest request, string createdBy, CancellationToken ct = default)
    {
        var user = request with
        {
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            UserGroup = Blank(request.UserGroup),
            Location = Blank(request.Location),
        };
        var token = AuthService.NewToken();
        // Hash of a random secret nobody knows: the account cannot sign in until the link is used.
        var unusable = hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var userId = await access.InviteUserAsync(user, unusable, AuthService.HashToken(token), Expires, createdBy, ct);

        await emailQueue.QueueAsync(WelcomeEmail(user.DisplayName, user.UserName, user.Email, Link(token, welcome: true)), ct);
        return userId;
    }

    public async Task SendPasswordLinkAsync(int userId, string requestedBy, CancellationToken ct = default)
    {
        var token = AuthService.NewToken();
        var user = await access.CreatePasswordLinkAsync(userId, AuthService.HashToken(token), Expires, requestedBy, ct);
        await emailQueue.QueueAsync(LinkEmail(user, Link(token, welcome: false)), ct);
    }

    private DateTime Expires => time.GetUtcNow().UtcDateTime.AddHours(_options.InviteTokenHours);

    private string Link(string token, bool welcome)
        => $"{_options.PublicBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}{(welcome ? "&welcome=1" : "")}";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private EmailMessage WelcomeEmail(string displayName, string userName, string email, string link)
    {
        var hours = _options.InviteTokenHours;
        var html = $"""
            <p>Hello {WebUtility.HtmlEncode(displayName)},</p>
            <p>An account has been created for you in the LT ODM Style Library. Your user name is <strong>{WebUtility.HtmlEncode(userName)}</strong>.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Set your password</a></p>
            <p>The link expires in {hours} hours and can be used once. If it expires, ask your administrator for a new one.</p>
            """;
        var text = $"""
            Hello {displayName},

            An account has been created for you in the LT ODM Style Library. Your user name is {userName}.
            Set your password: {link}

            The link expires in {hours} hours and can be used once. If it expires, ask your administrator for a new one.
            """;
        return new EmailMessage(email, "Your LT ODM account", html, text);
    }

    private EmailMessage LinkEmail(ResetUserRecord user, string link)
    {
        var hours = _options.InviteTokenHours;
        var html = $"""
            <p>Hello {WebUtility.HtmlEncode(user.DisplayName)},</p>
            <p>Your administrator sent you a link to set a new password for your LT ODM Style Library account ({WebUtility.HtmlEncode(user.UserName)}).</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Set your password</a></p>
            <p>The link expires in {hours} hours and can be used once. Until you use it, your current password (if any) keeps working.</p>
            """;
        var text = $"""
            Hello {user.DisplayName},

            Your administrator sent you a link to set a new password for your LT ODM Style Library account ({user.UserName}).
            Set your password: {link}

            The link expires in {hours} hours and can be used once. Until you use it, your current password (if any) keeps working.
            """;
        return new EmailMessage(user.Email, "Set your LT ODM password", html, text);
    }
}
