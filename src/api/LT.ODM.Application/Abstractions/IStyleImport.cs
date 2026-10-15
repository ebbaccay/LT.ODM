using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Abstractions;

/// <summary>Reads a Style Library workbook (.xlsx) into staging rows.</summary>
public interface IStyleWorkbookReader
{
    /// <summary>Each sheet's headings with a few sample values.</summary>
    IReadOnlyList<WorkbookSheetDto> Inspect(Stream xlsx);

    /// <summary>The rows of the mapped sheets and columns, or the problems that stop the upload.</summary>
    StyleWorkbookReadResult Read(Stream xlsx, ImportColumnMapping mapping);
}

/// <summary>Uploaded workbooks kept with their import (Files:Root/style-imports).</summary>
public interface IImportFileStore
{
    /// <summary>Writes the workbook; returns its file name.</summary>
    Task<string> SaveAsync(Stream content, CancellationToken ct = default);

    /// <summary>The workbook, or null when it is not there (any more).</summary>
    Stream? Open(string name);

    void Delete(string name);
}

/// <summary>Style Library import batches (staging.usp_StyleImport_* procedures).</summary>
public interface IStyleImportRepository
{
    Task<StyleImportBatchDto?> GetAsync(int batchId, CancellationToken ct = default);
    Task<IReadOnlyList<StyleImportBatchDto>> ListAsync(CancellationToken ct = default);
    Task<PagedResult<StyleImportStyleDto>> GetStylesAsync(int batchId, string? action, string? search, int skip, int take, CancellationToken ct = default);
    Task<PagedResult<StyleImportIssueDto>> GetIssuesAsync(int batchId, string? severity, string? styleKey, int skip, int take, CancellationToken ct = default);

    /// <summary>Writes New styles and the listed Changed styles. Throws StyleImportException if the batch is not waiting.</summary>
    Task<StyleImportBatchDto> CommitAsync(int batchId, IReadOnlyList<string> overwriteStyleKeys, string committedBy, CancellationToken ct = default);

    Task CancelAsync(int batchId, string cancelledBy, CancellationToken ct = default);

    /// <summary>A new import holding a kept workbook whose columns are being mapped (Status Mapping).</summary>
    Task<StyleImportBatchDto> CreateDraftAsync(string fileName, string storedFile, ImportColumnMapping mapping, string uploadedBy, CancellationToken ct = default);

    Task<StyleImportFile?> GetFileAsync(int batchId, CancellationToken ct = default);

    /// <summary>Saves the mapping being edited. 409 as StyleImportException once committed.</summary>
    Task SaveBatchMappingAsync(int batchId, ImportColumnMapping mapping, CancellationToken ct = default);

    /// <summary>Stages a kept workbook again with this mapping (replacing staged rows) and checks it. 409 when not possible.</summary>
    Task<StyleImportBatchDto> RestageAsync(int batchId, ImportColumnMapping mapping, StyleWorkbook workbook, string uploadedBy, CancellationToken ct = default);

    /// <summary>Deletes an import that was not committed; returns its kept workbook's name (to delete). 404 / 409.</summary>
    Task<string?> DeleteAsync(int batchId, CancellationToken ct = default);

    /// <summary>Releases kept workbooks no longer needed (see usp_StyleImport_Purge); returns the names to delete.</summary>
    Task<IReadOnlyList<string>> PurgeFilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Undoes a commit (within StyleImportRules.RollbackDays), unless styles changed after it: then Blockers lists them
    /// and nothing is changed. 404 / 409 as StyleImportException.
    /// </summary>
    Task<StyleImportRollbackResult> RollbackAsync(int batchId, string rolledBackBy, CancellationToken ct = default);

    /// <summary>Codes the import would add (and values that block or are blanked), with rule suggestions and each list's codes.</summary>
    Task<ImportNewCodesDto> GetNewCodesAsync(int batchId, CancellationToken ct = default);

    /// <summary>
    /// Uses existing codes for values in the staged rows (remembered for later imports when asked) and checks the batch
    /// again. 400 / 404 as StyleImportException.
    /// </summary>
    Task<StyleImportBatchDto> MapCodesAsync(int batchId, IReadOnlyList<MapImportCodeItem> items, bool remember, string mappedBy, CancellationToken ct = default);

    /// <summary>Adds customer Code (if new) and uses it for the accounts in Values, then checks again.</summary>
    Task<StyleImportBatchDto> GroupCustomersAsync(int batchId, GroupImportCustomersRequest request, string mappedBy, CancellationToken ct = default);

    /// <summary>Gives the accounts in Values a brand (added if new) as separate customers, then checks again.</summary>
    Task<StyleImportBatchDto> BrandCustomersAsync(int batchId, BrandImportCustomersRequest request, string mappedBy, CancellationToken ct = default);

    /// <summary>Forgets a remembered answer; false when there was none.</summary>
    Task<bool> ForgetCodeAsync(string list, string value, CancellationToken ct = default);

    /// <summary>Saved column mappings, latest first.</summary>
    Task<IReadOnlyList<ImportMappingDto>> ListMappingsAsync(CancellationToken ct = default);

    /// <summary>Saves a mapping under its name (replacing one with the same name).</summary>
    Task<ImportMappingDto> SaveMappingAsync(string name, ImportColumnMapping mapping, string savedBy, CancellationToken ct = default);

    /// <summary>False when there was no such mapping.</summary>
    Task<bool> DeleteMappingAsync(int mappingId, CancellationToken ct = default);
}
