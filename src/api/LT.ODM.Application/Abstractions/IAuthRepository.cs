using LT.ODM.Application.Auth;

namespace LT.ODM.Application.Abstractions;

/// <summary>Calls the auth.* stored procedures (db/procedures/auth.procedures.sql).</summary>
public interface IAuthRepository
{
    Task<UserRecord?> GetUserAsync(int? userId, string? login, CancellationToken ct = default);

    Task<SignInRecord> RecordSignInAsync(int userId, bool succeeded, int maxFailedAttempts, int lockoutMinutes, string? loginName, ClientInfo client, CancellationToken ct = default);

    Task AuditAsync(int? userId, string? loginName, string eventType, ClientInfo client, string? detailsJson = null, CancellationToken ct = default);

    Task<int> CreateUserAsync(string userName, string email, string displayName, string passwordHash, bool mustChangePassword, IEnumerable<string> roles, string? userGroup = null, string? location = null, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetPasswordHistoryAsync(int userId, int count, CancellationToken ct = default);

    /// <summary>Returns false when <paramref name="resetTokenHash"/> is given but is invalid, used or expired.</summary>
    Task<bool> SetPasswordAsync(int userId, string passwordHash, int historyToKeep, string eventType, byte[]? resetTokenHash, ClientInfo client, CancellationToken ct = default);

    Task CreateRefreshTokenAsync(int userId, byte[] tokenHash, bool isPersistent, DateTime expiresUtc, ClientInfo client, CancellationToken ct = default);

    Task<RefreshRotationRecord> RotateRefreshTokenAsync(byte[] oldTokenHash, byte[] newTokenHash, int persistentLifetimeMinutes, int sessionLifetimeMinutes, ClientInfo client, CancellationToken ct = default);

    Task RevokeRefreshTokenAsync(byte[] tokenHash, ClientInfo client, CancellationToken ct = default);

    /// <summary>Returns the user to email, or null (unknown email, inactive user or throttled).</summary>
    Task<ResetUserRecord?> CreatePasswordResetAsync(string email, byte[] tokenHash, DateTime expiresUtc, int maxPerHour, ClientInfo client, CancellationToken ct = default);

    Task<ResetUserRecord?> GetPasswordResetUserAsync(byte[] tokenHash, CancellationToken ct = default);
}
