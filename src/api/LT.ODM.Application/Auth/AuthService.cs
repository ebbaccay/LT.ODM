using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LT.ODM.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LT.ODM.Application.Auth;

public sealed class AuthService(
    IAuthRepository repository,
    IPasswordHasher hasher,
    IAccessTokenService accessTokens,
    IEmailQueue emailQueue,
    IOptions<AuthOptions> options,
    TimeProvider time,
    ILogger<AuthService> logger)
{
    private readonly AuthOptions _options = options.Value;

    // Verified against when the user does not exist, so both paths take the same time.
    private readonly Lazy<string> _dummyHash = new(() => hasher.Hash(Guid.NewGuid().ToString()));

    public PasswordPolicy Policy => new(_options.Password);

    public PasswordPolicyDto GetPasswordPolicy()
    {
        var p = _options.Password;
        return new PasswordPolicyDto(p.MinLength, p.MaxLength, p.RequireUppercase, p.RequireLowercase, p.RequireDigit, p.RequireSymbol, p.HistoryCount);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct = default)
    {
        var login = request.Login.Trim();
        var user = await repository.GetUserAsync(null, login, ct);

        if (user is null || !user.IsActive)
        {
            hasher.Verify(_dummyHash.Value, request.Password);
            await repository.AuditAsync(user?.UserId, login, "LoginFailed", client, Reason(user is null ? "unknown-user" : "inactive"), ct);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        if (user.LockoutEndUtc > Now)
        {
            await repository.AuditAsync(user.UserId, login, "LoginFailed", client, Reason("locked-out"), ct);
            return new LoginResult(LoginStatus.LockedOut, LockoutEndUtc: user.LockoutEndUtc);
        }

        if (!hasher.Verify(user.PasswordHash, request.Password))
        {
            var failed = await repository.RecordSignInAsync(user.UserId, false, _options.MaxFailedAttempts, _options.LockoutMinutes, login, client, ct);
            return failed.IsLockedOut
                ? new LoginResult(LoginStatus.LockedOut, LockoutEndUtc: failed.LockoutEndUtc)
                : new LoginResult(LoginStatus.InvalidCredentials);
        }

        await repository.RecordSignInAsync(user.UserId, true, _options.MaxFailedAttempts, _options.LockoutMinutes, login, client, ct);

        var refreshToken = NewToken();
        var refreshExpires = Now.Add(request.RememberMe ? TimeSpan.FromDays(_options.PersistentSessionDays) : TimeSpan.FromHours(_options.SessionHours));
        await repository.CreateRefreshTokenAsync(user.UserId, HashToken(refreshToken), request.RememberMe, refreshExpires, client, ct);

        var profile = ToProfile(user.UserId, user.UserName, user.Email, user.DisplayName, user.Roles, user.MustChangePassword, user.UserGroup, user.Location);
        return new LoginResult(LoginStatus.Success, CreateSession(profile, user.SecurityStamp, refreshToken, refreshExpires, request.RememberMe));
    }

    public async Task<RefreshResult> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken ct = default)
    {
        var newToken = NewToken();
        var rotation = await repository.RotateRefreshTokenAsync(
            HashToken(refreshToken),
            HashToken(newToken),
            _options.PersistentSessionDays * 24 * 60,
            _options.SessionHours * 60,
            client,
            ct);

        switch (rotation.Status)
        {
            case "Ok":
                var profile = ToProfile(rotation.UserId, rotation.UserName, rotation.Email, rotation.DisplayName, rotation.Roles, rotation.MustChangePassword, rotation.UserGroup, rotation.Location);
                return new RefreshResult(RefreshStatus.Success, CreateSession(profile, rotation.SecurityStamp, newToken, DateTime.SpecifyKind(rotation.ExpiresUtc!.Value, DateTimeKind.Utc), rotation.IsPersistent));
            case "Stale":
                return new RefreshResult(RefreshStatus.Stale);
            case "Reused":
                logger.LogWarning("Refresh token re-use detected from {IpAddress}; all sessions of that sign-in were revoked.", client.IpAddress);
                return new RefreshResult(RefreshStatus.Invalid);
            default:
                return new RefreshResult(RefreshStatus.Invalid);
        }
    }

    public Task LogoutAsync(string refreshToken, ClientInfo client, CancellationToken ct = default)
        => repository.RevokeRefreshTokenAsync(HashToken(refreshToken), client, ct);

    public async Task<UserProfileDto?> GetProfileAsync(int userId, CancellationToken ct = default)
    {
        var user = await repository.GetUserAsync(userId, null, ct);
        return user is { IsActive: true }
            ? ToProfile(user.UserId, user.UserName, user.Email, user.DisplayName, user.Roles, user.MustChangePassword, user.UserGroup, user.Location)
            : null;
    }

    /// <summary>Always completes the same way; an email is queued only when the address belongs to an active user.</summary>
    public async Task ForgotPasswordAsync(string email, ClientInfo client, CancellationToken ct = default)
    {
        var token = NewToken();
        var expires = Now.AddMinutes(_options.ResetTokenMinutes);
        var user = await repository.CreatePasswordResetAsync(email.Trim(), HashToken(token), expires, _options.ResetRequestsPerHour, client, ct);
        if (user is null) return;

        var link = $"{_options.PublicBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
        await emailQueue.QueueAsync(ResetEmail(user, link), ct);
    }

    public async Task<bool> IsResetTokenValidAsync(string token, CancellationToken ct = default)
        => await repository.GetPasswordResetUserAsync(HashToken(token), ct) is not null;

    public async Task<PasswordChangeResult> ResetPasswordAsync(ResetPasswordRequest request, ClientInfo client, CancellationToken ct = default)
    {
        var tokenHash = HashToken(request.Token);
        var user = await repository.GetPasswordResetUserAsync(tokenHash, ct);
        if (user is null)
            return new PasswordChangeResult(PasswordChangeStatus.InvalidToken);

        var errors = await CheckNewPasswordAsync(user.UserId, request.NewPassword, ct, user.UserName, user.Email, user.DisplayName);
        if (errors.Count > 0)
            return new PasswordChangeResult(PasswordChangeStatus.PolicyFailed, errors);

        var ok = await repository.SetPasswordAsync(user.UserId, hasher.Hash(request.NewPassword), _options.Password.HistoryCount, "PasswordReset", tokenHash, client, ct);
        return new PasswordChangeResult(ok ? PasswordChangeStatus.Success : PasswordChangeStatus.InvalidToken);
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(int userId, ChangePasswordRequest request, ClientInfo client, CancellationToken ct = default)
    {
        var user = await repository.GetUserAsync(userId, null, ct);
        if (user is not { IsActive: true } || !hasher.Verify(user.PasswordHash, request.CurrentPassword))
            return new PasswordChangeResult(PasswordChangeStatus.WrongCurrentPassword);

        var errors = await CheckNewPasswordAsync(user.UserId, request.NewPassword, ct, user.UserName, user.Email, user.DisplayName);
        if (errors.Count > 0)
            return new PasswordChangeResult(PasswordChangeStatus.PolicyFailed, errors);

        await repository.SetPasswordAsync(user.UserId, hasher.Hash(request.NewPassword), _options.Password.HistoryCount, "PasswordChanged", null, client, ct);
        return new PasswordChangeResult(PasswordChangeStatus.Success);
    }

    /// <summary>Policy rules plus "not one of the last N passwords".</summary>
    private async Task<IReadOnlyList<string>> CheckNewPasswordAsync(int userId, string password, CancellationToken ct, params string?[] personalInfo)
    {
        var errors = Policy.Validate(password, personalInfo).ToList();
        if (errors.Count == 0 && _options.Password.HistoryCount > 0)
        {
            var history = await repository.GetPasswordHistoryAsync(userId, _options.Password.HistoryCount, ct);
            if (history.Any(h => hasher.Verify(h, password)))
                errors.Add($"You used this password recently. Choose one you have not used in your last {_options.Password.HistoryCount} passwords.");
        }
        return errors;
    }

    private AuthSession CreateSession(UserProfileDto profile, Guid securityStamp, string refreshToken, DateTime refreshExpires, bool persistent)
    {
        var (accessToken, accessExpires) = accessTokens.CreateAccessToken(profile, securityStamp);
        return new AuthSession(new AuthResponse(accessToken, accessExpires, profile), refreshToken, refreshExpires, persistent);
    }

    private static UserProfileDto ToProfile(int userId, string userName, string email, string displayName, string? roles, bool mustChangePassword, string? userGroup, string? location)
        => new(userId, userName, email, displayName,
            string.IsNullOrEmpty(roles) ? [] : roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            mustChangePassword, userGroup, location);

    private EmailMessage ResetEmail(ResetUserRecord user, string link)
    {
        var name = WebUtility.HtmlEncode(user.DisplayName);
        var href = WebUtility.HtmlEncode(link);
        var minutes = _options.ResetTokenMinutes;
        var html = $"""
            <p>Hello {name},</p>
            <p>We received a request to reset the password for your LT ODM Style Library account.</p>
            <p><a href="{href}">Reset your password</a></p>
            <p>The link expires in {minutes} minutes and can be used once. If you did not ask for this, you can ignore this email; your password will not change.</p>
            """;
        var text = $"""
            Hello {user.DisplayName},

            We received a request to reset the password for your LT ODM Style Library account.
            Reset your password: {link}

            The link expires in {minutes} minutes and can be used once. If you did not ask for this, ignore this email.
            """;
        return new EmailMessage(user.Email, "Reset your LT ODM password", html, text);
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private static string? Reason(string reason) => $$"""{"reason":"{{reason}}"}""";

    /// <summary>256-bit random token, URL-safe.</summary>
    internal static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Tokens are stored as SHA-256 hashes only.</summary>
    internal static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
