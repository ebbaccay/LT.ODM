using System.Net.Mail;
using LT.ODM.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>HttpOnly refresh-token cookie, sent only to the auth endpoints.</summary>
    public const string RefreshCookieName = "ltodm_rt";
    private const string RefreshCookiePath = "/api/v1/auth";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Enter your user name or email and your password.");

        var result = await auth.LoginAsync(request, Client, ct);
        switch (result.Status)
        {
            case LoginStatus.Success:
                SetRefreshCookie(result.Session!);
                return Ok(result.Session!.Response);
            case LoginStatus.LockedOut:
                var minutes = Math.Max(1, (int)Math.Ceiling((result.LockoutEndUtc!.Value - DateTime.UtcNow).TotalMinutes));
                return Problem(statusCode: StatusCodes.Status423Locked, title: "Account temporarily locked",
                    detail: $"Too many failed sign-in attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")} or reset your password.");
            default:
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid user name or password.");
        }
    }

    /// <summary>Exchanges the refresh cookie for a new access token (and a rotated cookie).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var token) || string.IsNullOrEmpty(token))
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not signed in.");

        var result = await auth.RefreshAsync(token, Client, ct);
        switch (result.Status)
        {
            case RefreshStatus.Success:
                SetRefreshCookie(result.Session!);
                return Ok(result.Session!.Response);
            case RefreshStatus.Stale:
                // Another tab rotated this token a moment ago; the browser already has the new cookie.
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "Session was refreshed elsewhere. Retry.");
            default:
                DeleteRefreshCookie();
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Your session has expired. Please sign in again.");
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var token) && !string.IsNullOrEmpty(token))
            await auth.LogoutAsync(token, Client, ct);
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> Me(CancellationToken ct)
    {
        var profile = await auth.GetProfileAsync(CurrentUserId, ct);
        return profile is null ? Unauthorized() : Ok(profile);
    }

    [HttpGet("password-policy")]
    [AllowAnonymous]
    public ActionResult<PasswordPolicyDto> PasswordPolicy() => Ok(auth.GetPasswordPolicy());

    /// <summary>Always 202, whether or not the email belongs to an account (no account enumeration).</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        if (!MailAddress.TryCreate(request.Email?.Trim(), out _))
            return ValidationProblem(Errors("email", "Enter a valid email address."));

        await auth.ForgotPasswordAsync(request.Email!, Client, ct);
        return Accepted();
    }

    [HttpPost("reset-password/validate")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ValidateResetToken(ResetTokenRequest request, CancellationToken ct)
        => !string.IsNullOrEmpty(request.Token) && await auth.IsResetTokenValidAsync(request.Token, ct)
            ? NoContent()
            : InvalidResetLink();

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Token))
            return InvalidResetLink();

        var result = await auth.ResetPasswordAsync(request, Client, ct);
        return result.Status switch
        {
            PasswordChangeStatus.Success => NoContent(),
            PasswordChangeStatus.PolicyFailed => ValidationProblem(Errors("newPassword", [.. result.Errors!])),
            _ => InvalidResetLink(),
        };
    }

    /// <summary>On success every session of the user is signed out (including this one).</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await auth.ChangePasswordAsync(CurrentUserId, request, Client, ct);
        switch (result.Status)
        {
            case PasswordChangeStatus.Success:
                DeleteRefreshCookie();
                return NoContent();
            case PasswordChangeStatus.WrongCurrentPassword:
                return ValidationProblem(Errors("currentPassword", "Your current password is incorrect."));
            default:
                return ValidationProblem(Errors("newPassword", [.. result.Errors!]));
        }
    }

    private ClientInfo Client => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private ObjectResult InvalidResetLink()
        => Problem(statusCode: StatusCodes.Status400BadRequest, title: "This reset link is invalid or has expired.",
            detail: "Request a new password reset link.");

    private static ValidationProblemDetails Errors(string field, params string[] messages)
        => new(new Dictionary<string, string[]> { [field] = messages });

    private void SetRefreshCookie(AuthSession session)
        => Response.Cookies.Append(RefreshCookieName, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            IsEssential = true,
            // Without "Keep me signed in" the cookie ends with the browser session.
            Expires = session.IsPersistent ? session.RefreshTokenExpiresUtc : null,
        });

    private void DeleteRefreshCookie()
        => Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath, Secure = true, SameSite = SameSiteMode.Strict, HttpOnly = true });
}

public static class RateLimitPolicies
{
    /// <summary>Sign-in and password endpoints: 10 requests per minute per client IP.</summary>
    public const string Auth = "auth";

    /// <summary>AI endpoints: 10 requests per minute per signed-in user.</summary>
    public const string Ai = "ai";
}
