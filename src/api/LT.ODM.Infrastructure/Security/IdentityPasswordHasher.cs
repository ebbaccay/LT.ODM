using LT.ODM.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace LT.ODM.Infrastructure.Security;

/// <summary>
/// ASP.NET Core Identity's hasher without the rest of Identity:
/// PBKDF2-HMAC-SHA512, 100,000 iterations, 128-bit random salt per password (format v3).
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly object User = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(User, password);

    public bool Verify(string passwordHash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(User, passwordHash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
