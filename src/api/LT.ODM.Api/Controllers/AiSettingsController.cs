using System.Security.Cryptography;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Ai;
using LT.ODM.Infrastructure.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Settings > AI connections (Admin): the AI services the app may call and which one each job uses.
/// API keys are encrypted before they are stored and never sent back; the screen sees only the last 4 characters.
/// Every change drops the cached settings, so the next AI call uses it.
/// </summary>
[ApiController]
[Route("api/v1/admin/ai")]
[Authorize(Roles = AdminValidation.AdminRole)]
public sealed class AiSettingsController(
    IAiSettingsRepository settings, IAiSecretProtector protector, IAiConnectionResolver resolver, AiConnectionTester tester) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    [HttpGet]
    public async Task<AiSettingsDto> Get(CancellationToken ct)
    {
        var (connections, purposes) = await settings.GetAsync(ct);
        var names = connections.ToDictionary(c => c.ConnectionId, c => c.Name);
        var fallbacks = new List<AiFallbackDto>();
        if (resolver is AiConnectionResolver r)
            foreach (var p in AiPurposes.InUse)
                if (r.Fallback(p) is { } f && AiConnectionInfo.Of(f) is var info)
                    fallbacks.Add(new AiFallbackDto(p, f.Name, f.Model, info.IsConfigured, info.LeavesNetwork));

        return new AiSettingsDto(
            connections.Select(c => new AiConnectionDto(c.ConnectionId, c.Name, c.Kind, c.Endpoint, c.ApiKeyProtected is not null, c.ApiKeyHint, c.InHouse,
                c.TimeoutSeconds, c.Notes, c.IsActive, purposes.Where(p => p.ConnectionId == c.ConnectionId).Select(p => p.Purpose).ToList(), c.UpdatedBy,
                c.UpdatedUtc, c.RowVer)).ToList(),
            AiPurposes.All.Select(p => purposes.FirstOrDefault(x => x.Purpose == p) is { } x
                ? new AiPurposeDto(p, AiPurposes.InUse.Contains(p), x.ConnectionId, x.ConnectionId is { } id ? names.GetValueOrDefault(id) : null, x.Model,
                    x.UpdatedBy, x.UpdatedUtc)
                : new AiPurposeDto(p, AiPurposes.InUse.Contains(p), null, null, null, null, null)).ToList(),
            fallbacks,
            AiConnectionKinds.All);
    }

    [HttpPost("connections")]
    public Task<IActionResult> Create(SaveAiConnectionRequest request, CancellationToken ct) => Save(null, request, ct);

    [HttpPut("connections/{connectionId:int}")]
    public Task<IActionResult> Update(int connectionId, SaveAiConnectionRequest request, CancellationToken ct)
        => request.RowVer is { Length: 8 }
            ? Save(connectionId, request, ct)
            : Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["rowVer"] = ["Reload and try again."] })));

    [HttpDelete("connections/{connectionId:int}")]
    public Task<IActionResult> Delete(int connectionId, string? rowVer, CancellationToken ct)
    {
        byte[]? v = null;
        try { if (!string.IsNullOrEmpty(rowVer)) v = Convert.FromBase64String(rowVer); } catch (FormatException) { }
        if (v is not { Length: 8 })
            return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["rowVer"] = ["Reload and try again."] })));
        return Run(async () =>
        {
            await settings.DeleteConnectionAsync(connectionId, v, ct);
            return NoContent();
        });
    }

    /// <summary>Points a job at a connection and model (no connection = the app default from appsettings, or not set up).</summary>
    [HttpPut("purposes/{purpose}")]
    public Task<IActionResult> SavePurpose(string purpose, SaveAiPurposeRequest request, CancellationToken ct)
    {
        var errors = AiSettingsValidation.Purpose(purpose, request);
        if (errors.Count > 0) return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(errors)));
        return Run(async () =>
        {
            await settings.SavePurposeAsync(purpose, request.ConnectionId, request.Model, CurrentUserName, ct);
            return NoContent();
        });
    }

    /// <summary>
    /// One harmless call (the model list) to a saved connection or the form's draft. A blank API key uses the saved one.
    /// Admin only, because it makes the server call the address given.
    /// </summary>
    [HttpPost("test")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> Test(TestAiConnectionRequest request, CancellationToken ct)
    {
        var errors = AiSettingsValidation.Test(request);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        var key = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        if (key is null && request.ConnectionId is { } id)
        {
            var (connections, _) = await settings.GetAsync(ct);
            if (connections.FirstOrDefault(c => c.ConnectionId == id)?.ApiKeyProtected is { } stored) key = protector.Unprotect(stored);
        }
        return Ok(await tester.TestAsync(request.Kind, request.Endpoint.Trim().TrimEnd('/'), key, request.TimeoutSeconds, ct));
    }

    private Task<IActionResult> Save(int? connectionId, SaveAiConnectionRequest request, CancellationToken ct)
    {
        var errors = AiSettingsValidation.Connection(request);
        if (errors.Count > 0) return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(errors)));
        var newKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        var action = newKey is not null ? "set" : request.ClearApiKey ? "clear" : "keep";
        return Run(async () =>
        {
            var id = await settings.SaveConnectionAsync(connectionId, request, action, newKey is null ? null : protector.Protect(newKey),
                newKey is null ? null : AiSettingsValidation.Hint(newKey), CurrentUserName, ct);
            return Ok(new { connectionId = id });
        });
    }

    /// <summary>Database rules -> 400 / 404 / 409; every successful change drops the cached settings.</summary>
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            var result = await action();
            resolver.Invalidate();
            return result;
        }
        catch (AiSettingsException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}

/// <summary>API keys of AI connections, encrypted with ASP.NET Data Protection (key ring set up in Program.cs).</summary>
public sealed class DataProtectionAiSecretProtector(IDataProtectionProvider provider) : IAiSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("LT.ODM.Ai.ApiKeys.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? Unprotect(string protectedValue)
    {
        try { return _protector.Unprotect(protectedValue); }
        catch (CryptographicException) { return null; }
    }
}
