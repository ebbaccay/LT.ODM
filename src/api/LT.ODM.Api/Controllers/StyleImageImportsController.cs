using LT.ODM.Application.Admin;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Settings > Import pictures (Admin): style pictures from files named after the style (RC2606OW001_Img01.jpg). The page
/// sends one file at a time: analyse (styles found from the name, garment box suggested), optionally preview the result,
/// then save (cut, enlarged when small, stored once, set on the styles ticked). Nothing changes before save.
/// </summary>
[ApiController]
[Route("api/v1/style-library/image-imports")]
[Authorize(Roles = AdminValidation.AdminRole)]
public sealed class StyleImageImportsController(StyleImageImportService imports) : ControllerBase
{
    private const long RequestLimit = StyleImageImportRules.MaxFileBytes + 1024 * 1024;

    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    /// <summary>Whether the image model (choosing the garment) and the upscaler are installed, and AI is on.</summary>
    [HttpGet("status")]
    public Task<StyleImageImportStatusDto> Status(CancellationToken ct) => imports.StatusAsync(ct);

    /// <summary>File key, matching styles and the suggested garment box for one picture.</summary>
    [HttpPost("analyse")]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> Analyse(IFormFile? file, CancellationToken ct)
    {
        var (bytes, problem) = await ReadAsync(file, ct);
        if (problem is not null) return problem;
        try
        {
            return Ok(await imports.AnalyseAsync(file!.FileName, bytes!, ct));
        }
        catch (UnreadableImageException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>The picture as it would be saved (PNG): cut to the box, enlarged when small and enhance is on.</summary>
    [HttpPost("preview")]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> Preview([FromForm] StyleImageForm form, CancellationToken ct)
    {
        var (bytes, problem) = await ReadAsync(form.File, ct);
        if (problem is not null) return problem;
        try
        {
            var (png, enhanced) = await imports.PreviewAsync(bytes!, form.Box(), form.Enhance, ct);
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.ContentSecurityPolicy = "default-src 'none'";
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Enhanced"] = enhanced ? "true" : "false";
            return File(png, "image/png");
        }
        catch (UnreadableImageException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>Stores the result once and sets it as the picture (field Image) or sketch (Sketch) of the styles given.</summary>
    [HttpPost("save")]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> Save([FromForm] StyleImageForm form, CancellationToken ct)
    {
        var (bytes, problem) = await ReadAsync(form.File, ct);
        if (problem is not null) return problem;
        var ids = (form.StyleIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
        if (ids.Count > 500) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Tick at most 500 styles per picture.");
        try
        {
            return Ok(await imports.SaveAsync(bytes!, form.Box(), form.Enhance, form.Field ?? "Image", form.Overwrite, ids, CurrentUserName, ct));
        }
        catch (StyleImageImportException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (UnreadableImageException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// Checks every stored style picture and sketch (sketch or photo) and suggests moving those in the wrong place.
    /// Nothing changes. Takes about a quarter of a second per picture file on the server's CPU.
    /// </summary>
    [HttpPost("existing/check")]
    public Task<ExistingPicturesCheckDto> CheckExisting(CancellationToken ct) => imports.CheckExistingAsync(ct);

    /// <summary>Swaps picture and sketch of the styles given, where they still hold what the check saw.</summary>
    [HttpPost("existing/move")]
    public async Task<IActionResult> MoveExisting(MoveExistingPicturesRequest request, CancellationToken ct)
    {
        var moves = (request.Items ?? []).Where(i => i.StyleId > 0).DistinctBy(i => i.StyleId).ToList();
        if (moves.Count > 5000) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Move at most 5000 styles at a time.");
        try
        {
            return Ok(await imports.MoveExistingAsync(moves, CurrentUserName, ct));
        }
        catch (StyleImageImportException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>The upload's bytes when it is a JPEG, PNG, GIF or WebP of at most 10 MB; else a 400 problem.</summary>
    private async Task<(byte[]? Bytes, IActionResult? Problem)> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "Choose a picture to import."));
        if (file.Length > StyleImageImportRules.MaxFileBytes)
            return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "Pictures must be 10 MB or smaller."));
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        return ConceptStudioRules.DetectImage(bytes) is null
            ? (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only JPEG, PNG, GIF and WebP pictures can be imported."))
            : (bytes, null);
    }
}

public sealed record MoveExistingPicturesRequest(IReadOnlyList<ExistingPictureMove>? Items);

/// <summary>
/// One picture with the admin's choices. The box (x, y, width, height in pixels of the upright picture) is optional:
/// none = the whole picture. styleIds is a comma list (save only).
/// </summary>
public sealed class StyleImageForm
{
    public IFormFile? File { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool Enhance { get; set; } = true;
    public string? Field { get; set; }
    public bool Overwrite { get; set; }
    public string? StyleIds { get; set; }

    public CropBoxDto? Box() => X is { } x && Y is { } y && Width is > 0 and { } w && Height is > 0 and { } h ? new CropBoxDto(x, y, w, h) : null;
}
