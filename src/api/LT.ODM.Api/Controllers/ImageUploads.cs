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

    /// <summary>Serves a stored image with headers that stop browsers from running it as anything else.</summary>
    public static IActionResult Serve(ControllerBase controller, IImageStore store, string fileName)
    {
        if (store.Open(fileName) is not { } image) return controller.NotFound();
        controller.Response.Headers.XContentTypeOptions = "nosniff";
        controller.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        return controller.File(image.Content, image.ContentType);
    }
}
