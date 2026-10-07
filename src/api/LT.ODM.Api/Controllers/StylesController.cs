using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public Task<PagedResult<StyleListItemDto>> List(string? search, string? customer, string? season, string? businessUnit, string? productType,
        string? weaveType, string? gender, string? material, int skip = 0, int take = 50, CancellationToken ct = default)
        => styles.ListAsync(new StyleListQuery(Clean(search, 100), Clean(customer, 32), Clean(season, 200), Clean(businessUnit, 16),
            Clean(productType, 400), Clean(weaveType, 8), Clean(gender, 16), Math.Max(0, skip), Math.Clamp(take, 1, 200), Clean(material, 100)), ct);

    /// <summary>Landing page summary of the library (totals, seasons, top customers / materials / suppliers, recent changes).</summary>
    [HttpGet("dashboard")]
    public Task<StyleDashboardDto> Dashboard(CancellationToken ct) => styles.GetDashboardAsync(ct);

    [HttpGet("{styleId:int}")]
    public async Task<IActionResult> Get(int styleId, CancellationToken ct)
        => await styles.GetAsync(styleId, ct) is { } style ? Ok(style) : NotFound();

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

    [HttpGet("images/{fileName}")]
    public IActionResult GetImage(string fileName) => ImageUploads.Serve(this, images, fileName);

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
