using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// A style package opened by the client through its share link, without signing in (roadmap 5.16). The token in the address
/// is the link's secret; only its SHA-256 is stored. Anything wrong (unknown, revoked or expired link, a style or picture not
/// in the package) answers 404 the same way, so a guessed link learns nothing. The client sees the line sheet's content
/// (offered colourways, notes for the client; never internal notes or another brand's style), answers Like / Pass and
/// comments per style, and downloads the PDF. 60 requests per minute per IP (pictures count only in the global limit).
/// </summary>
[ApiController]
[Route("api/v1/share/{token}")]
[AllowAnonymous]
public sealed class SharedPackagesController(IPackageRepository packages, IPackageNotifier notifier, ILogger<SharedPackagesController> logger) : ControllerBase
{
    /// <summary>
    /// The package as the client sees it, with the answers of <paramref name="name"/> (the person visiting; several people
    /// can use one link); each call counts as an opening (logged with IP and browser).
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Share)]
    public async Task<IActionResult> Get(string token, string? name, CancellationToken ct)
    {
        if (await OpenAsync(token, count: true, ct) is not { } share || await packages.GetAsync(share.PackageId, ct) is not { } package) return NotFound();
        NoStore();
        return Ok(SharedPackageView.Build(package, share, name is { Length: > 100 } ? name[..100] : name));
    }

    /// <summary>
    /// The client's answer on one style: Like / Pass / none and a comment (both empty removes it). What is new goes to the
    /// package owner and the link maker as a notice (the bell), pushed to their open app pages.
    /// </summary>
    [HttpPut("items/{packageItemId:int}/feedback")]
    [EnableRateLimiting(RateLimitPolicies.Share)]
    public async Task<IActionResult> Feedback(string token, int packageItemId, ShareFeedbackRequest request, CancellationToken ct)
    {
        if (!ShareTokens.LooksValid(token)) return NotFound();
        var verdict = request.Verdict?.Trim() is { Length: > 0 } v ? PackageValues.Parse(["Like", "Pass"], v) : null;
        if (request.Verdict?.Trim() is { Length: > 0 } && verdict is null) return Invalid("verdict", "Choose Like or Pass.");
        if (request.Comment?.Trim().Length > 1000) return Invalid("comment", "Keep the comment under 1,000 characters.");
        if (request.Name?.Trim().Length > 100) return Invalid("name", "Keep the name under 100 characters.");
        try
        {
            var notices = await packages.SaveShareFeedbackAsync(ShareTokens.Hash(token), packageItemId, request with { Verdict = verdict }, ct);
            // The answer is saved; a notice that cannot be pushed now is still in the bell at the next page load.
            try
            {
                await notifier.NotifyAsync(notices, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not push {Count} package notices", notices.Count);
            }
            return NoContent();
        }
        catch (StyleRuleException ex)
        {
            return ex.StatusCode == 404 ? NotFound() : Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    /// <summary>A picture on the client's page (a style or colourway picture of the package only); width 160 / 320 / 640.</summary>
    [HttpGet("images/{fileName}")]
    public async Task<IActionResult> Image(string token, string fileName, int? width, [FromServices] IStyleImageStore images, CancellationToken ct)
    {
        if (!ShareTokens.LooksValid(token) || !await packages.ShareImageAllowedAsync(ShareTokens.Hash(token), SharedPackageView.ImageUrl(fileName), ct))
            return NotFound();
        return await ImageUploads.ServeAsync(this, images, fileName, width, ct);
    }

    /// <summary>The line sheet PDF, as the package page makes it (not counted as an opening).</summary>
    [HttpGet("line-sheet.pdf")]
    [EnableRateLimiting(RateLimitPolicies.Share)]
    public async Task<IActionResult> LineSheet(string token, [FromServices] IPackageLineSheetWriter writer, CancellationToken ct)
    {
        if (await OpenAsync(token, count: false, ct) is not { } share || await packages.GetAsync(share.PackageId, ct) is not { } package) return NotFound();
        NoStore();
        var pdf = await writer.WritePdfAsync(package, $"share link: {share.Label}", DateTime.UtcNow, ct);
        var name = string.Concat(package.Package.Name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return File(pdf, "application/pdf", $"{name}_line_sheet.pdf");
    }

    private Task<OpenedShare?> OpenAsync(string token, bool count, CancellationToken ct)
        => ShareTokens.LooksValid(token)
            ? packages.OpenShareAsync(ShareTokens.Hash(token), HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), count, ct)
            : Task.FromResult<OpenedShare?>(null);

    /// <summary>The page holds the client's answers and may sit on a shared computer: never kept by browsers or proxies.</summary>
    private void NoStore() => Response.Headers.CacheControl = "no-store";

    private IActionResult Invalid(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));
}
