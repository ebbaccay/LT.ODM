using System.Collections.Concurrent;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;

namespace LT.ODM.Api.Tests;

/// <summary>
/// In-memory stand-in for the auth.* stored procedures, mirroring their rules
/// (lockout, token rotation with a 30-second "stale" window, single-use reset tokens).
/// </summary>
public sealed class FakeAuthRepository : IAuthRepository
{
    private sealed class User
    {
        public int UserId;
        public string UserName = "", Email = "", DisplayName = "", PasswordHash = "";
        public Guid Stamp = Guid.NewGuid();
        public bool IsActive = true, MustChange;
        public int Failed;
        public DateTime? LockoutEnd;
        public List<string> History = [];
        public string Roles = "";
    }

    private sealed class Token
    {
        public int UserId;
        public Guid Family;
        public bool Persistent;
        public DateTime Expires;
        public DateTime? Revoked;
        public bool Replaced;
    }

    private readonly ConcurrentDictionary<int, User> _users = new();
    private readonly ConcurrentDictionary<string, Token> _refresh = new();
    private readonly ConcurrentDictionary<string, (int UserId, DateTime Expires, bool Used)> _reset = new();
    private int _nextId;

    private static string Key(byte[] hash) => Convert.ToHexString(hash);

    private static UserRecord ToRecord(User u) => new()
    {
        UserId = u.UserId, UserName = u.UserName, Email = u.Email, DisplayName = u.DisplayName, PasswordHash = u.PasswordHash,
        SecurityStamp = u.Stamp, IsActive = u.IsActive, MustChangePassword = u.MustChange, FailedLoginCount = u.Failed,
        LockoutEndUtc = u.LockoutEnd, Roles = u.Roles,
    };

