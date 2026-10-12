using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// AI Studio > Image search: find library styles by an uploaded photo, by words, or by another style's picture.
/// Look-alike vectors come from the image model built into the API; descriptions from in-house servers only (the Vision
/// and Embedding jobs, OpenAI-compatible). Reading and searching: Style Library
/// readers; building the index: its editors. Uploaded photos are read and then dropped, never stored.
/// </summary>
[ApiController]
[Route("api/v1/ai/image-search")]
[Authorize(Roles = StyleAiRoles.Readers)]
public sealed class ImageSearchController(ImageSearchService search, IAiVisionClient vision, IAiEmbeddingClient embedding) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    /// <summary>Both jobs and how far the index is for their current models.</summary>
    [HttpGet("status")]
    public Task<ImageSearchStatusDto> Status(CancellationToken ct) => search.StatusAsync(ct);

    /// <summary>Indexes the next pictures (about 45 s of work per call); the screen calls it again while pictures remain.</summary>
    [HttpPost("index")]
    [Authorize(Roles = StyleAiRoles.Editors)]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> Index(CancellationToken ct)
        => await NeitherReady(ct) ?? await Run(async () => Ok(await search.IndexNextAsync(CurrentUserName, ct)));

    /// <summary>
    /// Styles whose pictures match the words (e.g. "navy hooded track jacket with white side stripes"): by the built-in
    /// model's text part, or (useAiServer, or the model's text part not installed) by the descriptions.
    /// </summary>
    [HttpPost("words")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> Words(ImageSearchTextRequest request, CancellationToken ct)
    {
        var query = request.Query?.Trim() ?? "";
        if (query.Length is < 3 or > 300)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["query"] = ["Describe the garment in 3 to 300 characters."] }));
        var byLook = !request.UseAiServer && (await search.ReadinessAsync(ct)).LookAlikeWords;
        return (byLook ? null : await NotReady(ct, needsVision: false))
               ?? await Run(async () => Ok(await search.SearchByWordsAsync(query, Customer(request.Customer), request.UseAiServer, ct)));
    }

    /// <summary>
    /// Styles whose pictures look like the uploaded photo (JPEG, PNG, GIF or WebP, at most 10 MB; not stored): by the
    /// built-in model, or (useAiServer, or the model not installed) by the vision model's description.
    /// </summary>
    [HttpPost("photo")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    [RequestSizeLimit(ConceptStudioRules.MaxImageBytes + 64 * 1024)]
    public async Task<IActionResult> Photo(IFormFile? file, [FromForm] string? customer, [FromForm] bool useAiServer, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Choose a photo.");
        if (file.Length > ConceptStudioRules.MaxImageBytes) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Photos must be 10 MB or smaller.");
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (ConceptStudioRules.DetectImage(bytes) is not { } kind)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only JPEG, PNG, GIF and WebP photos can be used.");
        var byLook = (await search.ReadinessAsync(ct)).CanUseLookAlike(useAiServer);
        return (byLook ? null : await NotReady(ct, needsVision: true))
               ?? await Run(async () => Ok(await search.SearchByPhotoAsync(bytes, kind, Customer(customer), useAiServer, ct)));
    }

    /// <summary>Styles that look like this one, from the stored vectors (no AI call).</summary>
    [HttpGet("similar/{styleId:int}")]
    public Task<IActionResult> Similar(int styleId, string? customer, CancellationToken ct)
        => Run(async () => Ok(await search.SimilarAsync(styleId, Customer(customer), ct)));

    private static string? Customer(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > 32 ? v[..32] : value.Trim();

    /// <summary>
    /// Indexing needs the built-in look-alike model (installed, AI switched on) or else the Vision and Embedding jobs;
    /// 503 with the jobs' reason when neither can run.
    /// </summary>
    private async Task<IActionResult?> NeitherReady(CancellationToken ct)
        => (await search.ReadinessAsync(ct)).LookAlike ? null : await NotReady(ct, needsVision: true);

    /// <summary>503 with the reason when a job the call needs cannot run (not set up, switched off, cloud blocked).</summary>
    private async Task<IActionResult?> NotReady(CancellationToken ct, bool needsVision)
    {
        if (needsVision && await vision.GetInfoAsync(ct) is { IsConfigured: false } v)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiUnavailable.Message("Picture reading", v));
        if (await embedding.GetInfoAsync(ct) is { IsConfigured: false } e)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiUnavailable.Message("Image search", e));
        return null;
    }

    /// <summary>AI failures -> 502 with a readable message.</summary>
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }
}
