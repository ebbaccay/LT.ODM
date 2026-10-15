using System.Text.Json;
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
/// Inspect shows the workbook's sheets and headings with a suggested column mapping (saved or automatic);
/// Upload stages and checks the file with the mapping chosen; nothing reaches the library until Commit, which writes New styles and the
/// Changed styles the admin ticked. Who did what comes from the sign-in token.
/// </summary>
[ApiController]
[Route("api/v1/style-library/imports")]
[Authorize(Roles = AdminValidation.AdminRole)]
public sealed class StyleImportsController(IStyleWorkbookReader reader, IStyleImportRepository imports, IImportFileStore files) : ControllerBase
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    [HttpGet]
    public Task<IReadOnlyList<StyleImportBatchDto>> List(CancellationToken ct) => imports.ListAsync(ct);

    /// <summary>
    /// Reads an uploaded workbook's sheets and headings (with sample values) and suggests a column mapping: the saved
    /// mapping that fits best, otherwise the automatic one. The workbook is kept with a new import in status Mapping
    /// (BatchId), so the mapping can be finished later; nothing is staged.
    /// </summary>
    [HttpPost("inspect")]
    [RequestSizeLimit(StyleImportRules.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = StyleImportRules.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Inspect(IFormFile? file, CancellationToken ct)
    {
        var (buffer, error) = await ReceiveAsync(file, ct);
        if (buffer is null) return error!;
        await using (buffer)
        {
            if (TryInspect(buffer, out var sheets) is { } unreadable) return unreadable;
            var saved = await imports.ListMappingsAsync(ct);
            var pick = StyleImportMapper.Pick(saved, sheets);
            var mapping = pick?.Mapping ?? StyleImportMapper.Auto(sheets);

            await PurgeFilesAsync(ct);
            buffer.Position = 0;
            var stored = await files.SaveAsync(buffer, ct);
            var fileName = ShortName(file!.FileName);
            var draft = await imports.CreateDraftAsync(fileName, stored, mapping, CurrentUserName, ct);
            return Ok(new WorkbookInspectionDto(sheets, StyleImportMapper.Targets, mapping, pick?.MappingId, saved, draft.BatchId, fileName));
        }
    }

    /// <summary>
    /// The kept workbook of an import that was not committed (mapping not finished, waiting to commit, cancelled), with
    /// the mapping chosen for it: to finish or change the mapping. 404; 409 when the workbook is no longer kept.
    /// </summary>
    [HttpGet("{batchId:int}/inspect")]
    public async Task<IActionResult> Reinspect(int batchId, CancellationToken ct)
    {
        var (kept, buffer, error) = await OpenKeptAsync(batchId, ct);
        if (buffer is null) return error!;
        await using (buffer)
        {
            if (TryInspect(buffer, out var sheets) is { } unreadable) return unreadable;
            var saved = await imports.ListMappingsAsync(ct);
            var pick = kept!.Mapping is null ? StyleImportMapper.Pick(saved, sheets) : null;
            var mapping = kept.Mapping ?? pick?.Mapping ?? StyleImportMapper.Auto(sheets);
            return Ok(new WorkbookInspectionDto(sheets, StyleImportMapper.Targets, mapping, pick?.MappingId, saved, batchId, kept.FileName));
        }
    }

    /// <summary>Saves the mapping being edited, so it can be resumed. 409 once committed.</summary>
    [HttpPut("{batchId:int}/mapping")]
    public Task<IActionResult> SaveBatchMapping(int batchId, ImportColumnMapping? mapping, CancellationToken ct)
    {
        if (mapping?.Sheets is null)
            return Task.FromResult<IActionResult>(Problem(statusCode: StatusCodes.Status400BadRequest, title: "The column mapping could not be read."));
        return Run(async () =>
        {
            await imports.SaveBatchMappingAsync(batchId, StyleImportMapper.Clean(mapping), ct);
            return NoContent();
        });
    }

    /// <summary>
    /// Stages the kept workbook with this mapping and checks it (an import waiting to commit is checked again from
    /// scratch; a cancelled one is waiting again). Returns the batch; 400 with problems when the mapping does not fit.
    /// </summary>
    [HttpPost("{batchId:int}/stage")]
    public async Task<IActionResult> Stage(int batchId, ImportColumnMapping? mapping, CancellationToken ct)
    {
        if (mapping?.Sheets is null)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The column mapping could not be read. Map the columns again.");
        var chosen = StyleImportMapper.Clean(mapping);
        var (_, buffer, error) = await OpenKeptAsync(batchId, ct);
        if (buffer is null) return error!;
        await using (buffer)
        {
            if (TryInspect(buffer, out var sheets) is { } unreadable) return unreadable;
            var problems = StyleImportMapper.Problems(chosen, sheets);
            if (problems.Count > 0) return TemplateProblems(problems);
            StyleWorkbookReadResult read;
            try
            {
                buffer.Position = 0;
                read = reader.Read(buffer, chosen);
            }
            catch (Exception ex) when (ex is InvalidDataException or ExcelDataReader.Exceptions.ExcelReaderException)
            {
                return Unreadable();
            }
            if (read.Workbook is null) return TemplateProblems(read.Problems);
            return await Run(async () => Ok(await imports.RestageAsync(batchId, chosen, read.Workbook, CurrentUserName, ct)));
        }
    }

    /// <summary>Deletes an import that was not committed, with its kept workbook. 404; 409 for a committed import.</summary>
    [HttpDelete("{batchId:int}")]
    public Task<IActionResult> Delete(int batchId, CancellationToken ct)
        => Run(async () =>
        {
            if (await imports.DeleteAsync(batchId, ct) is { } stored) files.Delete(stored);
            return NoContent();
        });

    /// <summary>
    /// Uploads a workbook (.xlsx, at most 100 MB), stages and checks it. Returns the batch and its summary.
    /// mapping (form field, JSON ImportColumnMapping): which sheet and heading feeds each column; omitted = automatic.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(StyleImportRules.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = StyleImportRules.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromForm] string? mapping, CancellationToken ct)
    {
        ImportColumnMapping? chosen = null;
        if (!string.IsNullOrWhiteSpace(mapping))
        {
            try
            {
                chosen = mapping.Length <= MaxMappingChars ? JsonSerializer.Deserialize<ImportColumnMapping>(mapping, Json) : null;
            }
            catch (JsonException)
            {
                chosen = null;
            }
            if (chosen?.Sheets is null)
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The column mapping could not be read. Map the columns again.");
            chosen = StyleImportMapper.Clean(chosen);
        }

        var (buffer, error) = await ReceiveAsync(file, ct);
        if (buffer is null) return error!;
        await using (buffer)
        {
            if (TryInspect(buffer, out var sheets) is { } unreadable) return unreadable;
            chosen ??= StyleImportMapper.Auto(sheets);
            var problems = StyleImportMapper.Problems(chosen, sheets);
            if (problems.Count > 0) return TemplateProblems(problems);

            StyleWorkbookReadResult read;
            try
            {
                buffer.Position = 0;
                read = reader.Read(buffer, chosen);
            }
            catch (Exception ex) when (ex is InvalidDataException or ExcelDataReader.Exceptions.ExcelReaderException)
            {
                return Unreadable();
            }
            if (read.Workbook is null) return TemplateProblems(read.Problems);

            // Kept like an inspected workbook, so the columns can be mapped again before commit.
            await PurgeFilesAsync(ct);
            buffer.Position = 0;
            var stored = await files.SaveAsync(buffer, ct);
            var draft = await imports.CreateDraftAsync(ShortName(file!.FileName), stored, chosen, CurrentUserName, ct);
            return await Run(async () => Ok(await imports.RestageAsync(draft.BatchId, chosen, read.Workbook, CurrentUserName, ct)));
        }
    }

    // ----- Saved column mappings -----

    [HttpGet("mappings")]
    public Task<IReadOnlyList<ImportMappingDto>> ListMappings(CancellationToken ct) => imports.ListMappingsAsync(ct);

    /// <summary>Saves the mapping under its name (a mapping with that name is replaced).</summary>
    [HttpPut("mappings")]
    public Task<IActionResult> SaveMapping(SaveImportMappingRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > StyleImportMapper.MaxNameLength || request.Mapping?.Sheets is null)
            return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["name"] = [$"Enter a name of up to {StyleImportMapper.MaxNameLength} characters for the mapping."],
            })));
        return Run(async () => Ok(await imports.SaveMappingAsync(name, StyleImportMapper.Clean(request.Mapping), CurrentUserName, ct)));
    }

    [HttpDelete("mappings/{mappingId:int}")]
    public async Task<IActionResult> DeleteMapping(int mappingId, CancellationToken ct)
        => await imports.DeleteMappingAsync(mappingId, ct) ? NoContent() : NotFound();

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

    /// <summary>
    /// Undoes a commit made in the last 30 days. When styles changed after the import nothing is changed: 409 with
    /// blockers (style, season, reason).
    /// </summary>
    [HttpPost("{batchId:int}/rollback")]
    public Task<IActionResult> Rollback(int batchId, CancellationToken ct)
        => Run(async () =>
        {
            var result = await imports.RollbackAsync(batchId, CurrentUserName, ct);
            if (result.Blockers.Count == 0) return Ok(result.Batch);
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "This import cannot be rolled back: some of its styles changed after it. Nothing was changed.",
            };
            problem.Extensions["blockers"] = result.Blockers;
            return Conflict(problem);
        });

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

    /// <summary>
    /// Uses an existing code for a value (rewrites the staged rows, checks again) and returns the batch.
    /// remember (default true): later imports use the same code for this value without asking.
    /// </summary>
    [HttpPost("{batchId:int}/codes/map")]
    public Task<IActionResult> MapCode(int batchId, MapImportCodeRequest request, CancellationToken ct)
        => MapCodes(batchId, new MapImportCodesRequest([new MapImportCodeItem(request.List, request.Value, request.Code)], request.Remember), ct);

    /// <summary>Several values at once (e.g. every suggested SAP supplier); checked once. Returns the batch.</summary>
    [HttpPost("{batchId:int}/codes/map-many")]
    public Task<IActionResult> MapCodes(int batchId, MapImportCodesRequest request, CancellationToken ct)
    {
        var items = request.Items ?? [];
        if (items.Count is 0 or > MaxMapItems || items.Any(i => !ValidCodeItem(i)))
            return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["code"] = ["Choose the list, the value and an existing code."],
            })));
        var clean = items.Select(i => i with { Code = i.Code!.Trim() }).ToList();
        return Run(async () => Ok(await imports.MapCodesAsync(batchId, clean, request.Remember, CurrentUserName, ct)));
    }

    /// <summary>
    /// New customer accounts that are one customer: adds customer code (when it is new, named name) and uses it for
    /// every account in values. Returns the batch.
    /// </summary>
    [HttpPost("{batchId:int}/codes/group-customers")]
    public Task<IActionResult> GroupCustomers(int batchId, GroupImportCustomersRequest request, CancellationToken ct)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? "";
        var values = request.Values ?? [];
        var errors = new Dictionary<string, string[]>();
        if (code.Length is 0 or > 32 || code.Contains('|') || code.Any(char.IsControl))
            errors["code"] = ["Enter a customer code of up to 32 characters."];
        if (request.Name?.Trim().Length > 150) errors["name"] = ["Use at most 150 characters for the name."];
        if (values.Count is 0 or > MaxMapItems || values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 150))
            errors["values"] = ["Choose the accounts to group."];
        if (errors.Count > 0) return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(errors)));
        return Run(async () => Ok(await imports.GroupCustomersAsync(batchId, request with { Code = code, Name = request.Name?.Trim() }, CurrentUserName, ct)));
    }

    /// <summary>
    /// Customer accounts that stay separate customers under one brand (11 POLO accounts -> POLO): adds the brand if
    /// new and gives it to every account in values (and to alsoCustomer when it has none). Returns the batch.
    /// </summary>
    [HttpPost("{batchId:int}/codes/brand-customers")]
    public Task<IActionResult> BrandCustomers(int batchId, BrandImportCustomersRequest request, CancellationToken ct)
    {
        var brand = request.Brand?.Trim().ToUpperInvariant() ?? "";
        var values = request.Values ?? [];
        var errors = new Dictionary<string, string[]>();
        if (brand.Length is 0 or > 32 || brand.Contains('|') || brand.Any(char.IsControl))
            errors["brand"] = ["Enter a brand code of up to 32 characters."];
        if (request.Name?.Trim().Length > 100) errors["name"] = ["Use at most 100 characters for the brand name."];
        if (values.Count is 0 or > MaxMapItems || values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 150))
            errors["values"] = ["Choose the accounts to give the brand."];
        if (request.AlsoCustomer?.Length > 32) errors["alsoCustomer"] = ["Choose an existing customer."];
        if (errors.Count > 0) return Task.FromResult<IActionResult>(ValidationProblem(new ValidationProblemDetails(errors)));
        return Run(async () => Ok(await imports.BrandCustomersAsync(batchId,
            request with { Brand = brand, Name = request.Name?.Trim(), AlsoCustomer = string.IsNullOrWhiteSpace(request.AlsoCustomer) ? null : request.AlsoCustomer.Trim() },
            CurrentUserName, ct)));
    }

    /// <summary>Forgets a remembered answer: later imports ask about the value again.</summary>
    [HttpPost("codes/forget")]
    public async Task<IActionResult> ForgetCode(ForgetImportCodeRequest request, CancellationToken ct)
    {
        if (request.List is null || !ImportCodeLists.All.Contains(request.List) || string.IsNullOrWhiteSpace(request.Value) || request.Value.Length > 150)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["value"] = ["Choose the list and the value."] }));
        return await imports.ForgetCodeAsync(request.List, request.Value, ct) ? NoContent() : NotFound();
    }

    private const int MaxMapItems = 2000;

    private static bool ValidCodeItem(MapImportCodeItem i)
        => i.List is not null && ImportCodeLists.All.Contains(i.List) && !string.IsNullOrWhiteSpace(i.Value) && !string.IsNullOrWhiteSpace(i.Code)
           && i.Value.Length <= 150 && i.Code.Trim().Length <= 64;

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

    private const int MaxMappingChars = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The uploaded file in memory, or (Buffer null) the reason it is refused.</summary>
    private async Task<(MemoryStream? Buffer, IActionResult? Error)> ReceiveAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "Choose an Excel workbook (.xlsx) to upload."));
        if (file.Length > StyleImportRules.MaxFileBytes)
            return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "The workbook must be 100 MB or smaller."));
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "Only Excel workbooks (.xlsx) can be imported. Save the file as .xlsx and try again."));

        var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        if (buffer.Length < 4 || !buffer.GetBuffer().AsSpan(0, 4).SequenceEqual(ZipSignature))
        {
            await buffer.DisposeAsync();
            return (null, Problem(statusCode: StatusCodes.Status400BadRequest, title: "This file is not an Excel workbook (.xlsx)."));
        }
        return (buffer, null);
    }

    private static string ShortName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return name.Length > 260 ? name[^260..] : name;
    }

    /// <summary>Deletes kept workbooks that are no longer needed (committed imports; old cancelled ones and drafts).</summary>
    private async Task PurgeFilesAsync(CancellationToken ct)
    {
        foreach (var name in await imports.PurgeFilesAsync(ct)) files.Delete(name);
    }

    /// <summary>An import's kept workbook in memory, or (Buffer null) why it cannot be opened.</summary>
    private async Task<(StyleImportFile? Kept, MemoryStream? Buffer, IActionResult? Error)> OpenKeptAsync(int batchId, CancellationToken ct)
    {
        var kept = await imports.GetFileAsync(batchId, ct);
        if (kept is null) return (null, null, NotFound());
        if (kept.Status is "Committed" or "RolledBack")
            return (kept, null, Problem(statusCode: StatusCodes.Status409Conflict, title: "The columns of a committed import cannot be mapped again. Upload the file as a new import."));
        await using var stream = kept.StoredFile is null ? null : files.Open(kept.StoredFile);
        if (stream is null)
            return (kept, null, Problem(statusCode: StatusCodes.Status409Conflict, title: "The workbook of this import is no longer kept (they are kept for 7 days). Upload the file again."));
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return (kept, buffer, null);
    }

    /// <summary>The workbook's sheets and headings; a result only when the file cannot be read.</summary>
    private IActionResult? TryInspect(MemoryStream buffer, out IReadOnlyList<WorkbookSheetDto> sheets)
    {
        try
        {
            buffer.Position = 0;
            sheets = reader.Inspect(buffer);
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            sheets = [];
            return Unreadable();
        }
    }

    private ObjectResult Unreadable()
        => Problem(statusCode: StatusCodes.Status400BadRequest, title: "The workbook could not be read. Open it in Excel, save it as .xlsx and try again.");

    private BadRequestObjectResult TemplateProblems(IReadOnlyList<string> problems)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The workbook does not match the Style Library template.",
            Detail = string.Join(" ", problems),
        };
        problem.Extensions["problems"] = problems;
        return BadRequest(problem);
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
