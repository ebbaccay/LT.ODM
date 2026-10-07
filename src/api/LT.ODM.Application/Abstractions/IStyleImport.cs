using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Abstractions;

/// <summary>Reads a Style Library workbook (.xlsx) into staging rows.</summary>
public interface IStyleWorkbookReader
{
    StyleWorkbookReadResult Read(Stream xlsx);
}

/// <summary>Style Library import batches (staging.usp_StyleImport_* procedures).</summary>
public interface IStyleImportRepository
{
    /// <summary>Creates a batch, stages the workbook and checks it. Returns the batch with its summary.</summary>
    Task<StyleImportBatchDto> StageAsync(string fileName, StyleWorkbook workbook, string uploadedBy, CancellationToken ct = default);

    Task<StyleImportBatchDto?> GetAsync(int batchId, CancellationToken ct = default);
    Task<IReadOnlyList<StyleImportBatchDto>> ListAsync(CancellationToken ct = default);
    Task<PagedResult<StyleImportStyleDto>> GetStylesAsync(int batchId, string? action, string? search, int skip, int take, CancellationToken ct = default);
    Task<PagedResult<StyleImportIssueDto>> GetIssuesAsync(int batchId, string? severity, string? styleKey, int skip, int take, CancellationToken ct = default);

    /// <summary>Writes New styles and the listed Changed styles. Throws StyleImportException if the batch is not waiting.</summary>
    Task<StyleImportBatchDto> CommitAsync(int batchId, IReadOnlyList<string> overwriteStyleKeys, string committedBy, CancellationToken ct = default);

    Task CancelAsync(int batchId, string cancelledBy, CancellationToken ct = default);

    /// <summary>Codes the import would add (and values that block or are blanked), with rule suggestions and each list's codes.</summary>
    Task<ImportNewCodesDto> GetNewCodesAsync(int batchId, CancellationToken ct = default);

    /// <summary>Uses an existing code for a value in the staged rows and checks the batch again. 400 / 404 as StyleImportException.</summary>
    Task<StyleImportBatchDto> MapCodeAsync(int batchId, MapImportCodeRequest request, CancellationToken ct = default);
}
