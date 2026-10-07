using LT.ODM.Application.ConceptStudio;
using Microsoft.AspNetCore.Mvc;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// SBU overview (ported from TMS business-unit): product photos. Signed-in users only.
/// The product records themselves still use the TMS procedures through /hubs/sp.
/// Replaces the TMS api/upload/product-image endpoints, which allowed anonymous uploads and deletes.
/// </summary>
[ApiController]
[Route("api/v1/sbu-products")]
public sealed class SbuProductsController(IProductImageStore images, ILogger<SbuProductsController> logger) : ControllerBase
{
    /// <summary>Uploads one product photo (JPEG, PNG, GIF or WebP, at most 10 MB). Returns the URL to save with the product.</summary>
    [HttpPost("images")]
    [RequestSizeLimit(ConceptStudioRules.MaxImageBytes + 64 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken ct)
    {
        var (name, problem) = await ImageUploads.SaveAsync(this, images, file, ct);
        if (problem is not null) return problem;
        logger.LogInformation("SBU product image {Name} uploaded by {User}.", name, User.Identity?.Name);
        return Ok(new { imageUrl = $"/api/v1/sbu-products/images/{name}" });
    }

    /// <summary>Serves a product photo to signed-in users (the screen fetches it with the sign-in token).</summary>
    [HttpGet("images/{fileName}")]
    public IActionResult GetImage(string fileName) => ImageUploads.Serve(this, images, fileName);
}
