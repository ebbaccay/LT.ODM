using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Repositories;

public sealed class AuthRepository(IDbConnectionFactory connectionFactory) : IAuthRepository
{
    /// <summary>Raised by auth.usp_User_SetPassword when the reset token is invalid, used or expired.</summary>
    private const int InvalidResetTokenError = 50010;

    public async Task<UserRecord?> GetUserAsync(int? userId, string? login, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<UserRecord>(
            Proc("auth.usp_User_Get", new { UserId = userId, Login = login }, ct));
    }

    public async Task<SignInRecord> RecordSignInAsync(int userId, bool succeeded, int maxFailedAttempts, int lockoutMinutes, string? loginName, ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleAsync<SignInRecord>(Proc("auth.usp_User_RecordSignIn", new
        {
            UserId = userId,
            Succeeded = succeeded,
            MaxFailedAttempts = maxFailedAttempts,
            LockoutMinutes = lockoutMinutes,
            LoginName = loginName,
            client.IpAddress,
            client.UserAgent,
        }, ct));
    }

    public async Task AuditAsync(int? userId, string? loginName, string eventType, ClientInfo client, string? detailsJson = null, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(Proc("auth.usp_Audit_Insert", new
        {
            UserId = userId,
            LoginName = loginName,
            EventType = eventType,
            client.IpAddress,
            client.UserAgent,
            Details = detailsJson,
        }, ct));
    }

    public async Task<int> CreateUserAsync(string userName, string email, string displayName, string passwordHash, bool mustChangePassword, IEnumerable<string> roles, string? userGroup = null, string? location = null, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleAsync<int>(Proc("auth.usp_User_Create", new
        {
            UserName = userName,
            Email = email,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            MustChangePassword = mustChangePassword,
            Roles = string.Join(',', roles),
            UserGroup = userGroup,
            Location = location,
        }, ct));
    }

    public async Task<IReadOnlyList<string>> GetPasswordHistoryAsync(int userId, int count, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<string>(Proc("auth.usp_User_GetPasswordHistory", new { UserId = userId, Count = count }, ct));
        return rows.AsList();
    }

    public async Task<bool> SetPasswordAsync(int userId, string passwordHash, int historyToKeep, string eventType, byte[]? resetTokenHash, ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        try
        {
            await connection.ExecuteAsync(Proc("auth.usp_User_SetPassword", new
            {
                UserId = userId,
                PasswordHash = passwordHash,
                HistoryToKeep = historyToKeep,
                EventType = eventType,
                ResetTokenHash = resetTokenHash,
                client.IpAddress,
                client.UserAgent,
            }, ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number == InvalidResetTokenError)
        {
            return false;
        }
    }

    public async Task CreateRefreshTokenAsync(int userId, byte[] tokenHash, bool isPersistent, DateTime expiresUtc, ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(Proc("auth.usp_RefreshToken_Create", new
        {
            UserId = userId,
            TokenHash = tokenHash,
            IsPersistent = isPersistent,
            ExpiresUtc = expiresUtc,
            client.IpAddress,
        }, ct));
    }

    public async Task<RefreshRotationRecord> RotateRefreshTokenAsync(byte[] oldTokenHash, byte[] newTokenHash, int persistentLifetimeMinutes, int sessionLifetimeMinutes, ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleAsync<RefreshRotationRecord>(Proc("auth.usp_RefreshToken_Rotate", new
        {
            OldTokenHash = oldTokenHash,
            NewTokenHash = newTokenHash,
            PersistentLifetimeMin = persistentLifetimeMinutes,
            SessionLifetimeMin = sessionLifetimeMinutes,
            client.IpAddress,
            client.UserAgent,
        }, ct));
    }

    public async Task RevokeRefreshTokenAsync(byte[] tokenHash, ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(Proc("auth.usp_RefreshToken_Revoke", new { TokenHash = tokenHash, client.IpAddress, client.UserAgent }, ct));
    }

    public async Task<ResetUserRecord?> CreatePasswordResetAsync(string email, byte[] tokenHash, DateTime expiresUtc, int maxPerHour, ClientInfo client, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ResetUserRecord>(Proc("auth.usp_PasswordReset_Create", new
        {
            Email = email,
            TokenHash = tokenHash,
            ExpiresUtc = expiresUtc,
            MaxPerHour = maxPerHour,
            client.IpAddress,
            client.UserAgent,
        }, ct));
    }

    public async Task<ResetUserRecord?> GetPasswordResetUserAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ResetUserRecord>(Proc("auth.usp_PasswordReset_Get", new { TokenHash = tokenHash }, ct));
    }

    private static CommandDefinition Proc(string name, object parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
