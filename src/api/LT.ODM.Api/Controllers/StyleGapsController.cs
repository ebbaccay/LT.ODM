using LT.ODM.Api.Jobs;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Style Library > Fill the gaps: suggestions for missing genders and product types, season names to merge, and empty LCO
/// consumption. Reading: Style Library readers. Accepting, dismissing, merging, filling and asking AI: Admin, Merchandiser.
/// Suggestions come from rules (db/procedures/style.gaps.procedures.sql); AI only reads the styles the rules could not, and
/// its answers are suggestions too. Nothing changes a style until someone accepts.
/// </summary>
[ApiController]
[Route("api/v1/style-gaps")]
[Authorize(Roles = StyleRoles.Readers)]
public sealed class StyleGapsController(IStyleGapRepository gaps) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    /// <summary>Counts of what is missing, with how many suggestions are ready (about 1.5 s: suggestions are worked out).</summary>
    [HttpGet("summary")]
    public Task<StyleGapSummaryDto> Summary(CancellationToken ct) => gaps.SummaryAsync(withSuggestions: true, ct);

    // ----- Gender and product type -----

    /// <summary>Styles missing the field. show = suggested (default) | none | all; minConfidence 1 (Check) to 3 (High).</summary>
    [HttpGet("{field}")]
    public async Task<IActionResult> List(string field, string? customer, string? season, string? show, int minConfidence = 1, int skip = 0,
        int take = 50, CancellationToken ct = default)
    {
        if (GapFields.Parse(field) is not { } f) return NotFound();
        var mode = show?.Trim().ToLowerInvariant() is "none" or "all" ? show.Trim().ToLowerInvariant() : "suggested";
        return Ok(await gaps.ListAsync(new GapListQuery(f, Clean(customer, 32), Clean(season, 16), mode, Math.Clamp(minConfidence, 1, 3),
            Math.Max(0, skip), Math.Clamp(take, 1, 200)), ct));
    }

    /// <summary>Accepts the listed suggestions (or values chosen by hand), or every suggestion in the scope with at least minConfidence.</summary>
    [HttpPost("{field}/accept")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Accept(string field, AcceptGapsRequest request, CancellationToken ct)
    {
        if (GapFields.Parse(field) is not { } f) return Task.FromResult<IActionResult>(NotFound());
        if (request.Items is { Count: > 1000 }) return Task.FromResult(Invalid("items", "Accept at most 1,000 styles at a time."));
        return Run(async () => Ok(new
        {
            applied = await gaps.AcceptAsync(request with { Customer = Clean(request.Customer, 32), Season = Clean(request.Season, 16) }, f, CurrentUserName, ct),
        }));
    }

    /// <summary>Turns suggestions down; the same value is not offered again for those styles.</summary>
    [HttpPost("{field}/dismiss")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Dismiss(string field, DismissGapsRequest request, CancellationToken ct)
    {
        if (GapFields.Parse(field) is not { } f) return Task.FromResult<IActionResult>(NotFound());
        if (request.Items is not { Count: > 0 and <= 1000 } items) return Task.FromResult(Invalid("items", "Choose 1 to 1,000 suggestions."));
        return Run(async () => Ok(new { dismissed = await gaps.DismissAsync(f, items, CurrentUserName, ct) }));
    }

    /// <summary>Asks the Text job about the next 25 styles the rules could not read; their answers become suggestions. An AI job: 202 with its id.</summary>
    [HttpPost("{field}/ask-ai")]
    [Authorize(Roles = StyleRoles.Editors)]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> AskAi(string field, AskAiGapsRequest request, [FromServices] AiJobQueue jobs, [FromServices] IAiJsonClient text,
        CancellationToken ct)
    {
        if (GapFields.Parse(field) is not { } f) return NotFound();
        var info = await text.GetInfoAsync(ct);
        if (!info.IsConfigured) return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiUnavailable.Message("This AI feature", info));
        var (customer, season, user) = (Clean(request.Customer, 32), Clean(request.Season, 16), CurrentUserName);
        return await this.QueueAiJobAsync(jobs, AiJobKinds.GapsAskAi, AiJobLanes.Text, new { field = f, customer, season },
            async (sp, jct) => await sp.GetRequiredService<GapAdvisor>().AskNextAsync(f, customer, season, user, jct), ct);
    }

    // ----- Seasons -----

    [HttpGet("seasons")]
    public Task<IReadOnlyList<GapSeasonDto>> Seasons(CancellationToken ct) => gaps.SeasonsAsync(ct);

    /// <summary>Moves a season's styles into a year-term season (e.g. FA26 into 2026-FA) and remembers it for later imports.</summary>
    [HttpPost("seasons/merge")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> MergeSeason(MergeSeasonRequest request, CancellationToken ct)
    {
        if (Clean(request.From, 16) is not { } from || Clean(request.To, 16) is not { } to)
            return Task.FromResult(Invalid("to", "Choose the season to merge and the year-term season to merge it into (e.g. 2026-FA)."));
        return Run(async () => Ok(await gaps.MergeSeasonAsync(from, to, CurrentUserName, ct)));
    }

    // ----- LCO consumption -----

    /// <summary>BOM lines without LCO per customer and season, and how many have a suggestion.</summary>
    [HttpGet("lco")]
    public Task<IReadOnlyList<LcoGroupDto>> LcoSummary(bool withAllowance = false, CancellationToken ct = default) => gaps.LcoSummaryAsync(withAllowance, ct);

    /// <summary>One style's BOM lines without LCO, with the suggested figure and where it comes from.</summary>
    [HttpGet("lco/styles/{styleId:int}")]
    public Task<IReadOnlyList<LcoLineDto>> LcoForStyle(int styleId, bool withAllowance = false, CancellationToken ct = default)
        => gaps.LcoForStyleAsync(styleId, withAllowance, ct);

    /// <summary>Fills empty LCO from the suggestions and marks it as an estimate (lines, a style, a customer / season, or all).</summary>
    [HttpPost("lco/apply")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> ApplyLco(ApplyLcoRequest request, CancellationToken ct)
    {
        if (request.BomLineIds is { Count: > 2000 }) return Task.FromResult(Invalid("bomLineIds", "Fill at most 2,000 lines at a time."));
        var r = request with { Customer = Clean(request.Customer, 32), Season = Clean(request.Season, 16) };
        if (r.BomLineIds is not { Count: > 0 } && r.StyleId is null && r.Customer is null && r.Season is null && !r.All)
            return Task.FromResult(Invalid("all", "Choose lines, a style, a customer or a season (or all)."));
        return Run(async () => Ok(await gaps.ApplyLcoAsync(r, CurrentUserName, ct)));
    }

    // ----- Helpers -----

    private static string? Clean(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();

    private IActionResult Invalid(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}
