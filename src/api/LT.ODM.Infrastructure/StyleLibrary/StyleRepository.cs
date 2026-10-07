using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Styles screens: style.usp_* procedures (db/procedures/style.procedures.sql).</summary>
public sealed class StyleRepository(IDbConnectionFactory connectionFactory) : IStyleRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    // ----- Reads -----

    public async Task<StyleLookupsDto> GetLookupsAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("style.usp_Style_Lookups", null, ct));
        async Task<IReadOnlyList<LookupItem>> Next() => (await grid.ReadAsync<LookupRow>()).Select(r => new LookupItem(r.Code, r.Name)).ToList();
        return new StyleLookupsDto(await Next(), await Next(), await Next(), await Next(), await Next(), await Next(), await Next(), await Next(),
            await Next(), await Next());
    }

    public async Task<PagedResult<StyleListItemDto>> ListAsync(StyleListQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<ListRow>(Proc("style.usp_Style_List", new
        {
            q.Search, CustomerCode = q.Customer, SeasonCode = q.Season, BusinessUnitCode = q.BusinessUnit, ProductTypeCode = q.ProductType,
            WeaveTypeCode = q.WeaveType, q.Gender, q.Material, q.Skip, q.Take,
        }, ct))).ToList();
        return new PagedResult<StyleListItemDto>(rows.Select(r => new StyleListItemDto(
            r.StyleId, r.StyleNo, r.BaseStyleNo, r.Description, r.ModelCode, r.ModelName, r.CustomerCode, r.CustomerName, r.SeasonCode,
            r.BusinessUnitCode, r.BusinessUnitName, r.ProductTypeCode, r.ProductTypeName, r.WeaveTypeCode, r.Gender, r.ImageUrl, r.SketchUrl,
            r.ColorwayCount, r.BomLineCount, r.HasHistory, Utc(r.LastChangedUtc))).ToList(), rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<StyleDetailDto?> GetAsync(int styleId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("style.usp_Style_Get", new { StyleId = styleId }, ct));
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>();
        if (h is null) return null;
        var colorways = (await grid.ReadAsync<ColorwayRow>()).ToList();
        var lines = (await grid.ReadAsync<LineRow>()).ToList();
        var lineColors = (await grid.ReadAsync<LineColorRow>()).ToLookup(r => r.BomLineId);
        var family = (await grid.ReadAsync<FamilyRow>()).ToList();

        return new StyleDetailDto(
            new StyleHeaderDto(h.StyleId, h.CustomerCode, h.CustomerName, h.SeasonCode, h.StyleNo, h.BaseStyleNo, h.Description, h.ModelCode, h.ModelName,
                h.WeaveTypeCode, h.WeaveTypeName, h.ProductTypeCode, h.ProductTypeName, h.Gender, h.GarmentLeadTimeDays, h.BusinessUnitCode,
                h.BusinessUnitName, h.SketchUrl, h.ImageUrl, Utc(h.SourceCreatedUtc), h.ImportBatchId, h.CreatedBy, Utc(h.CreatedUtc), h.UpdatedBy,
                Utc(h.UpdatedUtc), h.RowVer),
            colorways.Select(c => new ColorwayDto(c.ColorwayId, c.SortOrder, c.ColorwayCode, c.ColorwayName, c.Status, c.ImageUrl, c.CreatedBy,
                Utc(c.CreatedUtc), c.UpdatedBy, Utc(c.UpdatedUtc), c.RowVer)).ToList(),
            lines.Select(l => new BomLineDto(l.BomLineId, l.LineSeq, l.PartNo, l.MaterialCode, l.MaterialDescription, l.MaterialTypeCode, l.MaterialTypeName,
                l.ContentClassCode, l.ContentClassName, l.NominatedSupplierCode, l.NominatedSupplierName, l.SupplierCode, l.SupplierName,
                l.LcoConsumption, l.BrandConsumption, l.UomCode, l.ImageUrl,
                lineColors[l.BomLineId].Select(x => new BomLineColorwayDto(x.ColorwayId, x.MaterialColorCode, x.MaterialColorDescription)).ToList(),
                l.CreatedBy, Utc(l.CreatedUtc), l.UpdatedBy, Utc(l.UpdatedUtc), l.RowVer, l.MaterialId)).ToList(),
            family.Select(f => new StyleFamilyMemberDto(f.StyleId, f.StyleNo, f.SeasonCode, f.ModelName, f.ImageUrl, f.SourceStyleId, f.Relation,
                f.Suffix, f.LinkSource, f.Note, f.LinkedBy, f.IsCurrent)).ToList());
    }

    public async Task<StyleDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("style.usp_Dashboard_Get", null, ct));
        var totals = await grid.ReadSingleAsync<StyleDashboardTotalsDto>();
        var seasons = (await grid.ReadAsync<DashboardSeasonDto>()).ToList();
        var customers = (await grid.ReadAsync<CountRow>()).Select(r => r.ToDto()).ToList();
        var materialTypes = (await grid.ReadAsync<CountRow>()).Select(r => r.ToDto()).ToList();
        var materials = (await grid.ReadAsync<DashboardMaterialDto>()).ToList();
        var suppliers = (await grid.ReadAsync<CountRow>()).Select(r => r.ToDto()).ToList();
        var productTypes = (await grid.ReadAsync<CountRow>()).Select(r => r.ToDto()).ToList();
        var recent = (await grid.ReadAsync<RecentRow>()).Select(r => new DashboardRecentStyleDto(r.StyleId, r.StyleNo, r.ModelName, r.CustomerCode,
            r.SeasonCode, r.ImageUrl, r.SketchUrl, r.ColorwayCount, r.BomLineCount, r.LastChangedBy, Utc(r.LastChangedUtc))).ToList();
        var import = await grid.ReadSingleOrDefaultAsync<ImportRow>();
        return new StyleDashboardDto(totals, seasons, customers, materialTypes, materials, suppliers, productTypes, recent,
            import is null ? null : new DashboardImportDto(import.BatchId, import.FileName, import.FinishedBy, Utc(import.FinishedUtc)));
    }

    // ----- Writes -----

    public Task<int> SaveStyleAsync(int? styleId, SaveStyleRequest r, string changedBy, CancellationToken ct = default)
        => Scalar("style.usp_Style_Save", new
        {
            StyleId = styleId, RowVer = styleId is null ? null : r.RowVer,
            CustomerCode = StyleValidation.Clean(r.CustomerCode)?.ToUpperInvariant(), SeasonCode = StyleValidation.Clean(r.SeasonCode)?.ToUpperInvariant(),
            StyleNo = StyleValidation.Clean(r.StyleNo), Description = StyleValidation.Clean(r.Description), ModelCode = StyleValidation.Clean(r.ModelCode),
            ModelName = StyleValidation.Clean(r.ModelName), WeaveTypeCode = StyleValidation.Clean(r.WeaveTypeCode)?.ToUpperInvariant(),
            ProductTypeCode = StyleValidation.Clean(r.ProductTypeCode), Gender = StyleValidation.Clean(r.Gender)?.ToUpperInvariant(),
            r.GarmentLeadTimeDays, BusinessUnitCode = StyleValidation.Clean(r.BusinessUnitCode),
            SketchUrl = StyleValidation.Clean(r.SketchUrl), ImageUrl = StyleValidation.Clean(r.ImageUrl), ChangedBy = changedBy,
        }, ct);

    public Task DeleteStyleAsync(int styleId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Execute("style.usp_Style_Delete", new { StyleId = styleId, RowVer = rowVer, ChangedBy = changedBy }, ct);

    public Task<int> CopyStyleAsync(int styleId, CopyStyleRequest r, string changedBy, CancellationToken ct = default)
        => Scalar("style.usp_Style_Copy", new
        {
            StyleId = styleId, SeasonCode = StyleValidation.Clean(r.SeasonCode)?.ToUpperInvariant(), StyleNo = StyleValidation.Clean(r.StyleNo), ChangedBy = changedBy,
        }, ct);

    public Task<int> SaveColorwayAsync(int styleId, int? colorwayId, SaveColorwayRequest r, string changedBy, CancellationToken ct = default)
        => Scalar("style.usp_Colorway_Save", new
        {
            ColorwayId = colorwayId, StyleId = styleId, RowVer = colorwayId is null ? null : r.RowVer,
            ColorwayCode = StyleValidation.Clean(r.ColorwayCode), ColorwayName = StyleValidation.Clean(r.ColorwayName),
            Status = StyleValidation.Clean(r.Status)?.ToUpperInvariant() ?? "INRANGE", r.SortOrder, ImageUrl = StyleValidation.Clean(r.ImageUrl),
            ChangedBy = changedBy,
        }, ct);

    public Task DeleteColorwayAsync(int colorwayId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Execute("style.usp_Colorway_Delete", new { ColorwayId = colorwayId, RowVer = rowVer, ChangedBy = changedBy }, ct);

    public Task<int> SaveBomLineAsync(int styleId, int? bomLineId, SaveBomLineRequest r, string changedBy, CancellationToken ct = default)
        => Scalar("style.usp_BomLine_Save", new
        {
            BomLineId = bomLineId, StyleId = styleId, RowVer = bomLineId is null ? null : r.RowVer, r.PartNo,
            MaterialCode = StyleValidation.Clean(r.MaterialCode), MaterialDescription = StyleValidation.Clean(r.MaterialDescription),
            MaterialTypeCode = StyleValidation.Clean(r.MaterialTypeCode)?.ToUpperInvariant(), ContentClassCode = StyleValidation.Clean(r.ContentClassCode)?.ToUpperInvariant(),
            NominatedSupplierCode = StyleValidation.Clean(r.NominatedSupplierCode), NominatedSupplierName = StyleValidation.Clean(r.NominatedSupplierName),
            SupplierCode = StyleValidation.Clean(r.SupplierCode), SupplierName = StyleValidation.Clean(r.SupplierName),
            r.LcoConsumption, r.BrandConsumption, UomCode = StyleValidation.Clean(r.UomCode), ImageUrl = StyleValidation.Clean(r.ImageUrl),
            Colorways = JsonSerializer.Serialize(r.Colorways ?? [], Json), ChangedBy = changedBy,
        }, ct);

    public Task DeleteBomLineAsync(int bomLineId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Execute("style.usp_BomLine_Delete", new { BomLineId = bomLineId, RowVer = rowVer, ChangedBy = changedBy }, ct);

    public Task SetHistoryAsync(int styleId, SetStyleHistoryRequest r, string changedBy, CancellationToken ct = default)
        => Execute("style.usp_StyleHistory_Set", new
        {
            StyleId = styleId, r.SourceStyleId, r.Relation, Note = StyleValidation.Clean(r.Note), ChangedBy = changedBy,
        }, ct);

    public Task RemoveHistoryAsync(int styleId, string changedBy, CancellationToken ct = default)
        => Execute("style.usp_StyleHistory_Remove", new { StyleId = styleId, ChangedBy = changedBy }, ct);

    private async Task<int> Scalar(string proc, object parameters, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await RuleErrors(() => conn.QuerySingleAsync<int>(Proc(proc, parameters, ct)));
    }

    private async Task Execute(string proc, object parameters, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await RuleErrors(() => conn.ExecuteAsync(Proc(proc, parameters, ct)));
    }

    /// <summary>THROW 50400 / 50404 / 50409 -> StyleRuleException with that HTTP status.</summary>
    private static async Task<T> RuleErrors<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    // ----- Row shapes -----

    private sealed class CountRow
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public int Styles { get; init; }
        public int? Colorways { get; init; }
        public int? BomLines { get; init; }
        public DashboardCountDto ToDto() => new(Code, Name, Styles, Colorways, BomLines);
    }

    private sealed class RecentRow
    {
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string? ModelName { get; init; }
        public string CustomerCode { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public int ColorwayCount { get; init; }
        public int BomLineCount { get; init; }
        public string LastChangedBy { get; init; } = "";
        public DateTime LastChangedUtc { get; init; }
    }

    private sealed class ImportRow
    {
        public int BatchId { get; init; }
        public string FileName { get; init; } = "";
        public string? FinishedBy { get; init; }
        public DateTime? FinishedUtc { get; init; }
    }

    private sealed class LookupRow { public string Code { get; init; } = ""; public string Name { get; init; } = ""; }

    private sealed class ListRow
    {
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string BaseStyleNo { get; init; } = "";
        public string? Description { get; init; }
        public string? ModelCode { get; init; }
        public string? ModelName { get; init; }
        public string CustomerCode { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string? BusinessUnitCode { get; init; }
        public string? BusinessUnitName { get; init; }
        public string? ProductTypeCode { get; init; }
        public string? ProductTypeName { get; init; }
        public string? WeaveTypeCode { get; init; }
        public string? Gender { get; init; }
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public int ColorwayCount { get; init; }
        public int BomLineCount { get; init; }
        public bool HasHistory { get; init; }
        public DateTime LastChangedUtc { get; init; }
        public int TotalCount { get; init; }
    }

    private sealed class HeaderRow
    {
        public int StyleId { get; init; }
        public string CustomerCode { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string StyleNo { get; init; } = "";
        public string BaseStyleNo { get; init; } = "";
        public string? Description { get; init; }
        public string? ModelCode { get; init; }
        public string? ModelName { get; init; }
        public string? WeaveTypeCode { get; init; }
        public string? WeaveTypeName { get; init; }
        public string? ProductTypeCode { get; init; }
        public string? ProductTypeName { get; init; }
        public string? Gender { get; init; }
        public short? GarmentLeadTimeDays { get; init; }
        public string? BusinessUnitCode { get; init; }
        public string? BusinessUnitName { get; init; }
        public string? SketchUrl { get; init; }
        public string? ImageUrl { get; init; }
        public DateTime? SourceCreatedUtc { get; init; }
        public int? ImportBatchId { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
        public byte[] RowVer { get; init; } = [];
    }

    private sealed class ColorwayRow
    {
        public int ColorwayId { get; init; }
        public short SortOrder { get; init; }
        public string ColorwayCode { get; init; } = "";
        public string? ColorwayName { get; init; }
        public string Status { get; init; } = "";
        public string? ImageUrl { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
        public byte[] RowVer { get; init; } = [];
    }

    private sealed class LineRow
    {
        public int BomLineId { get; init; }
        public int MaterialId { get; init; }
        public int LineSeq { get; init; }
        public int? PartNo { get; init; }
        public string MaterialCode { get; init; } = "";
        public string? MaterialDescription { get; init; }
        public string? MaterialTypeCode { get; init; }
        public string? MaterialTypeName { get; init; }
        public string? ContentClassCode { get; init; }
        public string? ContentClassName { get; init; }
        public string? NominatedSupplierCode { get; init; }
        public string? NominatedSupplierName { get; init; }
        public string? SupplierCode { get; init; }
        public string? SupplierName { get; init; }
        public decimal? LcoConsumption { get; init; }
        public decimal? BrandConsumption { get; init; }
        public string? UomCode { get; init; }
        public string? ImageUrl { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
        public byte[] RowVer { get; init; } = [];
    }

    private sealed class LineColorRow
    {
        public int BomLineId { get; init; }
        public int ColorwayId { get; init; }
        public string? MaterialColorCode { get; init; }
        public string? MaterialColorDescription { get; init; }
    }

    private sealed class FamilyRow
    {
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string? ModelName { get; init; }
        public string? ImageUrl { get; init; }
        public int? SourceStyleId { get; init; }
        public string? Relation { get; init; }
        public string? Suffix { get; init; }
        public string? LinkSource { get; init; }
        public string? Note { get; init; }
        public string? LinkedBy { get; init; }
        public bool IsCurrent { get; init; }
    }
}
