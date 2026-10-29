using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Client profiles (Style Packages > Client profiles, roadmap 5.13): what a client accepts (price band, minimum recycled share,
/// restricted words, preferred mills) and when it looks at offers (pitch dates). One profile per brand, or per customer without a
/// brand. Packages and What to offer are checked against it. Reading: Style Library readers; changing: Admin, Merchandiser.
/// </summary>
[ApiController]
[Authorize(Roles = StyleRoles.Readers)]
public sealed class ClientProfilesController(IClientProfileRepository profiles) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    [HttpGet("api/v1/client-profiles")]
    public Task<IReadOnlyList<ClientSummaryDto>> List(CancellationToken ct) => profiles.ListAsync(ct);

    /// <summary>The client's profile, or an empty one (rowVer null) when none is set yet.</summary>
    [HttpGet("api/v1/client-profiles/{clientKey}")]
    public async Task<IActionResult> Get(string clientKey, CancellationToken ct)
        => clientKey.Length > 32 ? NotFound() : Ok(await profiles.GetAsync(clientKey.Trim(), ct));

    [HttpPut("api/v1/client-profiles/{clientKey}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> Save(string clientKey, SaveClientProfileRequest request, CancellationToken ct)
    {
        var errors = ClientProfileRules.Validate(request);
        if (clientKey.Length > 32) errors["clientKey"] = ["Unknown client."];
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        try
        {
            await profiles.SaveAsync(clientKey.Trim(), request with
            {
                RestrictedTerms = Clean(request.RestrictedTerms), AccountManager = Clean(request.AccountManager), Notes = Clean(request.Notes),
                Mills = request.Mills?.Select(m => m.Trim()).Distinct().ToList(),
                PitchDates = request.PitchDates?.Select(d => d with { SeasonCode = Clean(d.SeasonCode)?.ToUpperInvariant(), Note = Clean(d.Note) }).ToList(),
            }, CurrentUserName, ct);
            return NoContent();
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    /// <summary>The package's styles against its client's profile (no issues and clientKey null when the client has none).</summary>
    [HttpGet("api/v1/style-packages/{packageId:int}/client-check")]
    public async Task<IActionResult> CheckPackage(int packageId, [FromServices] IPackageRepository packages, CancellationToken ct)
    {
        if (await packages.GetAsync(packageId, ct) is not { } package) return NotFound();
        return Ok(await profiles.CheckAsync(package.Package.CustomerCode, package.Items.Select(i => i.StyleId).ToList(), ct));
    }

    // ----- Follow-up reminders (roadmap 5.24) -----

    /// <summary>The signed-in user's follow-ups (links not opened or answered, links ending, sample requests waiting, pitch dates coming).</summary>
    [HttpGet("api/v1/follow-ups")]
    public Task<IReadOnlyList<FollowUpDto>> FollowUps([FromServices] IOptions<FollowUpOptions> options, CancellationToken ct)
        => profiles.FollowUpsAsync(CurrentUserName, options.Value, ct);

    /// <summary>Done (days empty) or Later (1-30 days).</summary>
    [HttpPost("api/v1/follow-ups/dismiss")]
    public async Task<IActionResult> Dismiss(DismissFollowUpRequest request, CancellationToken ct)
    {
        var kind = ClientProfileRules.FollowUpKinds.FirstOrDefault(k => string.Equals(k, request.Kind, StringComparison.OrdinalIgnoreCase));
        var errors = new Dictionary<string, string[]>();
        if (kind is null) errors["kind"] = ["Unknown kind of follow-up."];
        if (string.IsNullOrWhiteSpace(request.RefKey) || request.RefKey.Trim().Length > 40) errors["refKey"] = ["Unknown follow-up."];
        if (request.Days is < 1 or > 30) errors["days"] = ["Put it off for 1 to 30 days."];
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        await profiles.DismissFollowUpAsync(CurrentUserName, kind!, request.RefKey!.Trim(), request.Days, ct);
        return NoContent();
    }
}
