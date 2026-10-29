using System.Text.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Concepts;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Concepts (Style Packages > Concepts; db/procedures/concept.procedures.sql): LT's own design ideas on library codes, with
/// inspiration pictures, the library styles they take from (design provenance) and an AI brief, checked for closeness to other
/// brands' designs before they are offered (roadmap 5.7, 5.1, 5.2). Reading: Style Library readers. Changing: Admin, Merchandiser,
/// on any concept.
/// </summary>
[ApiController]
[Route("api/v1/concepts")]
[Authorize(Roles = StyleRoles.Readers)]
public sealed class ConceptsController(IConceptRepository concepts, BrandClosenessService closeness, ILogger<ConceptsController> logger) : ControllerBase
{
    private const int MaxStyles = 100;
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    // ----- Reads -----

    /// <summary>Concepts, latest change first. mine = only concepts the signed-in user owns.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? search, string? customer, string? season, string? category, string? status, bool mine = false,
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        string? st = null;
        if (!string.IsNullOrWhiteSpace(status) && (st = ConceptValues.Parse(ConceptValues.Statuses, status)) is null)
            return Invalid("status", "Choose a status from the list.");
        return Ok(await concepts.ListAsync(new ConceptListQuery(Clean(search, 100), Clean(customer, 32), Clean(season, 16), Clean(category, 20)?.ToUpperInvariant(),
            st, mine, CurrentUserName, Math.Max(0, skip), Math.Clamp(take, 1, 200)), ct));
    }

    /// <summary>Target markets, trend tags and product categories (Settings > Reference lists).</summary>
    [HttpGet("lookups")]
    public Task<ConceptLookupsDto> Lookups(CancellationToken ct) => concepts.LookupsAsync(ct);

    [HttpGet("{conceptId:int}")]
    public async Task<IActionResult> Get(int conceptId, CancellationToken ct)
        => await concepts.GetAsync(conceptId, (await closeness.ThresholdsAsync(ct)).Model, ct) is { } c ? Ok(c) : NotFound();

    /// <summary>The concepts a style inspired (style page).</summary>
    [HttpGet("for-style/{styleId:int}")]
    public Task<IReadOnlyList<StyleConceptDto>> ForStyle(int styleId, CancellationToken ct) => concepts.ForStyleAsync(styleId, ct);

    /// <summary>
    /// Brand closeness (roadmap 5.2): reads pictures not read by the image model yet, then lists per picture the library styles
    /// that look closest. Another brand's style at or above the thresholds is Close / Check; the concept customer's own brand is
    /// never flagged.
    /// </summary>
    [HttpGet("{conceptId:int}/closeness")]
    public async Task<IActionResult> Closeness(int conceptId, CancellationToken ct)
        => await closeness.CheckConceptAsync(conceptId, ct) is { } c ? Ok(c) : NotFound();

    /// <summary>
    /// Proven styles to start the concept from, at once and without AI: the concept's category, gender, customer and fabric words
    /// scored against every style with a BOM. take: how many to return (1-25, default 12).
    /// </summary>
    [HttpGet("{conceptId:int}/library-matches")]
    public async Task<IActionResult> LibraryMatches(int conceptId, [FromServices] ConceptLibraryMatcher matcher, int take = ConceptLibraryMatcher.Shown,
        CancellationToken ct = default)
        => await matcher.FindAsync(conceptId, take, ct) is { } found ? Ok(found.Result) : NotFound();

    /// <summary>
    /// The AI (Text job) ranks the best-scored styles for the concept and says why each fits, what can be reused and what differs.
    /// One AI call (the criteria come from the concept, not from AI).
    /// </summary>
    [HttpPost("{conceptId:int}/library-matches/explain")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> ExplainMatches(int conceptId, [FromServices] ConceptLibraryMatcher matcher, [FromServices] ConceptMatcher ai,
        [FromServices] IAiJsonClient text, CancellationToken ct)
    {
        if (!(await text.GetInfoAsync(ct)).IsConfigured)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiUnavailable.Generic("AI explanations"));
        if (await matcher.FindAsync(conceptId, ConceptMatchRules.Candidates, ct) is not { } found) return NotFound();
        try
        {
            return Ok(await ai.ExplainAsync(ConceptLibraryMatcher.Request(found.Concept), found.Result.Criteria, found.Candidates, ct));
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }

    // ----- Concepts -----

    [HttpPost]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Create(SaveConceptRequest request, CancellationToken ct)
        => Run(Validate(request), async () => Ok(new { conceptId = await concepts.SaveAsync(null, Normalise(request) with { RowVer = null }, CurrentUserName, ct) }));

    [HttpPut("{conceptId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Update(int conceptId, SaveConceptRequest request, CancellationToken ct)
    {
        var errors = Validate(request);
        if (request.RowVer is not { Length: 8 }) errors["rowVer"] = ["Reload the concept and try again."];
        return Run(errors, async () => Ok(new { conceptId = await concepts.SaveAsync(conceptId, Normalise(request), CurrentUserName, ct) }));
    }

    [HttpDelete("{conceptId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Delete(int conceptId, string? rowVer, CancellationToken ct)
    {
        byte[]? value = null;
        try { if (!string.IsNullOrEmpty(rowVer)) value = Convert.FromBase64String(rowVer); } catch (FormatException) { }
        if (value is not { Length: 8 } v) return Task.FromResult(Invalid("rowVer", "Reload the concept and try again."));
        return Run([], async () =>
        {
            await concepts.DeleteAsync(conceptId, v, CurrentUserName, ct);
            return NoContent();
        });
    }

    /// <summary>
    /// Drafts the concept brief with AI (the Text job, as Concept Studio) from the concept as saved, and keeps it on the concept.
    /// Returns the brief and the concept's new RowVer.
    /// </summary>
    [HttpPost("{conceptId:int}/brief")]
    [Authorize(Roles = StyleRoles.Editors)]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> DraftBrief(int conceptId, [FromServices] IConceptDraftAi ai, CancellationToken ct)
    {
        if (await concepts.GetAsync(conceptId, null, ct) is not { } detail) return NotFound();
        if (!await ai.IsConfiguredAsync(ct))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiUnavailable.Generic("AI drafting"));
        var c = detail.Concept;
        ConceptDraftDto draft;
        try
        {
            draft = await ai.DraftAsync(new ConceptDraftRequest(c.Name, c.CustomerName ?? c.CustomerCode, c.SeasonCode, c.TargetMarketName, c.TargetPrice,
                detail.Tags.Select(t => t.Name).ToList(), c.ProductCategoryName, c.Gender, c.Description), ct);
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
        var json = JsonSerializer.Serialize(new { products = draft.Products, fabrics = draft.Fabrics, notes = draft.Notes });
        return await Run([], async () =>
        {
            var rowVer = await concepts.SaveBriefAsync(conceptId, draft.Summary, json, CurrentUserName, ct);
            return Ok(new
            {
                brief = new ConceptBriefDto(draft.Summary, draft.Products, draft.Fabrics, draft.Notes, CurrentUserName, DateTime.UtcNow), rowVer,
            });
        });
    }

    // ----- Develop as style (roadmap 5.21) -----

    /// <summary>
    /// Makes an LT design from the concept: a new style of the concept's customer (season and style number from the user), its
    /// sketch a concept picture, its BOM from one of the concept's inspiration styles (whole from the customer's own brand, know-how
    /// only from another). Then it is offered like any style (Add to package with the concept).
    /// </summary>
    [HttpPost("{conceptId:int}/develop")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Develop(int conceptId, DevelopStyleRequest request, [FromServices] ConceptStyleDeveloper developer, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var season = request.SeasonCode?.Trim().ToUpperInvariant();
        var styleNo = request.StyleNo?.Trim();
        if (string.IsNullOrEmpty(season)) errors["seasonCode"] = ["Choose the season of the new style."];
        else if (season.Length > 16) errors["seasonCode"] = ["A season code has at most 16 characters."];
        if (string.IsNullOrEmpty(styleNo)) errors["styleNo"] = ["Give the new style its own style number."];
        else if (styleNo.Length > 100) errors["styleNo"] = ["A style number has at most 100 characters."];
        return Run(errors, async () =>
        {
            var result = await developer.DevelopAsync(conceptId, request with { SeasonCode = season, StyleNo = styleNo }, CurrentUserName, ct);
            logger.LogInformation("Style {StyleId} developed from concept {ConceptId} by {User}.", result.StyleId, conceptId, CurrentUserName);
            return Ok(result);
        });
    }

    // ----- Inspiration pictures -----

    /// <summary>
    /// Uploads an inspiration picture (JPEG, PNG, GIF or WebP, at most 10 MB; type checked from the content, stored under a random
    /// name) and reads its look-alike vector with the image model for the closeness check (when AI is on).
    /// </summary>
    [HttpPost("{conceptId:int}/images")]
    [Authorize(Roles = StyleRoles.Editors)]
    [RequestSizeLimit(ConceptStudioRules.MaxImageBytes + 64 * 1024)]
    public async Task<IActionResult> AddImage(int conceptId, IFormFile? file, [FromForm] string? caption, [FromServices] IConceptImageStore images,
        CancellationToken ct)
    {
        if (caption?.Trim().Length > 200) return Invalid("caption", "Keep the caption under 200 characters.");
        var (name, problem) = await ImageUploads.SaveAsync(this, images, file, ct);
        if (problem is not null) return problem;
        var vector = await closeness.ReadAsync(name!, ct);
        var url = BrandClosenessService.ImagePrefix + name;
        logger.LogInformation("Concept {ConceptId} picture {Name} uploaded by {User}.", conceptId, name, CurrentUserName);
        return await Run([], async () => Ok(new
        {
            conceptImageId = await concepts.AddImageAsync(conceptId, url, Clean(caption, 200), vector, CurrentUserName, ct), imageUrl = url,
        }));
    }

    [HttpDelete("{conceptId:int}/images/{conceptImageId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> RemoveImage(int conceptId, int conceptImageId, CancellationToken ct)
        => Run([], async () =>
        {
            await concepts.RemoveImageAsync(conceptId, conceptImageId, ct);
            return NoContent();
        });

    /// <summary>
    /// Shows a picture on the concept's story page for the client, or not, and its caption. Showing it needs the brand closeness
    /// check to have read it and found no other brand's design too close (Check is allowed: the editor has looked).
    /// </summary>
    [HttpPut("{conceptId:int}/images/{conceptImageId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> SaveImage(int conceptId, int conceptImageId, SaveConceptImageRequest request, CancellationToken ct)
    {
        if (request.Caption?.Trim().Length > 200) return Invalid("caption", "Keep the caption under 200 characters.");
        if (request.ForClient)
        {
            if (await closeness.CheckConceptAsync(conceptId, ct) is not { } check) return NotFound();
            var picture = check.Images.FirstOrDefault(i => i.ConceptImageId == conceptImageId);
            if (picture is null) return Problem(statusCode: StatusCodes.Status404NotFound, title: "This picture is no longer on the concept.");
            if (picture.Status != "Checked")
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "This picture has not been checked against other brands' designs yet (the image model is off), so it cannot go to the client.");
            if (picture.Matches.FirstOrDefault(m => m.Level == ClosenessLevels.Close) is { } close)
                return Problem(statusCode: StatusCodes.Status409Conflict,
                    title: $"This picture looks like {close.CustomerCode}'s style {close.StyleNo}, so it cannot go to the client.");
        }
        return await Run([], async () =>
        {
            await concepts.SaveImageAsync(conceptId, conceptImageId, request.ForClient, Clean(request.Caption, 200), ct);
            return NoContent();
        });
    }

    /// <summary>Serves a concept picture to signed-in readers (the page fetches it with the sign-in token).</summary>
    [HttpGet("images/{fileName}")]
    [DisableRateLimiting]   // cheap files; one page can show dozens
    public Task<IActionResult> GetImage(string fileName, int? w, [FromServices] IConceptImageStore images, CancellationToken ct)
        => ImageUploads.ServeAsync(this, images, fileName, w, ct);

    // ----- Inspiration styles (design provenance) -----

    /// <summary>Records library styles as inspirations; one result per style (Added, AlreadyIn, NotFound).</summary>
    [HttpPost("{conceptId:int}/styles")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> AddStyles(int conceptId, AddConceptStylesRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.StyleIds is not { Count: > 0 and <= MaxStyles }) errors["styleIds"] = [$"Choose 1 to {MaxStyles} styles."];
        string? from = null;
        if (!string.IsNullOrWhiteSpace(request.AddedFrom) && (from = ConceptValues.Parse(ConceptValues.StyleSources, request.AddedFrom)) is null)
            errors["addedFrom"] = ["Unknown place the styles were found."];
        return Run(errors, async () => Ok(await concepts.AddStylesAsync(conceptId, request.StyleIds!, from ?? "Pick", CurrentUserName, ct)));
    }

    /// <summary>What the concept takes from the style (fabric, construction, fit, ...).</summary>
    [HttpPut("{conceptId:int}/styles/{styleId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> SaveStyle(int conceptId, int styleId, SaveConceptStyleRequest request, CancellationToken ct)
    {
        if (request.Note?.Trim().Length > 500) return Task.FromResult(Invalid("note", "Keep the note under 500 characters."));
        return Run([], async () =>
        {
            await concepts.SaveStyleAsync(conceptId, styleId, Clean(request.Note, 500), ct);
            return NoContent();
        });
    }

    [HttpDelete("{conceptId:int}/styles/{styleId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> RemoveStyle(int conceptId, int styleId, CancellationToken ct)
        => Run([], async () =>
        {
            await concepts.RemoveStyleAsync(conceptId, styleId, ct);
            return NoContent();
        });

    // ----- Helpers -----

    internal static Dictionary<string, string[]> Validate(SaveConceptRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name)) errors["name"] = ["Give the concept a name."];
        else if (r.Name.Trim().Length > 150) errors["name"] = ["Keep the name under 150 characters."];
        if (r.CustomerCode?.Trim().Length > 32) errors["customerCode"] = ["A customer code has at most 32 characters."];
        if (r.SeasonCode?.Trim().Length > 16) errors["seasonCode"] = ["A season code has at most 16 characters."];
        if (r.TargetMarketCode?.Trim().Length > 16) errors["targetMarketCode"] = ["Choose a target market from the list."];
        if (r.ProductCategoryCode?.Trim().Length > 20) errors["productCategoryCode"] = ["Choose a product category from the list."];
        if (!string.IsNullOrWhiteSpace(r.Gender) && ConceptValues.Parse(ConceptValues.Genders, r.Gender) is null) errors["gender"] = ["Choose a gender from the list."];
        if (r.TargetPrice?.Trim().Length > 50) errors["targetPrice"] = ["Keep the target price under 50 characters."];
        if (r.Description?.Trim().Length > 2000) errors["description"] = ["Keep the description under 2,000 characters."];
        if (r.ClientStory?.Trim().Length > 2000) errors["clientStory"] = ["Keep the story under 2,000 characters."];
        if (!string.IsNullOrWhiteSpace(r.Status) && ConceptValues.Parse(ConceptValues.Statuses, r.Status) is null) errors["status"] = ["Choose a status from the list."];
        if (r.OwnerName?.Trim().Length > 64) errors["ownerName"] = ["A user name has at most 64 characters."];
        if (r.TrendTags is { } tags && (tags.Count > ConceptValues.MaxTags || tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Trim().Length > 40)))
            errors["trendTags"] = [$"Choose at most {ConceptValues.MaxTags} trend tags from the list."];
        return errors;
    }

    /// <summary>Trimmed text; codes upper case as the library stores them; status and gender as the database spells them.</summary>
    internal static SaveConceptRequest Normalise(SaveConceptRequest r)
        => r with
        {
            Name = r.Name!.Trim(), CustomerCode = Clean(r.CustomerCode, 32)?.ToUpperInvariant(), SeasonCode = Clean(r.SeasonCode, 16)?.ToUpperInvariant(),
            TargetMarketCode = Clean(r.TargetMarketCode, 16)?.ToUpperInvariant(), ProductCategoryCode = Clean(r.ProductCategoryCode, 20)?.ToUpperInvariant(),
            Gender = ConceptValues.Parse(ConceptValues.Genders, r.Gender), TargetPrice = Clean(r.TargetPrice, 50), Description = Clean(r.Description, 2000),
            ClientStory = Clean(r.ClientStory, 2000),
            Status = ConceptValues.Parse(ConceptValues.Statuses, r.Status) ?? "Draft", OwnerName = Clean(r.OwnerName, 64),
            TrendTags = r.TrendTags?.Select(t => t.Trim().ToUpperInvariant()).Distinct().ToList(),
        };

    private static string? Clean(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();

    private IActionResult Invalid(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    /// <summary>Validation errors -> 400 ValidationProblem; database rules -> 400 / 404 / 409 Problem with the reason.</summary>
    private async Task<IActionResult> Run(Dictionary<string, string[]> errors, Func<Task<IActionResult>> action)
    {
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
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
