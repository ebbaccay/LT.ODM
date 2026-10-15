namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// Style Library workbook import (Settings > Import, Admin only). The workbook has three sheets in the customer's terms:
/// Style Header, Article (colorways) and BOM Detail; other layouts are loaded through a column mapping
/// (StyleImportMapping.cs). Rows are staged as text, checked in SQL
/// (staging.usp_StyleImport_Check), previewed, then committed (staging.usp_StyleImport_Commit).
/// </summary>
public static class StyleImportRules
{
    public const long MaxFileBytes = 100L * 1024 * 1024;
    public const int MaxRowsPerSheet = 500_000;
    public const int MaxPageSize = 500;

    /// <summary>The first problems found reading a workbook (enough to fix the file, not a full report).</summary>
    public const int MaxReadProblems = 20;

    /// <summary>A commit can be rolled back for this many days (its rollback data is kept as long).</summary>
    public const int RollbackDays = 30;
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

/// <summary>
/// Status: Mapping (file read, columns not mapped yet), Staged, Committed, Cancelled or RolledBack.
/// CanRollBack: committed recently, with its rollback data. HasFile: the workbook is kept, so it can be mapped (again).
/// </summary>
public sealed record StyleImportBatchDto(
    int BatchId,
    string FileName,
    string Status,
    StyleImportSummary? Summary,
    string UploadedBy,
    DateTime UploadedUtc,
    string? FinishedBy,
    DateTime? FinishedUtc,
    string? RolledBackBy = null,
    DateTime? RolledBackUtc = null,
    bool CanRollBack = false,
    bool HasFile = false);

/// <summary>An import's kept workbook (StoredFile null: not kept) and the mapping chosen for it.</summary>
public sealed record StyleImportFile(int BatchId, string FileName, string Status, string? StoredFile, ImportColumnMapping? Mapping);

/// <summary>Remembered: answers from earlier imports applied to this one (staging.ImportAppliedAlias).</summary>
public sealed record StyleImportSummary(
    StyleImportStyleCounts Styles,
    StyleImportRowCounts Rows,
    int BomLines,
    int Errors,
    int Warnings,
    StyleImportCommitCounts? Committed,
    int Remembered = 0,
    StyleImportRollbackCounts? RolledBack = null,
    StyleImportSkippedCounts? SkippedBom = null);

/// <summary>BOM rows without a material code: flagged as warnings and skipped on commit (their styles import without them).</summary>
public sealed record StyleImportSkippedCounts(int Rows, int Styles);

/// <summary>What a rollback did: styles removed (the import added them), restored (it had replaced them), codes removed.</summary>
public sealed record StyleImportRollbackCounts(int Removed, int Restored, int Codes);

/// <summary>Why a rollback stopped: a style (or, without StyleNo, a material) changed after the import.</summary>
public sealed record StyleImportRollbackBlockerDto(string? StyleNo, string? SeasonCode, string Reason);

/// <summary>Blockers empty: rolled back. Otherwise nothing was changed.</summary>
public sealed record StyleImportRollbackResult(StyleImportBatchDto Batch, IReadOnlyList<StyleImportRollbackBlockerDto> Blockers);

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
        ["season", "customer", "brand", "customerBrand", "productType", "weaveType", "businessUnit", "gender", "status", "contentClass", "materialType", "uom", "supplier"];
}

/// <summary>
/// Blocking: the rows are blocked until mapped (content class, status). Suggested*: an existing code with the same spelling key.
/// Brand: for a customer account, the brand the file (Customer_Brand) or an answer gives it.
/// </summary>
public sealed record ImportNewCodeDto(
    string List, string Value, string? Label, int Rows, int Styles, bool Blocking, string? SuggestedCode, string? SuggestedName, string? Brand = null);

public sealed record ImportCodeOptionDto(string List, string Code, string? Name);

/// <summary>Groups: new customer accounts that look like one customer. Applied: remembered answers this import used.</summary>
public sealed record ImportNewCodesDto(
    IReadOnlyList<ImportNewCodeDto> Codes,
    IReadOnlyList<ImportCodeOptionDto> Options,
    IReadOnlyList<ImportCustomerGroupDto>? Groups = null,
    IReadOnlyList<ImportAppliedAliasDto>? Applied = null);

/// <summary>Key: the brand word the accounts share (POLO). Suggested*: an existing customer they probably are (SKE).</summary>
public sealed record ImportCustomerGroupDto(string Key, IReadOnlyList<ImportNewCodeDto> Accounts, string? SuggestedCode, string? SuggestedName);

/// <summary>A remembered answer applied to an import: Value in the file was replaced by Code in Rows rows.</summary>
public sealed record ImportAppliedAliasDto(string List, string Value, string Code, int Rows);

/// <summary>Remember: keep the answer for later imports (applied on upload).</summary>
public sealed record MapImportCodeRequest(string List, string Value, string Code, bool Remember = true);

public sealed record MapImportCodeItem(string? List, string? Value, string? Code);

/// <summary>Several values at once ("Use all"), checked once.</summary>
public sealed record MapImportCodesRequest(IReadOnlyList<MapImportCodeItem>? Items, bool Remember = true);

/// <summary>New customer accounts that are one customer: Code (new, or an existing customer) and its name if new.</summary>
public sealed record GroupImportCustomersRequest(IReadOnlyList<string>? Values, string? Code, string? Name, bool Remember = true);

public sealed record ForgetImportCodeRequest(string? List, string? Value);

/// <summary>
/// Customer accounts that stay separate customers under one brand: Brand (new or existing) and its name if new;
/// AlsoCustomer: an existing customer they look like (SKE), given the brand too if it has none.
/// </summary>
public sealed record BrandImportCustomersRequest(IReadOnlyList<string>? Values, string? Brand, string? Name, string? AlsoCustomer, bool Remember = true);

/// <summary>An AI suggestion: Code null = keep the value as a new code.</summary>
public sealed record ImportCodeSuggestionDto(string List, string Value, string? Code, string Reason);

