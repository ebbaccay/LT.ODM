using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Style Library import batches: staging.usp_StyleImport_* (db/procedures/style.import.procedures.sql).</summary>
public sealed class StyleImportRepository(IDbConnectionFactory connectionFactory, StyleDashboardCache dashboard) : IStyleImportRepository
{
    /// <summary>Checking and committing ~100k rows takes a few seconds; allow for much larger files.</summary>
    private const int LongCommandSeconds = 600;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class BatchRow
    {
        public int BatchId { get; init; }
        public string FileName { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Summary { get; init; }
        public string UploadedBy { get; init; } = "";
        public DateTime UploadedUtc { get; init; }
        public string? FinishedBy { get; init; }
        public DateTime? FinishedUtc { get; init; }
        public string? RolledBackBy { get; init; }
        public DateTime? RolledBackUtc { get; init; }
        public bool CanRollBack { get; init; }
        public bool HasFile { get; init; }

        public StyleImportBatchDto ToDto() => new(BatchId, FileName, Status,
            Summary is null ? null : JsonSerializer.Deserialize<StyleImportSummary>(Summary, Json),
            UploadedBy, DateTime.SpecifyKind(UploadedUtc, DateTimeKind.Utc), FinishedBy,
            FinishedUtc is { } f ? DateTime.SpecifyKind(f, DateTimeKind.Utc) : null,
            RolledBackBy, RolledBackUtc is { } r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null, CanRollBack, HasFile);
    }

    public async Task<StyleImportBatchDto> CreateDraftAsync(string fileName, string storedFile, ImportColumnMapping mapping, string uploadedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var batchId = await conn.QuerySingleAsync<int>(Proc("staging.usp_StyleImport_Create", new
        {
            FileName = fileName, UploadedBy = uploadedBy, StoredFile = storedFile, Mapping = JsonSerializer.Serialize(mapping, Json), Status = "Mapping",
        }, ct));
        return (await conn.QuerySingleAsync<BatchRow>(Proc("staging.usp_StyleImport_Get", new { BatchId = batchId }, ct))).ToDto();
    }

    private sealed class FileRow
    {
        public int BatchId { get; init; }
        public string FileName { get; init; } = "";
        public string Status { get; init; } = "";
        public string? StoredFile { get; init; }
        public string? Mapping { get; init; }
    }

    public async Task<StyleImportFile?> GetFileAsync(int batchId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<FileRow>(Proc("staging.usp_StyleImport_GetFile", new { BatchId = batchId }, ct));
        if (row is null) return null;
        ImportColumnMapping? mapping = null;
        try
        {
            mapping = row.Mapping is null ? null : JsonSerializer.Deserialize<ImportColumnMapping>(row.Mapping, Json);
        }
        catch (JsonException)
        {
            mapping = null;   // unreadable: the page suggests one again
        }
        return new StyleImportFile(row.BatchId, row.FileName, row.Status, row.StoredFile,
            mapping?.Sheets is null ? null : StyleImportMapper.Clean(mapping));
    }

    public async Task SaveBatchMappingAsync(int batchId, ImportColumnMapping mapping, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await RuleErrors(() => conn.ExecuteAsync(Proc("staging.usp_StyleImport_SaveMapping",
            new { BatchId = batchId, Mapping = JsonSerializer.Serialize(mapping, Json) }, ct)));
    }

    public async Task<StyleImportBatchDto> RestageAsync(int batchId, ImportColumnMapping mapping, StyleWorkbook workbook, string uploadedBy, CancellationToken ct = default)
    {
        await using var conn = (SqlConnection)await connectionFactory.OpenAsync(ct);
        await RuleErrors(() => conn.ExecuteAsync(Proc("staging.usp_StyleImport_Restage",
            new { BatchId = batchId, Mapping = JsonSerializer.Serialize(mapping, Json), UploadedBy = uploadedBy }, ct)));
        return await LoadAndCheckAsync(conn, batchId, workbook, uploadedBy, ct);
    }

    public async Task<string?> DeleteAsync(int batchId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await RuleErrors(() => conn.QuerySingleAsync<string?>(Proc("staging.usp_StyleImport_Delete", new { BatchId = batchId }, ct)));
    }

    public async Task<IReadOnlyList<string>> PurgeFilesAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<string>(Proc("staging.usp_StyleImport_Purge", null, ct))).ToList();
    }

    /// <summary>Bulk-copies the sheets into a batch and checks it; on failure the batch is cancelled.</summary>
    private async Task<StyleImportBatchDto> LoadAndCheckAsync(SqlConnection conn, int batchId, StyleWorkbook workbook, string uploadedBy, CancellationToken ct)
    {
        try
        {
            await BulkCopyAsync(conn, "staging.StyleRow", batchId, StyleImportLayout.Style, workbook.Styles, ct);
            await BulkCopyAsync(conn, "staging.ArticleRow", batchId, StyleImportLayout.Article, workbook.Articles, ct);
            await BulkCopyAsync(conn, "staging.BomRow", batchId, StyleImportLayout.Bom, workbook.Bom, ct);
            var batch = await conn.QuerySingleAsync<BatchRow>(Proc("staging.usp_StyleImport_Check", new { BatchId = batchId }, ct, LongCommandSeconds));
            return batch.ToDto();
        }
        catch
        {
            // Do not leave a half-staged batch waiting; the caller sees the original error.
            await conn.ExecuteAsync(Proc("staging.usp_StyleImport_Cancel", new { BatchId = batchId, CancelledBy = uploadedBy }, CancellationToken.None));
            throw;
        }
    }

    public async Task<StyleImportBatchDto?> GetAsync(int batchId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<BatchRow>(Proc("staging.usp_StyleImport_Get", new { BatchId = batchId }, ct));
        return row?.ToDto();
    }

    public async Task<IReadOnlyList<StyleImportBatchDto>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<BatchRow>(Proc("staging.usp_StyleImport_List", new { Top = 20 }, ct));
        return rows.Select(r => r.ToDto()).ToList();
    }

    private sealed record StyleRow(string StyleKey, string CustomerCode, string SeasonCode, string StyleNo, string? ModelName, string Action,
        int? ExistingStyleId, string? Changes, int ColorwayCount, int BomLineCount, int ErrorCount, int WarningCount, bool Committed, int TotalCount);

    public async Task<PagedResult<StyleImportStyleDto>> GetStylesAsync(int batchId, string? action, string? search, int skip, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<StyleRow>(Proc("staging.usp_StyleImport_GetStyles",
            new { BatchId = batchId, Action = action, Search = search, Skip = skip, Take = take }, ct))).ToList();
        return new PagedResult<StyleImportStyleDto>(
            rows.Select(r => new StyleImportStyleDto(r.StyleKey, r.CustomerCode, r.SeasonCode, r.StyleNo, r.ModelName, r.Action, r.ExistingStyleId,
                r.Changes, r.ColorwayCount, r.BomLineCount, r.ErrorCount, r.WarningCount, r.Committed)).ToList(),
            rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    private sealed record IssueRow(string Sheet, int? RowNo, string Severity, string? StyleKey, string Message, int TotalCount);

    public async Task<PagedResult<StyleImportIssueDto>> GetIssuesAsync(int batchId, string? severity, string? styleKey, int skip, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<IssueRow>(Proc("staging.usp_StyleImport_GetIssues",
            new { BatchId = batchId, Severity = severity, StyleKey = styleKey, Skip = skip, Take = take }, ct))).ToList();
        return new PagedResult<StyleImportIssueDto>(
            rows.Select(r => new StyleImportIssueDto(r.Sheet, r.RowNo, r.Severity, r.StyleKey, r.Message)).ToList(),
            rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<StyleImportBatchDto> CommitAsync(int batchId, IReadOnlyList<string> overwriteStyleKeys, string committedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await RuleErrors(() => conn.QuerySingleAsync<BatchRow>(Proc("staging.usp_StyleImport_Commit", new
        {
            BatchId = batchId,
            OverwriteStyleKeys = JsonSerializer.Serialize(overwriteStyleKeys),
            CommittedBy = committedBy,
        }, ct, LongCommandSeconds)));
        dashboard.Invalidate();
        return row.ToDto();
    }

    public async Task<ImportNewCodesDto> GetNewCodesAsync(int batchId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("staging.usp_StyleImport_NewCodes", new { BatchId = batchId }, ct));
        var codes = (await grid.ReadAsync<ImportNewCodeDto>()).ToList();
        var options = (await grid.ReadAsync<ImportCodeOptionDto>()).ToList();
        var applied = (await conn.QueryAsync<ImportAppliedAliasDto>(Proc("staging.usp_StyleImport_AppliedAliases", new { BatchId = batchId }, ct))).ToList();
        return new ImportNewCodesDto(codes, options, ImportCustomerGrouping.Build(codes, options), applied);
    }

    public async Task<StyleImportBatchDto> MapCodesAsync(int batchId, IReadOnlyList<MapImportCodeItem> items, bool remember, string mappedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await RuleErrors(() => conn.QuerySingleAsync<BatchRow>(Proc("staging.usp_StyleImport_MapCodes", new
        {
            BatchId = batchId, Items = JsonSerializer.Serialize(items, Json), Remember = remember, MappedBy = mappedBy,
        }, ct, LongCommandSeconds)));
        return row.ToDto();
    }

    public async Task<StyleImportBatchDto> GroupCustomersAsync(int batchId, GroupImportCustomersRequest r, string mappedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await RuleErrors(() => conn.QuerySingleAsync<BatchRow>(Proc("staging.usp_StyleImport_GroupCustomers", new
        {
            BatchId = batchId, Values = JsonSerializer.Serialize(r.Values ?? []), r.Code, r.Name, r.Remember, MappedBy = mappedBy,
        }, ct, LongCommandSeconds)));
        dashboard.Invalidate();
        return row.ToDto();
    }

    public async Task<StyleImportBatchDto> BrandCustomersAsync(int batchId, BrandImportCustomersRequest r, string mappedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await RuleErrors(() => conn.QuerySingleAsync<BatchRow>(Proc("staging.usp_StyleImport_BrandCustomers", new
        {
            BatchId = batchId, Values = JsonSerializer.Serialize(r.Values ?? []), r.Brand, r.Name, r.AlsoCustomer, r.Remember, MappedBy = mappedBy,
        }, ct, LongCommandSeconds)));
        return row.ToDto();
    }

    public async Task<bool> ForgetCodeAsync(string list, string value, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("staging.usp_StyleImport_ForgetAlias", new { List = list, Value = value }, ct)) > 0;
    }

    public async Task<StyleImportRollbackResult> RollbackAsync(int batchId, string rolledBackBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var result = await RuleErrors(async () =>
        {
            using var grid = await conn.QueryMultipleAsync(Proc("staging.usp_StyleImport_Rollback",
                new { BatchId = batchId, RolledBackBy = rolledBackBy, MaxAgeDays = StyleImportRules.RollbackDays }, ct, LongCommandSeconds));
            var blockers = (await grid.ReadAsync<StyleImportRollbackBlockerDto>()).ToList();
            var batch = await grid.ReadSingleAsync<BatchRow>();
            return new StyleImportRollbackResult(batch.ToDto(), blockers);
        });
        if (result.Blockers.Count == 0) dashboard.Invalidate();
        return result;
    }

    public async Task CancelAsync(int batchId, string cancelledBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await RuleErrors(() => conn.ExecuteAsync(Proc("staging.usp_StyleImport_Cancel", new { BatchId = batchId, CancelledBy = cancelledBy }, ct)));
    }

    private sealed class MappingRow
    {
        public int MappingId { get; init; }
        public string Name { get; init; } = "";
        public string Definition { get; init; } = "{}";
        public string UpdatedBy { get; init; } = "";
        public DateTime UpdatedUtc { get; init; }

        public ImportMappingDto ToDto() => new(MappingId, Name,
            StyleImportMapper.Clean(JsonSerializer.Deserialize<ImportColumnMapping>(Definition, Json) ?? new ImportColumnMapping(new Dictionary<string, ImportSheetMapping>())),
            UpdatedBy, DateTime.SpecifyKind(UpdatedUtc, DateTimeKind.Utc));
    }

    public async Task<IReadOnlyList<ImportMappingDto>> ListMappingsAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<MappingRow>(Proc("staging.usp_ImportMapping_List", null, ct));
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<ImportMappingDto> SaveMappingAsync(string name, ImportColumnMapping mapping, string savedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await RuleErrors(() => conn.QuerySingleAsync<MappingRow>(Proc("staging.usp_ImportMapping_Save", new
        {
            Name = name, Definition = JsonSerializer.Serialize(mapping, Json), SavedBy = savedBy,
        }, ct)));
        return row.ToDto();
    }

    public async Task<bool> DeleteMappingAsync(int mappingId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("staging.usp_ImportMapping_Delete", new { MappingId = mappingId }, ct)) > 0;
    }

    /// <summary>Streams one sheet into its staging table (BatchId, RowNo, then the sheet's columns in order).</summary>
    private static async Task BulkCopyAsync(SqlConnection conn, string table, int batchId, ImportTargetSheet sheet,
        IReadOnlyList<StagedRow> rows, CancellationToken ct)
    {
        using var data = new DataTable();
        data.Columns.Add("BatchId", typeof(int));
        data.Columns.Add("RowNo", typeof(int));
        foreach (var column in sheet.Columns)
            data.Columns.Add(column.Name, column.IsDate ? typeof(DateTime) : typeof(string));
        foreach (var row in rows)
        {
            var values = new object?[row.Values.Length + 2];
            values[0] = batchId;
            values[1] = row.RowNo;
            for (var i = 0; i < row.Values.Length; i++) values[i + 2] = row.Values[i] ?? DBNull.Value;
            data.Rows.Add(values);
        }

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, null)
        {
            DestinationTableName = table,
            BatchSize = 10_000,
            BulkCopyTimeout = LongCommandSeconds,
        };
        foreach (DataColumn column in data.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        await bulk.WriteToServerAsync(data, ct);
    }

    /// <summary>THROW 50404 / 50409 / 50400 from the procedures -> 404 / 409 / 400 for the caller.</summary>
    private static async Task<T> RuleErrors<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            var status = ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400;
            throw new StyleImportException(status, ex.Message);
        }
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct, int? timeoutSeconds = null)
        => new(name, parameters, commandType: CommandType.StoredProcedure, commandTimeout: timeoutSeconds, cancellationToken: ct);
}