    public Task<UserRecord?> GetUserAsync(int? userId, string? login, CancellationToken ct = default)
    {
        var u = userId is not null
            ? _users.GetValueOrDefault(userId.Value)
            : _users.Values.FirstOrDefault(x => string.Equals(x.UserName, login, StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(x.Email, login, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(u is null ? null : ToRecord(u));
    }

    public Task<SignInRecord> RecordSignInAsync(int userId, bool succeeded, int maxFailedAttempts, int lockoutMinutes, string? loginName, ClientInfo client, CancellationToken ct = default)
    {
        var u = _users[userId];
        if (succeeded)
        {
            u.Failed = 0;
            u.LockoutEnd = null;
            return Task.FromResult(new SignInRecord());
        }
        if (u.Failed + 1 >= maxFailedAttempts)
        {
            u.Failed = 0;
            u.LockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);
            return Task.FromResult(new SignInRecord { IsLockedOut = true, LockoutEndUtc = u.LockoutEnd });
        }
        u.Failed++;
        return Task.FromResult(new SignInRecord());
    }

    public Task AuditAsync(int? userId, string? loginName, string eventType, ClientInfo client, string? detailsJson = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<int> CreateUserAsync(string userName, string email, string displayName, string passwordHash, bool mustChangePassword, IEnumerable<string> roles, string? userGroup = null, string? location = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        _users[id] = new User
        {
            UserId = id, UserName = userName, Email = email, DisplayName = displayName, PasswordHash = passwordHash,
            MustChange = mustChangePassword, History = [passwordHash], Roles = string.Join(',', roles),
        };
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<string>> GetPasswordHistoryAsync(int userId, int count, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_users[userId].History.AsEnumerable().Reverse().Take(count).ToList());

    public Task<bool> SetPasswordAsync(int userId, string passwordHash, int historyToKeep, string eventType, byte[]? resetTokenHash, ClientInfo client, CancellationToken ct = default)
    {
        if (resetTokenHash is not null)
        {
            var key = Key(resetTokenHash);
            if (!_reset.TryGetValue(key, out var t) || t.Used || t.Expires <= DateTime.UtcNow || t.UserId != userId)
                return Task.FromResult(false);
        }
        var u = _users[userId];
        u.PasswordHash = passwordHash;
        u.Stamp = Guid.NewGuid();
        u.MustChange = false;
        u.History.Add(passwordHash);
        foreach (var (k, t) in _refresh.Where(x => x.Value.UserId == userId)) t.Revoked ??= DateTime.UtcNow;
        foreach (var k in _reset.Where(x => x.Value.UserId == userId).Select(x => x.Key).ToList())
            _reset[k] = _reset[k] with { Used = true };
        return Task.FromResult(true);
    }

    public Task CreateRefreshTokenAsync(int userId, byte[] tokenHash, bool isPersistent, DateTime expiresUtc, ClientInfo client, CancellationToken ct = default)
    {
        _refresh[Key(tokenHash)] = new Token { UserId = userId, Family = Guid.NewGuid(), Persistent = isPersistent, Expires = expiresUtc };
        return Task.CompletedTask;
    }

    public Task<RefreshRotationRecord> RotateRefreshTokenAsync(byte[] oldTokenHash, byte[] newTokenHash, int persistentLifetimeMinutes, int sessionLifetimeMinutes, ClientInfo client, CancellationToken ct = default)
    {
        if (!_refresh.TryGetValue(Key(oldTokenHash), out var old) || old.Expires <= DateTime.UtcNow)
            return Task.FromResult(new RefreshRotationRecord { Status = "Invalid" });
        if (old.Revoked is not null)
        {
            if (old.Replaced && old.Revoked > DateTime.UtcNow.AddSeconds(-30))
                return Task.FromResult(new RefreshRotationRecord { Status = "Stale" });
            foreach (var t in _refresh.Values.Where(t => t.Family == old.Family)) t.Revoked ??= DateTime.UtcNow;
            return Task.FromResult(new RefreshRotationRecord { Status = "Reused" });
        }

        var expires = DateTime.UtcNow.AddMinutes(old.Persistent ? persistentLifetimeMinutes : sessionLifetimeMinutes);
        _refresh[Key(newTokenHash)] = new Token { UserId = old.UserId, Family = old.Family, Persistent = old.Persistent, Expires = expires };
        old.Revoked = DateTime.UtcNow;
        old.Replaced = true;

        var u = _users[old.UserId];
        return Task.FromResult(new RefreshRotationRecord
        {
            Status = "Ok", IsPersistent = old.Persistent, ExpiresUtc = expires, UserId = u.UserId, UserName = u.UserName,
            Email = u.Email, DisplayName = u.DisplayName, SecurityStamp = u.Stamp, MustChangePassword = u.MustChange, Roles = u.Roles,
        });
    }

    public Task RevokeRefreshTokenAsync(byte[] tokenHash, ClientInfo client, CancellationToken ct = default)
    {
        if (_refresh.TryGetValue(Key(tokenHash), out var token))
            foreach (var t in _refresh.Values.Where(t => t.Family == token.Family)) t.Revoked ??= DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<ResetUserRecord?> CreatePasswordResetAsync(string email, byte[] tokenHash, DateTime expiresUtc, int maxPerHour, ClientInfo client, CancellationToken ct = default)
    {
        var u = _users.Values.FirstOrDefault(x => x.IsActive && string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));
        if (u is null) return Task.FromResult<ResetUserRecord?>(null);
        foreach (var k in _reset.Where(x => x.Value.UserId == u.UserId).Select(x => x.Key).ToList())
            _reset[k] = _reset[k] with { Used = true };
        _reset[Key(tokenHash)] = (u.UserId, expiresUtc, false);
        return Task.FromResult<ResetUserRecord?>(new ResetUserRecord { UserId = u.UserId, UserName = u.UserName, Email = u.Email, DisplayName = u.DisplayName });
    }

    public Task<ResetUserRecord?> GetPasswordResetUserAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        if (!_reset.TryGetValue(Key(tokenHash), out var t) || t.Used || t.Expires <= DateTime.UtcNow)
            return Task.FromResult<ResetUserRecord?>(null);
        var u = _users[t.UserId];
        return Task.FromResult<ResetUserRecord?>(new ResetUserRecord { UserId = u.UserId, UserName = u.UserName, Email = u.Email, DisplayName = u.DisplayName });
    }
}

/// <summary>Captures queued emails instead of sending them.</summary>
public sealed class FakeEmailQueue : IEmailQueue
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public ValueTask QueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        Sent.Enqueue(message);
        return ValueTask.CompletedTask;
    }
}
