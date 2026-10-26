using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Styles: list, detail, and manual create / edit / delete of styles, colorways, BOM lines and style history.
/// Reading: Admin, Merchandiser, Costing, Viewer. Changing: Admin, Merchandiser. Who changed what comes from the
/// sign-in token. Edits carry the row's RowVer; a change saved by someone else in between comes back as 409.
/// </summary>
[ApiController]
[Route("api/v1/styles")]
[Authorize(Roles = StyleRoles.Readers)]
public sealed class StylesController(IStyleRepository styles, IStyleImageStore images) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    // ----- Reads -----

    [HttpGet("lookups")]
    public Task<StyleLookupsDto> Lookups(CancellationToken ct) => styles.GetLookupsAsync(ct);

    [HttpGet]
    /// <param name="status">active, inactive, or empty for both.</param>
    /// <param name="pictures">with (a picture or sketch), without, or empty for both.</param>
    /// <param name="sort">style, customer, season, businessUnit, productType, colorways, bomLines or changed (else newest season first); desc=true reverses it.</param>
    /// <param name="parentsOnly">true: a version is folded under the style it came from when that style is in the results too (Versions counts them).</param>
    public Task<PagedResult<StyleListItemDto>> List(string? search, string? customer, string? season, string? businessUnit, string? productType,
        string? weaveType, string? gender, string? material, string? status, int skip = 0, int take = 50, string? looks = null,
        string? pictures = null, bool? parentsOnly = null, string? sort = null, bool? desc = null, CancellationToken ct = default)
        => styles.ListAsync(new StyleListQuery(Clean(search, 100), Clean(customer, 32), Clean(season, 200), Clean(businessUnit, 200),
            Clean(productType, 400), Clean(weaveType, 8), Clean(gender, 16), Math.Max(0, skip), Math.Clamp(take, 1, 200), Clean(material, 100),
            status?.Trim().ToLowerInvariant() switch { "active" => true, "inactive" => false, _ => null }, Clean(looks, 400),
            pictures?.Trim().ToLowerInvariant() switch { "with" => true, "without" => false, _ => null }, parentsOnly == true ? true : null,
            StyleListSorts.Clean(sort), desc == true ? true : null), ct);

    /// <summary>Landing page summary of the library (totals, seasons, top customers / materials / suppliers, recent changes).</summary>
    [HttpGet("dashboard")]
    public Task<StyleDashboardDto> Dashboard(CancellationToken ct) => styles.GetDashboardAsync(ct);

    [HttpGet("{styleId:int}")]
    public async Task<IActionResult> Get(int styleId, CancellationToken ct)
        => await styles.GetAsync(styleId, ct) is { } style ? Ok(style) : NotFound();

    /// <summary>The style's tech pack as a PDF download: header, sketch and photo, colorways, BOM and material colours by colorway.</summary>
    [HttpGet("{styleId:int}/tech-pack")]
    public async Task<IActionResult> TechPack(int styleId, [FromServices] ITechPackWriter writer, CancellationToken ct)
    {
        if (await styles.GetAsync(styleId, ct) is not { } style) return NotFound();
        var pdf = await writer.WriteAsync(style, CurrentUserName, DateTime.UtcNow, ct);
        return File(pdf, "application/pdf", $"{FileNameSafe(style.Style.StyleNo)}_{FileNameSafe(style.Style.SeasonCode)}_techpack.pdf");
    }

    private static string FileNameSafe(string text) => string.Concat(text.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    /// <summary>
    /// Styles like this one (shared materials, garment, weave, gender), for "have we made this before, and what did we use?"
    /// Same brand first; other brands' styles are know-how only. Rules only, no AI; the style's own family is left out.
    /// </summary>
    [HttpGet("{styleId:int}/similar")]
    public async Task<IActionResult> Similar(int styleId, [FromServices] IStyleGapRepository gaps, int top = 8, CancellationToken ct = default)
    {
        try
        {
            return Ok(await gaps.SimilarAsync(styleId, Math.Clamp(top, 1, 20), ct));
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    /// <summary>
    /// A prompt to paste into an outside design tool (StyTrix, Style3D AI, ...), built from the style, BOM and colorway data.
    /// No AI call; nothing is sent anywhere. mode = text (text-to-design) or image (image-to-design with the sketch or photo).
    /// </summary>
    [HttpGet("{styleId:int}/design-prompt")]
    public async Task<IActionResult> DesignPrompt(int styleId, string? mode, int? colorwayId, CancellationToken ct)
        => await styles.GetAsync(styleId, ct) is { } style
            ? Ok(Application.StyleAi.DesignPrompt.Build(style, mode, colorwayId))
            : NotFound();

    // ----- Styles -----

    [HttpPost]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Create(SaveStyleRequest request, CancellationToken ct)
        => Run(StyleValidation.Style(request), async () => Ok(new { styleId = await styles.SaveStyleAsync(null, request, CurrentUserName, ct) }));

    [HttpPut("{styleId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Update(int styleId, SaveStyleRequest request, CancellationToken ct)
        => Run(WithRowVer(StyleValidation.Style(request), request.RowVer), async () =>
        {
            await styles.SaveStyleAsync(styleId, request, CurrentUserName, ct);
            return Ok(new { styleId });
        });

    [HttpDelete("{styleId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Delete(int styleId, string? rowVer, CancellationToken ct)
        => RunWithRowVer(rowVer, async v =>
        {
            await styles.DeleteStyleAsync(styleId, v, CurrentUserName, ct);
            return NoContent();
        });

    /// <summary>
    /// Which part of the style's sketch (source=Sketch, default) or picture (Image) shows each colorway, matched by the
    /// colour in the colorway's name. Nothing changes.
    /// </summary>
    [HttpGet("{styleId:int}/colorway-pictures")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> ColorwayPictures(int styleId, string? source, [FromServices] ColorwayPictureService pictures, CancellationToken ct)
    {
        try
        {
            return await pictures.SuggestAsync(styleId, source, ct) is { } found ? Ok(found) : NotFound();
        }
        catch (Exception ex) when (ex is StyleImageImportException or Application.ConceptStudio.UnreadableImageException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>Cuts the confirmed parts of the sketch and sets them as the colorways' pictures (one result per colorway).</summary>
    [HttpPost("{styleId:int}/colorway-pictures")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> SaveColorwayPictures(int styleId, SaveColorwayPicturesRequest request, [FromServices] ColorwayPictureService pictures,
        CancellationToken ct)
    {
        if ((request.Items?.Count ?? 0) > 200) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Save at most 200 colorways at a time.");
        try
        {
            return await pictures.SaveAsync(styleId, request, CurrentUserName, ct) is { } saved ? Ok(saved) : NotFound();
        }
        catch (Exception ex) when (ex is StyleImageImportException or Application.ConceptStudio.UnreadableImageException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>Swaps the style's picture and sketch, for a picture saved in the wrong place.</summary>
    [HttpPost("{styleId:int}/swap-pictures")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> SwapPictures(int styleId, string? rowVer, CancellationToken ct)
        => RunWithRowVer(rowVer, async v =>
        {
            await styles.SwapPicturesAsync(styleId, v, CurrentUserName, ct);
            return NoContent();
        });

    /// <summary>Reuses the style in another season (or as a variant): copies colorways and BOM and links it as history.</summary>
    [HttpPost("{styleId:int}/copy")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> Copy(int styleId, CopyStyleRequest request, CancellationToken ct)
        => Run(StyleValidation.Copy(request), async () => Ok(new { styleId = await styles.CopyStyleAsync(styleId, request, CurrentUserName, ct) }));

    // ----- Colorways -----

    [HttpPost("{styleId:int}/colorways")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> CreateColorway(int styleId, SaveColorwayRequest request, CancellationToken ct)
        => Run(StyleValidation.Colorway(request), async () =>
            Ok(new { colorwayId = await styles.SaveColorwayAsync(styleId, null, request, CurrentUserName, ct) }));

    [HttpPut("{styleId:int}/colorways/{colorwayId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> UpdateColorway(int styleId, int colorwayId, SaveColorwayRequest request, CancellationToken ct)
        => Run(WithRowVer(StyleValidation.Colorway(request), request.RowVer), async () =>
            Ok(new { colorwayId = await styles.SaveColorwayAsync(styleId, colorwayId, request, CurrentUserName, ct) }));

    [HttpDelete("{styleId:int}/colorways/{colorwayId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> DeleteColorway(int styleId, int colorwayId, string? rowVer, CancellationToken ct)
        => RunWithRowVer(rowVer, async v =>
        {
            await styles.DeleteColorwayAsync(colorwayId, v, CurrentUserName, ct);
            return NoContent();
        });

    // ----- BOM lines -----

    /// <summary>
    /// Material colours for the style's empty BOM line x colorway cells, from what other styles used for the same material
    /// in colorways of the same name (or the one colour a material always comes in). Rules, nothing saved.
    /// </summary>
    [HttpGet("{styleId:int}/colour-suggestion")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> SuggestColours(int styleId, [FromServices] ColourSuggestionService colours, CancellationToken ct)
        => await colours.SuggestAsync(styleId, ct) is { } suggestion ? Ok(suggestion) : NotFound();

    /// <summary>Fills the picked colours into cells that are still empty (never overwrites a colour).</summary>
    [HttpPost("{styleId:int}/colour-suggestion/apply")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> ApplyColours(int styleId, ApplyColoursRequest request, [FromServices] IColourSuggestionRepository colours, CancellationToken ct)
    {
        if (request.Cells is not { Count: > 0 and <= 5000 } cells)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["cells"] = ["Pick between 1 and 5,000 colours."] }));
        if (cells.Any(c => c.MaterialColorCode?.Length > 80 || c.MaterialColorDescription?.Length > 150))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["cells"] = ["A colour code has at most 80 characters, a description 150."] }));
        return Ok(new { filled = await colours.ApplyAsync(styleId, cells, CurrentUserName, ct) });
    }

    /// <summary>
    /// Suggest from picture: a draft of a new style's garment, product type, sleeve, look tags and description from its
    /// uploaded sketch or photo, plus knit / woven and fabric hints from the closest-looking library styles (built-in image
    /// model, in the API). 409 when the model cannot run or the picture cannot be read.
    /// </summary>
    [HttpPost("describe-picture")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> DescribePicture(DescribePictureRequest request, [FromServices] PictureDescriber describer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl) || request.ImageUrl.Length > 500)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["imageUrl"] = ["Upload a sketch or photo first."] }));
        try
        {
            return Ok(await describer.DescribeAsync(request with { CustomerCode = Clean(request.CustomerCode, 32) }, ct));
        }
        catch (LT.ODM.Application.Ai.AiServiceException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }

    /// <summary>
    /// A suggested BOM for the style, built from similar library styles and the fabric its description names (rules, about
    /// a second, no AI service). Nothing is saved: the picked lines are added with bom-suggestion/apply.
    /// </summary>
    [HttpGet("{styleId:int}/bom-suggestion")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> SuggestBom(int styleId, [FromServices] BomSuggestionService suggestions, CancellationToken ct)
        => await suggestions.SuggestAsync(styleId, ct) is { } suggestion ? Ok(suggestion) : NotFound();

    /// <summary>Adds the picked suggestion lines as ordinary BOM lines (checked like any line first; all or none is checked, then each is saved).</summary>
    [HttpPost("{styleId:int}/bom-suggestion/apply")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> ApplyBomSuggestion(int styleId, ApplyBomSuggestionRequest request, CancellationToken ct)
    {
        if (request.Lines is not { Count: > 0 and <= 200 } lines)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["lines"] = ["Pick between 1 and 200 lines."] }));
        foreach (var line in lines)
            if (StyleValidation.BomLine(line) is { Count: > 0 } errors)
                return ValidationProblem(new ValidationProblemDetails(errors.ToDictionary(e => $"{line.MaterialCode}: {e.Key}", e => e.Value)));
        return await Run(new Dictionary<string, string[]>(), async () =>
        {
            var added = 0;
            foreach (var line in lines)
            {
                await styles.SaveBomLineAsync(styleId, null, line, CurrentUserName, ct);
                added++;
            }
            return Ok(new { added });
        });
    }

    [HttpPost("{styleId:int}/bom-lines")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> CreateBomLine(int styleId, SaveBomLineRequest request, CancellationToken ct)
        => Run(StyleValidation.BomLine(request), async () =>
            Ok(new { bomLineId = await styles.SaveBomLineAsync(styleId, null, request, CurrentUserName, ct) }));

    [HttpPut("{styleId:int}/bom-lines/{bomLineId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> UpdateBomLine(int styleId, int bomLineId, SaveBomLineRequest request, CancellationToken ct)
        => Run(WithRowVer(StyleValidation.BomLine(request), request.RowVer), async () =>
            Ok(new { bomLineId = await styles.SaveBomLineAsync(styleId, bomLineId, request, CurrentUserName, ct) }));

    [HttpDelete("{styleId:int}/bom-lines/{bomLineId:int}")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> DeleteBomLine(int styleId, int bomLineId, string? rowVer, CancellationToken ct)
        => RunWithRowVer(rowVer, async v =>
        {
            await styles.DeleteBomLineAsync(bomLineId, v, CurrentUserName, ct);
            return NoContent();
        });

    // ----- Style history -----

    /// <summary>Records which style this one was reused from (set by hand; never replaced by an import).</summary>
    [HttpPut("{styleId:int}/history")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> SetHistory(int styleId, SetStyleHistoryRequest request, CancellationToken ct)
        => Run(StyleValidation.History(request), async () =>
        {
            await styles.SetHistoryAsync(styleId, request, CurrentUserName, ct);
            return NoContent();
        });

    [HttpDelete("{styleId:int}/history")]
    [Authorize(Roles = StyleRoles.Editors)]
    public Task<IActionResult> RemoveHistory(int styleId, CancellationToken ct)
        => Run([], async () =>
        {
            await styles.RemoveHistoryAsync(styleId, CurrentUserName, ct);
            return NoContent();
        });

    // ----- Images -----

    /// <summary>Uploads a sketch or photo (JPEG, PNG, GIF or WebP, at most 10 MB). Save its imageUrl with the style, colorway or BOM line.</summary>
    [HttpPost("images")]
    [Authorize(Roles = StyleRoles.Editors)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken ct)
    {
        var (name, problem) = await ImageUploads.SaveAsync(this, images, file, ct);
        return problem ?? Ok(new { imageUrl = $"/api/v1/styles/images/{name}" });
    }

    /// <summary>A stored picture; ?w=160|320|640 for a thumbnail (lists, cards). Browsers may keep it (names are never reused).</summary>
    [HttpGet("images/{fileName}")]
    [DisableRateLimiting]   // cheap files for signed-in users; one page can show dozens
    public Task<IActionResult> GetImage(string fileName, int? w, CancellationToken ct) => ImageUploads.ServeAsync(this, images, fileName, w, ct);

    // ----- Helpers -----

    private static string? Clean(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();

    private static Dictionary<string, string[]> WithRowVer(Dictionary<string, string[]> errors, byte[]? rowVer)
    {
        if (rowVer is not { Length: 8 }) errors["rowVer"] = ["Reload the style and try again."];
        return errors;
    }

    private Task<IActionResult> RunWithRowVer(string? rowVer, Func<byte[], Task<IActionResult>> action)
    {
        byte[]? value = null;
        try { if (!string.IsNullOrEmpty(rowVer)) value = Convert.FromBase64String(rowVer); } catch (FormatException) { }
        return value is { Length: 8 } v
            ? Run([], () => action(v))
            : Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["rowVer"] = ["Reload the style and try again."] })));
    }

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
