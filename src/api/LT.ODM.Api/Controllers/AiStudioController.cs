using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// AI Studio on the Style Library: smart search, change summary, BOM check and style renders.
/// Reading: Style Library readers (Admin, Merchandiser, Costing, Viewer). Making or deleting renders: Admin, Merchandiser.
/// The comparison and the BOM check are rules and work without AI; AI calls are rate limited and answer 503 when no
/// provider is set up (Settings > AI connections, else appsettings Ai:Provider / Ai:ImageProvider).
/// </summary>
[ApiController]
[Route("api/v1/ai")]
[Authorize(Roles = StyleAiRoles.Readers)]
public sealed class AiStudioController(StyleAiAssistant assistant, IAiJsonClient text, IAiImageClient images) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    /// <summary>Which providers answer and whether data leaves the company network (shown on every AI Studio page).</summary>
    [HttpGet("status")]
    public Task<AiStatusDto> Status(CancellationToken ct) => assistant.StatusAsync(ct);

    // ----- Smart search -----

    [HttpPost("style-search")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<IActionResult> Search(StyleSearchRequest request, CancellationToken ct)
    {
        var query = request.Query?.Trim() ?? "";
        if (query.Length is < 3 or > 300)
            return Task.FromResult<IActionResult>(Invalid("query", "Describe the styles you are looking for in 3 to 300 characters."));
        return WithText(async () => Ok(await assistant.SearchAsync(query, ct)));
    }

    // ----- Change summary -----

    /// <summary>The differences, by rules (no AI). fromStyleId defaults to the style this one was reused from.</summary>
    [HttpGet("style-compare")]
    public Task<IActionResult> Compare(int styleId, int? fromStyleId, CancellationToken ct)
        => Run(async () => Ok(await assistant.CompareAsync(styleId, fromStyleId, ct)));

    [HttpPost("style-compare/summary")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<IActionResult> Summarize(StyleCompareRequest request, CancellationToken ct)
        => WithText(async () => Ok(await assistant.SummarizeAsync(await assistant.CompareAsync(request.StyleId, request.FromStyleId, ct), ct)));

    // ----- BOM check -----

    /// <summary>Rule findings for one style, or a customer and/or season (nothing = the whole library). No AI.</summary>
    [HttpGet("bom-check")]
    public Task<IActionResult> BomCheck(int? styleId, string? customer, string? season, CancellationToken ct)
        => Run(async () => Ok(await assistant.BomCheckAsync(Query(styleId, customer, season), ct)));

    [HttpPost("bom-check/explain")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<IActionResult> Explain(BomCheckQuery request, CancellationToken ct)
        => WithText(async () => Ok(await assistant.ExplainAsync(await assistant.BomCheckAsync(Query(request.StyleId, request.Customer, request.Season), ct), ct)));

    // ----- Concept to existing styles (from Concept Studio) -----

    /// <summary>Proven library styles to start a concept from: AI reads the concept, SQL scores the library, AI ranks and explains.</summary>
    [HttpPost("concept-matches")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<IActionResult> ConceptMatches(ConceptMatchRequest request, [FromServices] ConceptMatcher matcher, CancellationToken ct)
    {
        var errors = ConceptMatchRules.Validate(request);
        if (errors.Count > 0) return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(errors)));
        return WithText(async () => Ok(await matcher.MatchAsync(request, ct)));
    }

    // ----- Material description reader (Materials > Description reader) -----

    /// <summary>Materials with their reading; status = Unread | Pending | Accepted | Rejected | Outdated. Counts cover the scope.</summary>
    [HttpGet("material-specs")]
    public Task<MaterialSpecPageDto> MaterialSpecs([FromServices] IMaterialSpecRepository specs, string? contentClass, string? materialType, string? status,
        string? search, int skip = 0, int take = 50, CancellationToken ct = default)
        => specs.ListAsync(new MaterialSpecQuery(Clean(contentClass, 8), Clean(materialType, 16),
            MaterialSpecRules.States.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase)), Clean(search, 100),
            Math.Max(0, skip), Math.Clamp(take, 1, 200)), ct);

    [HttpGet("material-specs/{materialId:int}")]
    public async Task<IActionResult> MaterialSpec([FromServices] IMaterialSpecRepository specs, int materialId, CancellationToken ct)
        => await specs.GetAsync(materialId, ct) is { } spec ? Ok(spec) : NotFound();

    /// <summary>Reads the next 25 unread (or changed) descriptions in the scope with the Text job; stored as Pending.</summary>
    [HttpPost("material-specs/read")]
    [Authorize(Roles = StyleAiRoles.Editors)]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<IActionResult> ReadMaterialSpecs(ReadMaterialSpecsRequest request, [FromServices] MaterialReader reader, CancellationToken ct)
        => WithText(async () => Ok(await reader.ReadNextAsync(request with { ContentClass = Clean(request.ContentClass, 8), MaterialType = Clean(request.MaterialType, 16) },
            CurrentUserName, ct)));

    /// <summary>Accept or reject readings (only readings of the current description can be accepted).</summary>
    [HttpPost("material-specs/review")]
    [Authorize(Roles = StyleAiRoles.Editors)]
    public Task<IActionResult> ReviewMaterialSpecs(ReviewMaterialSpecsRequest request, [FromServices] IMaterialSpecRepository specs, CancellationToken ct)
    {
        if (request.MaterialIds is not { Count: > 0 and <= 500 } ids || !MaterialSpecRules.ReviewStatuses.Contains(request.Status))
            return Task.FromResult<IActionResult>(Invalid("materialIds", "Choose up to 500 readings and Accepted or Rejected."));
        return Run(async () => Ok(new { updated = await specs.ReviewAsync(ids.Distinct().ToList(), request.Status!, CurrentUserName, ct) }));
    }

    // ----- Renders -----

    /// <summary>Colorways, the facts and default prompt for the chosen colorway, and earlier renders.</summary>
    [HttpGet("style-render/{styleId:int}")]
    public Task<IActionResult> RenderBrief(int styleId, int? colorwayId, CancellationToken ct)
        => Run(async () => Ok(await assistant.RenderBriefAsync(styleId, colorwayId, ct)));

    /// <summary>Has the text model rewrite the prompt from the style's facts.</summary>
    [HttpPost("style-render/{styleId:int}/prompt")]
    [Authorize(Roles = StyleAiRoles.Editors)]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<IActionResult> WritePrompt(int styleId, RenderPromptRequest request, CancellationToken ct)
        => WithText(async () => Ok(await assistant.WritePromptAsync(styleId, request.ColorwayId, ct)));

    [HttpPost("style-render/{styleId:int}")]
    [Authorize(Roles = StyleAiRoles.Editors)]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<IActionResult> Render(int styleId, RenderRequest request, CancellationToken ct)
    {
        if ((request.Prompt?.Length ?? 0) > RenderRules.MaxPrompt)
            return Task.FromResult<IActionResult>(Invalid("prompt", $"Keep the description to {RenderRules.MaxPrompt} characters."));
        return Run(async () => await images.GetInfoAsync(ct) is { IsConfigured: true }
            ? Ok(await assistant.RenderAsync(styleId, request, CurrentUserName, ct))
            : NotSetUp("Image generation", await images.GetInfoAsync(ct)));
    }

    [HttpDelete("style-render/{styleId:int}/renders/{renderId:int}")]
    [Authorize(Roles = StyleAiRoles.Editors)]
    public Task<IActionResult> DeleteRender(int styleId, int renderId, CancellationToken ct)
        => Run(async () =>
        {
            await assistant.DeleteRenderAsync(styleId, renderId, ct);
            return NoContent();
        });

    // ----- Helpers -----

    private static BomCheckQuery Query(int? styleId, string? customer, string? season)
        => new(styleId is > 0 ? styleId : null, Clean(customer, 32), Clean(season, 16));

    private static string? Clean(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();

    private IActionResult Invalid(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    private ObjectResult NotSetUp(string feature, AiProviderInfo info)
        => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiUnavailable.Message(feature, info));

    private async Task<IActionResult> WithText(Func<Task<IActionResult>> action)
        => await text.GetInfoAsync() is { IsConfigured: true } ? await Run(action) : NotSetUp("This AI feature", await text.GetInfoAsync());

    /// <summary>Rule errors -> 400 / 404 / 409 with the reason; AI failures -> 502 with a readable message.</summary>
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
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }
}
