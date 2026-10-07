using System.Security.Claims;
using System.Text;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LT.ODM.Api.Auth;

/// <summary>Bound from the "Jwt" configuration section. SigningKey comes from user secrets / environment, never appsettings.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "LT.ODM";
    public string Audience { get; set; } = "LT.ODM.Web";
    /// <summary>HMAC-SHA256 key, at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public SymmetricSecurityKey CreateKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class LtOdmClaims
{
    public const string SecurityStamp = "stamp";
    /// <summary>TMS user group ("FTY" = factory user).</summary>
    public const string UserGroup = "user_group";
    /// <summary>TMS location / factory code.</summary>
    public const string Location = "location";
}

public sealed class JwtAccessTokenService(IOptions<JwtOptions> jwt, IOptions<AuthOptions> auth, TimeProvider time) : IAccessTokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresUtc) CreateAccessToken(UserProfileDto user, Guid securityStamp)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(auth.Value.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.PreferredUsername, user.UserName),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(LtOdmClaims.SecurityStamp, securityStamp.ToString()),
        };
        claims.AddRange(user.Roles.Select(r => new Claim("role", r)));
        if (!string.IsNullOrEmpty(user.UserGroup)) claims.Add(new Claim(LtOdmClaims.UserGroup, user.UserGroup));
        if (!string.IsNullOrEmpty(user.Location)) claims.Add(new Claim(LtOdmClaims.Location, user.Location));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Value.Issuer,
            Audience = jwt.Value.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(jwt.Value.CreateKey(), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }
}
