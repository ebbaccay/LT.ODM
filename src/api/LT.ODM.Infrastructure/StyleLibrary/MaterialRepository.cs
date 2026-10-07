using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Materials screens: mat.usp_Material_* (db/procedures/mat.procedures.sql).</summary>
public sealed class MaterialRepository(IDbConnectionFactory connectionFactory) : IMaterialRepository
{
    public async Task<PagedResult<MaterialListItemDto>> ListAsync(MaterialListQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<ListRow>(Proc("mat.usp_Material_List", new
        {
            q.Search, ContentClassCode = q.ContentClass, MaterialTypeCode = q.MaterialType, SupplierCode = q.Supplier, CustomerCode = q.Customer,
            SeasonCode = q.Season, q.Sort, q.Skip, q.Take,
        }, ct))).ToList();
        return new PagedResult<MaterialListItemDto>(rows.Select(r => new MaterialListItemDto(r.MaterialId, r.MaterialCode, r.Description,
            r.MaterialTypeCode, r.MaterialTypeName, r.ContentClassCode, r.ContentClassName, r.Styles, r.Lines, r.Seasons, r.Customers, r.Suppliers,
            r.TopSupplierName, r.LatestSeason)).ToList(), rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<MaterialDetailDto?> GetAsync(int materialId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("mat.usp_Material_Get", new { MaterialId = materialId, MaxRows = MaterialRules.MaxUses }, ct));
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>();
        if (h is null) return null;
        var header = new MaterialHeaderDto(h.MaterialId, h.MaterialCode, h.Description, h.MaterialTypeCode, h.MaterialTypeName, h.ContentClassCode,
            h.ContentClassName, h.Styles, h.Lines, h.Customers, h.Seasons, h.CreatedBy, Utc(h.CreatedUtc), h.UpdatedBy, h.UpdatedUtc is { } u ? Utc(u) : null);
        return new MaterialDetailDto(header,
            (await grid.ReadAsync<MaterialUseDto>()).ToList(),
            (await grid.ReadAsync<MaterialBenchmarkDto>()).ToList(),
            (await grid.ReadAsync<MaterialSupplierDto>()).ToList(),
            (await grid.ReadAsync<MaterialColourDto>()).ToList(),
            (await grid.ReadAsync<SimilarMaterialDto>()).ToList());
    }

    // ----- Insights -----

    public async Task<StandardTrimsDto> StandardTrimsAsync(MaterialInsightQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("mat.usp_Material_StandardTrims", new
        {
            ProductTypeCode = q.ProductType, CustomerCode = q.Customer, SeasonCode = q.Season,
        }, ct));
        var n = await grid.ReadSingleAsync<int>();
        return new StandardTrimsDto(n, (await grid.ReadAsync<TrimTypeDto>()).ToList(), (await grid.ReadAsync<StandardTrimDto>()).ToList());
    }

    public async Task<SupplierConcentrationDto> SuppliersAsync(MaterialInsightQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("mat.usp_Material_Suppliers", new
        {
            CustomerCode = q.Customer, SeasonCode = q.Season, ContentClassCode = q.ContentClass,
        }, ct));
        return new SupplierConcentrationDto(await grid.ReadSingleAsync<SupplierTotalsDto>(), (await grid.ReadAsync<SupplierShareDto>()).ToList());
    }

    public async Task<DuplicatesDto> DuplicatesAsync(MaterialInsightQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("mat.usp_Material_Duplicates", new
        {
            ContentClassCode = q.ContentClass, MaxGroups = MaterialRules.MaxDuplicateGroups,
        }, ct));
        var rows = (await grid.ReadAsync<DuplicateRow>()).ToList();
        var totals = await grid.ReadSingleAsync<DuplicateTotalsRow>();
        var groups = rows.GroupBy(r => r.GroupNo).Select(g => new DuplicateGroupDto(g.Key, g.First().GroupStyles,
            g.Select(r => new DuplicateMemberDto(r.MaterialId, r.MaterialCode, r.Description, r.ContentClassCode, r.Styles)).ToList())).ToList();
        return new DuplicatesDto(totals.Groups, totals.Materials, groups);
    }

    public async Task<RecycledShareDto> RecycledAsync(MaterialInsightQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("mat.usp_Material_Recycled", new { CustomerCode = q.Customer }, ct));
        var seasons = (await grid.ReadAsync<RecycledRow>()).Select(r => r.ToDto(r.SeasonCode, r.SeasonCode)).ToList();
        var types = (await grid.ReadAsync<RecycledRow>()).Select(r => r.ToDto(r.ProductTypeCode, r.ProductTypeName)).ToList();
        return new RecycledShareDto(seasons, types);
    }

    public async Task<IReadOnlyList<ColourUsageDto>> ColoursAsync(MaterialInsightQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ColourUsageDto>(Proc("mat.usp_Material_Colours", new
        {
            CustomerCode = q.Customer, SeasonCode = q.Season, ContentClassCode = q.ContentClass,
        }, ct))).ToList();
    }

    private sealed class DuplicateRow
    {
        public int GroupNo { get; init; }
        public int GroupStyles { get; init; }
        public int MaterialId { get; init; }
        public string MaterialCode { get; init; } = "";
        public string? Description { get; init; }
        public string? ContentClassCode { get; init; }
        public int Styles { get; init; }
    }

    private sealed class DuplicateTotalsRow
    {
        public int Groups { get; init; }
        public int Materials { get; init; }
    }

    private sealed class RecycledRow
    {
        public string? SeasonCode { get; init; }
        public string? ProductTypeCode { get; init; }
        public string? ProductTypeName { get; init; }
        public int Styles { get; init; }
        public int AllRecycled { get; init; }
        public int SomeRecycled { get; init; }
        public int FabricLines { get; init; }
        public int RecycledLines { get; init; }
        public RecycledRowDto ToDto(string? key, string? name) => new(key, name, Styles, AllRecycled, SomeRecycled, FabricLines, RecycledLines);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed class ListRow
    {
        public int MaterialId { get; init; }
        public string MaterialCode { get; init; } = "";
        public string? Description { get; init; }
        public string? MaterialTypeCode { get; init; }
        public string? MaterialTypeName { get; init; }
        public string? ContentClassCode { get; init; }
        public string? ContentClassName { get; init; }
        public int Styles { get; init; }
        public int Lines { get; init; }
        public int Seasons { get; init; }
        public int Customers { get; init; }
        public int Suppliers { get; init; }
        public string? TopSupplierName { get; init; }
        public string? LatestSeason { get; init; }
        public int TotalCount { get; init; }
    }

    private sealed class HeaderRow
    {
        public int MaterialId { get; init; }
        public string MaterialCode { get; init; } = "";
        public string? Description { get; init; }
        public string? MaterialTypeCode { get; init; }
        public string? MaterialTypeName { get; init; }
        public string? ContentClassCode { get; init; }
        public string? ContentClassName { get; init; }
        public int Styles { get; init; }
        public int Lines { get; init; }
        public int Customers { get; init; }
        public int Seasons { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
    }
}
