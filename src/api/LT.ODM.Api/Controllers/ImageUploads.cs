using LT.ODM.Application.ConceptStudio;
using Microsoft.AspNetCore.Mvc;

namespace LT.ODM.Api.Controllers;

/// <summary>Shared upload and download handling for image stores (Concept Studio, SBU products).</summary>
internal static class ImageUploads
{
    /// <summary>
    /// Checks the upload (JPEG, PNG, GIF or WebP by content, at most 10 MB) and saves it, made smaller, under a random name.
    /// Returns the stored file name, or a 400 problem.
    /// </summary>
    public static async Task<(string? Name, IActionResult? Problem)> SaveAsync(ControllerBase controller, IImageStore store, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return (null, controller.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Choose an image to upload."));
        if (file.Length > ConceptStudioRules.MaxImageBytes)
            return (null, controller.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Images must be 10 MB or smaller."));

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (ConceptStudioRules.DetectImage(bytes) is not { } kind)
            return (null, controller.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only JPEG, PNG, GIF and WebP images can be uploaded."));

        try
        {
            return (await store.SaveAsync(bytes, kind, ct), null);
        }
        catch (UnreadableImageException ex)
        {
            return (null, controller.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message));
        }
    }

    /// <summary>
    /// Serves a stored image, or with <paramref name="width"/> (one of ImageRules.ThumbnailWidths; anything else = the
    /// original) a thumbnail, with headers that stop browsers from running it as anything else and let them keep it
    /// (names are never reused, so a kept copy is never out of date).
    /// </summary>
    public static async Task<IActionResult> ServeAsync(ControllerBase controller, IImageStore store, string fileName, int? width, CancellationToken ct)
    {
        var image = width is { } w && ImageRules.ThumbnailWidths.Contains(w) ? await store.OpenThumbnailAsync(fileName, w, ct) : store.Open(fileName);
        if (image is not { } found) return controller.NotFound();
        controller.Response.Headers.XContentTypeOptions = "nosniff";
        controller.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        controller.Response.Headers.CacheControl = ImageRules.CacheControl;
        return controller.File(found.Content, found.ContentType);
    }
}
