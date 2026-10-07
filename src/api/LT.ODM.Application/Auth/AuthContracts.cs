namespace LT.ODM.Application.Auth;

// ----- API request / response DTOs -----

public sealed record LoginRequest(string Login, string Password, bool RememberMe);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetTokenRequest(string Token);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>UserGroup / Location: TMS user attributes (UserGroup "FTY" = factory user, Location = factory code).</summary>
public sealed record UserProfileDto(int UserId, string UserName, string Email, string DisplayName, IReadOnlyList<string> Roles, bool MustChangePassword, string? UserGroup = null, string? Location = null);

/// <summary>Returned by login and refresh. The refresh token itself travels only in an HttpOnly cookie.</summary>
public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresUtc, UserProfileDto User);

public sealed record PasswordPolicyDto(int MinLength, int MaxLength, bool RequireUppercase, bool RequireLowercase, bool RequireDigit, bool RequireSymbol, int HistoryCount);

/// <summary>IP address and user agent of the caller, for the audit log.</summary>
public sealed record ClientInfo(string? IpAddress, string? UserAgent);

// ----- Service results -----

/// <summary>A signed-in session: access token for the response body, refresh token for the cookie.</summary>
public sealed record AuthSession(AuthResponse Response, string RefreshToken, DateTime RefreshTokenExpiresUtc, bool IsPersistent);

public enum LoginStatus { Success, InvalidCredentials, LockedOut }

public sealed record LoginResult(LoginStatus Status, AuthSession? Session = null, DateTime? LockoutEndUtc = null);

public enum RefreshStatus { Success, Invalid, Stale }

public sealed record RefreshResult(RefreshStatus Status, AuthSession? Session = null);

public enum PasswordChangeStatus { Success, InvalidToken, WrongCurrentPassword, PolicyFailed }

public sealed record PasswordChangeResult(PasswordChangeStatus Status, IReadOnlyList<string>? Errors = null);

// ----- Repository records (filled by Dapper from the auth.* stored procedures) -----

public sealed class UserRecord
{
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public Guid SecurityStamp { get; init; }
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }
    public int FailedLoginCount { get; init; }
    public DateTime? LockoutEndUtc { get; init; }
    public string? Roles { get; init; }
    public string? UserGroup { get; init; }
    public string? Location { get; init; }
}

public sealed class SignInRecord
{
    public bool IsLockedOut { get; init; }
    public DateTime? LockoutEndUtc { get; init; }
}

public sealed class RefreshRotationRecord
{
    /// <summary>Ok, Invalid, Stale or Reused.</summary>
    public string Status { get; init; } = string.Empty;
    public bool IsPersistent { get; init; }
    public DateTime? ExpiresUtc { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public Guid SecurityStamp { get; init; }
    public bool MustChangePassword { get; init; }
    public string? Roles { get; init; }
    public string? UserGroup { get; init; }
    public string? Location { get; init; }
}

public sealed class ResetUserRecord
{
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
