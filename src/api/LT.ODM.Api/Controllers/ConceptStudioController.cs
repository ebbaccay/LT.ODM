using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Concept Studio (ported from TMS): AI concept brief and inspiration-board images. Signed-in users only.
/// Saved concepts, trend tags and target markets still use the TMS procedures through /hubs/sp.
/// </summary>
[ApiController]
[Route("api/v1/concept-studio")]
public sealed class ConceptStudioController(IConceptDraftAi ai, IConceptImageStore images, ILogger<ConceptStudioController> logger) : ControllerBase
{
    /// <summary>Writes a concept brief with AI (replaces the TMS hub method StreamConceptDraftStructured).</summary>
    [HttpPost("draft")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<ActionResult<ConceptDraftDto>> Draft(ConceptDraftRequest request, CancellationToken ct)
    {
        var errors = ConceptStudioRules.Draft(request);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        if (!await ai.IsConfiguredAsync(ct))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: AiUnavailable.Generic("AI drafting"));
        try
        {
            return await ai.DraftAsync(request, ct);
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }

    /// <summary>
    /// Uploads one inspiration image (JPEG, PNG, GIF or WebP, at most 10 MB; the screen sends compressed JPEGs).
    /// The type is checked from the file content; the stored name is random. Returns the URL to save with the concept.
    /// </summary>
    [HttpPost("images")]
    [RequestSizeLimit(ConceptStudioRules.MaxImageBytes + 64 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken ct)
    {
        var (name, problem) = await ImageUploads.SaveAsync(this, images, file, ct);
        if (problem is not null) return problem;
        logger.LogInformation("Concept inspiration image {Name} uploaded by {User}.", name, User.Identity?.Name);
        return Ok(new { imageUrl = $"/api/v1/concept-studio/images/{name}" });
    }

    /// <summary>Serves an uploaded image to signed-in users (the screen fetches it with the sign-in token).</summary>
    [HttpGet("images/{fileName}")]
    public IActionResult GetImage(string fileName) => ImageUploads.Serve(this, images, fileName);
}
