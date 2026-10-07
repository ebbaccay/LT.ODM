using LT.ODM.Application.Auth;

namespace LT.ODM.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}

public interface IAccessTokenService
{
    (string Token, DateTime ExpiresUtc) CreateAccessToken(UserProfileDto user, Guid securityStamp);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>Queues email for background delivery, so request timing does not reveal whether an account exists.</summary>
public interface IEmailQueue
{
    ValueTask QueueAsync(EmailMessage message, CancellationToken ct = default);
}
