namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// Style Library workbook import (Settings > Import, Admin only). The workbook has three sheets in the customer's terms:
/// Style Header, Article (colorways) and BOM Detail. Rows are staged as text, checked in SQL
/// (staging.usp_StyleImport_Check), previewed, then committed (staging.usp_StyleImport_Commit).
/// </summary>
public static class StyleImportRules
{
    public const long MaxFileBytes = 50L * 1024 * 1024;
    public const int MaxRowsPerSheet = 500_000;
    public const int MaxPageSize = 500;

    /// <summary>The first problems found reading a workbook (enough to fix the file, not a full report).</summary>
    public const int MaxReadProblems = 20;
}

/// <summary>Raised for import requests that cannot go ahead (unknown batch, already committed, ...).</summary>
public sealed class StyleImportException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>The three sheets as read from the workbook: one row per Excel row, values in staging column order.</summary>
public sealed record StyleWorkbook(IReadOnlyList<StagedRow> Styles, IReadOnlyList<StagedRow> Articles, IReadOnlyList<StagedRow> Bom);

/// <summary>An Excel row number and its values (text, or DateTime for Create_dt), in staging column order.</summary>
public sealed record StagedRow(int RowNo, object?[] Values);

/// <summary>Either a workbook, or the problems that stop it from being staged (missing sheet or heading, value too long).</summary>
public sealed record StyleWorkbookReadResult(StyleWorkbook? Workbook, IReadOnlyList<string> Problems);

public sealed record StyleImportBatchDto(
    int BatchId,
    string FileName,
    string Status,
    StyleImportSummary? Summary,
    string UploadedBy,
    DateTime UploadedUtc,
    string? FinishedBy,
    DateTime? FinishedUtc);

public sealed record StyleImportSummary(
    StyleImportStyleCounts Styles,
    StyleImportRowCounts Rows,
    int BomLines,
    int Errors,
    int Warnings,
    StyleImportCommitCounts? Committed);

public sealed record StyleImportStyleCounts(int Total, int New, int Changed, int Unchanged, int Blocked);

public sealed record StyleImportRowCounts(int Styles, int Colorways, int Bom);

public sealed record StyleImportCommitCounts(int Styles, int New, int Replaced, int BomLines);

/// <summary>What committing does to one style: New, Changed (written only if ticked), Unchanged or Blocked (has errors).</summary>
public sealed record StyleImportStyleDto(
    string StyleKey,
    string CustomerCode,
    string SeasonCode,
    string StyleNo,
    string? ModelName,
    string Action,
    int? ExistingStyleId,
    string? Changes,
    int ColorwayCount,
    int BomLineCount,
    int ErrorCount,
    int WarningCount,
    bool Committed);

/// <summary>Sheet is Style, Article or BOM; RowNo is the Excel row number.</summary>
public sealed record StyleImportIssueDto(string Sheet, int? RowNo, string Severity, string? StyleKey, string Message);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);

/// <summary>StyleKeys (Cust|Season|Style) of the Changed styles to overwrite. New styles are always written.</summary>
public sealed record CommitStyleImportRequest(IReadOnlyList<string>? OverwriteStyleKeys);

// ----- New codes (preview tab): codes the import would add, or values that block / are blanked -----

public static class ImportCodeLists
{
    public static readonly string[] All =
        ["customer", "productType", "weaveType", "businessUnit", "gender", "status", "contentClass", "materialType", "uom", "supplier"];
}

/// <summary>Blocking: the rows are blocked until mapped (content class, status). Suggested*: an existing code with the same spelling key.</summary>
public sealed record ImportNewCodeDto(
    string List, string Value, string? Label, int Rows, int Styles, bool Blocking, string? SuggestedCode, string? SuggestedName);

public sealed record ImportCodeOptionDto(string List, string Code, string? Name);

public sealed record ImportNewCodesDto(IReadOnlyList<ImportNewCodeDto> Codes, IReadOnlyList<ImportCodeOptionDto> Options);

public sealed record MapImportCodeRequest(string List, string Value, string Code);

/// <summary>An AI suggestion: Code null = keep the value as a new code.</summary>
public sealed record ImportCodeSuggestionDto(string List, string Value, string? Code, string Reason);

