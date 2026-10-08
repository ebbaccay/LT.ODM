using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Settings > Import: loads the Style Library workbook (Style Header, Article, BOM Detail). Admin role only.
/// Upload stages and checks the file; nothing reaches the library until Commit, which writes New styles and the
/// Changed styles the admin ticked. Who did what comes from the sign-in token.
/// </summary>
[ApiController]
[Route("api/v1/style-library/imports")]
[Authorize(Roles = AdminValidation.AdminRole)]
public sealed class StyleImportsController(IStyleWorkbookReader reader, IStyleImportRepository imports) : ControllerBase
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    [HttpGet]
    public Task<IReadOnlyList<StyleImportBatchDto>> List(CancellationToken ct) => imports.ListAsync(ct);

    /// <summary>Uploads a workbook (.xlsx, at most 50 MB), stages and checks it. Returns the batch and its summary.</summary>
    [HttpPost]
    [RequestSizeLimit(StyleImportRules.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = StyleImportRules.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Choose an Excel workbook (.xlsx) to upload.");
        if (file.Length > StyleImportRules.MaxFileBytes)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The workbook must be 50 MB or smaller.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only Excel workbooks (.xlsx) can be imported. Save the file as .xlsx and try again.");

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        if (buffer.Length < 4 || !buffer.GetBuffer().AsSpan(0, 4).SequenceEqual(ZipSignature))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "This file is not an Excel workbook (.xlsx).");
        buffer.Position = 0;

        StyleWorkbookReadResult read;
        try
        {
            read = reader.Read(buffer);
        }
        catch (Exception ex) when (ex is InvalidDataException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The workbook could not be read. Open it in Excel, save it as .xlsx and try again.");
        }

        if (read.Workbook is null)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The workbook does not match the Style Library template.",
                Detail = string.Join(" ", read.Problems),
            };
            problem.Extensions["problems"] = read.Problems;
            return BadRequest(problem);
        }

        var fileName = Path.GetFileName(file.FileName);
        if (fileName.Length > 260) fileName = fileName[^260..];
        return Ok(await imports.StageAsync(fileName, read.Workbook, CurrentUserName, ct));
    }

    [HttpGet("{batchId:int}")]
    public async Task<IActionResult> Get(int batchId, CancellationToken ct)
        => await imports.GetAsync(batchId, ct) is { } batch ? Ok(batch) : NotFound();

    /// <summary>Styles in the batch; action = New | Changed | Unchanged | Blocked (all when omitted).</summary>
    [HttpGet("{batchId:int}/styles")]
    public Task<PagedResult<StyleImportStyleDto>> GetStyles(int batchId, string? action, string? search, int skip = 0, int take = 100, CancellationToken ct = default)
        => imports.GetStylesAsync(batchId, OneOf(action, "New", "Changed", "Unchanged", "Blocked"), Blank(search), Math.Max(0, skip), Page(take), ct);

    /// <summary>Errors first, then warnings; severity = Error | Warning; styleKey = Cust|Season|Style.</summary>
    [HttpGet("{batchId:int}/issues")]
    public Task<PagedResult<StyleImportIssueDto>> GetIssues(int batchId, string? severity, string? styleKey, int skip = 0, int take = 100, CancellationToken ct = default)
        => imports.GetIssuesAsync(batchId, OneOf(severity, "Error", "Warning"), Blank(styleKey), Math.Max(0, skip), Page(take), ct);

    /// <summary>Writes New styles and the Changed styles listed in overwriteStyleKeys.</summary>
    [HttpPost("{batchId:int}/commit")]
    public Task<IActionResult> Commit(int batchId, CommitStyleImportRequest request, CancellationToken ct)
        => Run(async () => Ok(await imports.CommitAsync(batchId, request.OverwriteStyleKeys ?? [], CurrentUserName, ct)));

    [HttpPost("{batchId:int}/cancel")]
    public Task<IActionResult> Cancel(int batchId, CancellationToken ct)
        => Run(async () =>
        {
            await imports.CancelAsync(batchId, CurrentUserName, ct);
            return NoContent();
        });

    // ----- New codes -----

    /// <summary>Codes the import would add to the reference lists, and values that block rows or are blanked, with rule suggestions.</summary>
    [HttpGet("{batchId:int}/codes")]
    public Task<ImportNewCodesDto> GetNewCodes(int batchId, CancellationToken ct) => imports.GetNewCodesAsync(batchId, ct);

    /// <summary>Uses an existing code for a value (rewrites the staged rows, checks again) and returns the batch.</summary>
    [HttpPost("{batchId:int}/codes/map")]
    public Task<IActionResult> MapCode(int batchId, MapImportCodeRequest request, CancellationToken ct)
    {
        if (!ImportCodeLists.All.Contains(request.List) || string.IsNullOrWhiteSpace(request.Value) || string.IsNullOrWhiteSpace(request.Code)
            || request.Value.Length > 150 || request.Code.Length > 64)
            return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["code"] = ["Choose the list, the value and an existing code."],
            })));
        return Run(async () => Ok(await imports.MapCodeAsync(batchId, request with { Code = request.Code.Trim() }, ct)));
    }

    /// <summary>AI suggestions (Text job) for the values the spelling rules could not match. Nothing is changed until mapped.</summary>
    [HttpPost("{batchId:int}/codes/suggest")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> SuggestCodes(int batchId, [FromServices] IAiJsonClient ai, [FromServices] ImportCodeAdvisor advisor, CancellationToken ct)
    {
        if (await ai.GetInfoAsync(ct) is { IsConfigured: false } info)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiUnavailable.Message("AI suggestions", info));
        try
        {
            return Ok(await advisor.SuggestAsync(await imports.GetNewCodesAsync(batchId, ct), ct));
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }

    private static int Page(int take) => Math.Clamp(take, 1, StyleImportRules.MaxPageSize);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? OneOf(string? value, params string[] allowed)
        => allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (StyleImportException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }
}
