using LT.ODM.Application.Admin;
using LT.ODM.Application.Translations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Settings > Translations: corrections to the UI texts without a release. The deployed files stay the base; the web
/// app loads a language's corrections (anonymous: the sign-in pages are translated too) and layers them on top.
/// Everything else is Admin role only; uploads are checked first (apply=false) and saved with apply=true.
/// </summary>
[ApiController]
[Route("api/v1/translations")]
[Authorize(Roles = AdminValidation.AdminRole)]
public sealed class TranslationsController(TranslationService translations) : ControllerBase
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    /// <summary>The corrections of one language, key -> text ({} when there are none or the language is unknown).</summary>
    [HttpGet("{lang}/overrides")]
    [AllowAnonymous]
    public async Task<IReadOnlyDictionary<string, string>> Overrides(string lang, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache";
        return await translations.OverridesAsync(lang, ct);
    }

    /// <summary>Every key with its deployed text and correction per language.</summary>
    [HttpGet]
    public Task<TranslationTableDto> Get(CancellationToken ct) => translations.TableAsync(ct);

    /// <summary>Saves one correction; value null (or the deployed text) goes back to the deployed text. Returns the key's row.</summary>
    [HttpPut("entries")]
    public Task<IActionResult> Set(SetTranslationRequest request, CancellationToken ct)
        => Run(async () => Ok(await translations.SetAsync(request, CurrentUserName, ct)));

    /// <summary>The workbook to edit: Key, one column per language (current texts), corrected cells in yellow.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
        => File(await translations.ExportAsync(ct), XlsxType, $"LT_ODM_translations_{DateTime.UtcNow:yyyy-MM-dd}.xlsx");

    /// <summary>Checks an edited workbook (.xlsx, at most 10 MB) and lists the changes; apply=true also saves them.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(TranslationRules.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = TranslationRules.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, [FromQuery] bool apply, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Choose the translations workbook (.xlsx) to upload.");
        if (file.Length > TranslationRules.MaxFileBytes)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The workbook must be 10 MB or smaller.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only Excel workbooks (.xlsx) can be uploaded. Save the file as .xlsx and try again.");

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        if (buffer.Length < 4 || !buffer.GetBuffer().AsSpan(0, 4).SequenceEqual(ZipSignature))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "This file is not an Excel workbook (.xlsx).");
        buffer.Position = 0;

        var fileName = Path.GetFileName(file.FileName);
        try
        {
            return await Run(async () => Ok(await translations.ImportAsync(buffer, fileName, apply, CurrentUserName, ct)));
        }
        catch (Exception ex) when (ex is InvalidDataException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The workbook could not be read. Open it in Excel, save it as .xlsx and try again.");
        }
    }

    /// <summary>The current version and the earlier ones kept (newest first).</summary>
    [HttpGet("history")]
    public Task<IReadOnlyList<TranslationVersionDto>> History(CancellationToken ct) => translations.HistoryAsync(ct);

    /// <summary>Makes an earlier version current; the one replaced is kept, so this can be undone.</summary>
    [HttpPost("history/{id}/restore")]
    public Task<IActionResult> Restore(string id, CancellationToken ct)
        => Run(async () => Ok(await translations.RestoreAsync(id, CurrentUserName, ct)));

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TranslationException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}
