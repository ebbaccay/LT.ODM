namespace LT.ODM.Application.StyleLibrary;

// Materials (/api/v1/materials): the BOM seen by material - where each material is used, how much, from whom, in which colours.
// Same readers as Styles (mat.procedures.sql).

public sealed record MaterialListQuery(
    string? Search, string? ContentClass, string? MaterialType, string? Supplier, string? Customer, string? Season, string Sort, int Skip, int Take);

/// <summary>Counts cover the styles matching the list's customer / season filters.</summary>
public sealed record MaterialListItemDto(
    int MaterialId, string MaterialCode, string? Description, string? MaterialTypeCode, string? MaterialTypeName, string? ContentClassCode,
    string? ContentClassName, int Styles, int Lines, int Seasons, int Customers, int Suppliers, string? TopSupplierName, string? LatestSeason);

public sealed record MaterialHeaderDto(
    int MaterialId, string MaterialCode, string? Description, string? MaterialTypeCode, string? MaterialTypeName, string? ContentClassCode,
    string? ContentClassName, int Styles, int Lines, int Customers, int Seasons, string CreatedBy, DateTime CreatedUtc, string? UpdatedBy,
    DateTime? UpdatedUtc);

public sealed record MaterialUseDto(
    int BomLineId, int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? ModelName, string? ProductTypeCode,
    string? ProductTypeName, string? Gender, int LineSeq, int? PartNo, decimal? LcoConsumption, decimal? BrandConsumption, string? UomCode,
    string? SupplierName, string? NominatedSupplierName, int Colorways, string? Colours);

/// <summary>Per-style consumption of the material for one product type and unit: spread and middle.</summary>
public sealed record MaterialBenchmarkDto(
    string? ProductTypeCode, string? ProductTypeName, string UomCode, int Styles, decimal MinValue, decimal P25, decimal Median, decimal P75,
    decimal MaxValue);

/// <summary>SupplierCode is empty for lines without an SAP supplier.</summary>
public sealed record MaterialSupplierDto(string SupplierCode, string? SupplierName, int Styles, int Lines);

public sealed record MaterialColourDto(string? MaterialColorCode, string? MaterialColorDescription, int Colorways, int Styles);

/// <summary>Reason: SameDescription or SameBaseCode (same code before the "_" version suffix).</summary>
public sealed record SimilarMaterialDto(int MaterialId, string MaterialCode, string? Description, string Reason, int Styles);

/// <summary>Uses holds at most MaterialRules.MaxUses rows (newest season first); Header.Lines counts them all.</summary>
public sealed record MaterialDetailDto(
    MaterialHeaderDto Header, IReadOnlyList<MaterialUseDto> Uses, IReadOnlyList<MaterialBenchmarkDto> Benchmarks,
    IReadOnlyList<MaterialSupplierDto> Suppliers, IReadOnlyList<MaterialColourDto> Colours, IReadOnlyList<SimilarMaterialDto> Similar);

// ----- Insights (views on the Materials page) -----

/// <summary>Customer / season / section narrow every view that takes them; ProductType is for standard trims.</summary>
public sealed record MaterialInsightQuery(string? Customer, string? Season, string? ContentClass, string? ProductType);

/// <summary>How many different materials do one job (e.g. 51 zippers on 83 jackets) and how many are used on one style only.</summary>
public sealed record TrimTypeDto(string? MaterialTypeCode, string? MaterialTypeName, string? ContentClassCode, int Materials, int OneOffs, int Styles);

/// <summary>Share = styles using it / styles of the product type with a BOM.</summary>
public sealed record StandardTrimDto(
    int MaterialId, string MaterialCode, string? Description, string? MaterialTypeCode, string? MaterialTypeName, string? ContentClassCode,
    int Styles, decimal Share, string? UomCode, decimal? Median);

public sealed record StandardTrimsDto(int StylesWithBom, IReadOnlyList<TrimTypeDto> Types, IReadOnlyList<StandardTrimDto> Materials);

public sealed record SupplierTotalsDto(int Styles, int Materials, int Suppliers, int SingleSource, int MultiSource, int LinesWithoutSupplier, int Lines);

/// <summary>Share = styles with this supplier / styles in scope. OnlySource = materials no other supplier provides.</summary>
public sealed record SupplierShareDto(string SupplierCode, string? SupplierName, int Styles, decimal Share, int Lines, int Materials, int OnlySource);

public sealed record SupplierConcentrationDto(SupplierTotalsDto Totals, IReadOnlyList<SupplierShareDto> Suppliers);

public sealed record DuplicateMemberDto(int MaterialId, string MaterialCode, string? Description, string? ContentClassCode, int Styles);

public sealed record DuplicateGroupDto(int GroupNo, int Styles, IReadOnlyList<DuplicateMemberDto> Members);

/// <summary>Groups holds the first MaterialRules.MaxDuplicateGroups groups; TotalGroups / TotalMaterials count them all.</summary>
public sealed record DuplicatesDto(int TotalGroups, int TotalMaterials, IReadOnlyList<DuplicateGroupDto> Groups);

/// <summary>Key is a season code or a product type code (null = no product type).</summary>
public sealed record RecycledRowDto(string? Key, string? Name, int Styles, int AllRecycled, int SomeRecycled, int FabricLines, int RecycledLines);

public sealed record RecycledShareDto(IReadOnlyList<RecycledRowDto> Seasons, IReadOnlyList<RecycledRowDto> ProductTypes);

public sealed record ColourUsageDto(string Colour, int Codes, int Colorways, int Styles, int Materials, int Seasons);

public static class MaterialRules
{
    public const int MaxUses = 1000;
    public const int MaxDuplicateGroups = 200;
    public static readonly string[] Sorts = ["used", "code"];
}

public interface IMaterialRepository
{
    Task<PagedResult<MaterialListItemDto>> ListAsync(MaterialListQuery query, CancellationToken ct = default);
    Task<MaterialDetailDto?> GetAsync(int materialId, CancellationToken ct = default);

    Task<StandardTrimsDto> StandardTrimsAsync(MaterialInsightQuery query, CancellationToken ct = default);
    Task<SupplierConcentrationDto> SuppliersAsync(MaterialInsightQuery query, CancellationToken ct = default);
    Task<DuplicatesDto> DuplicatesAsync(MaterialInsightQuery query, CancellationToken ct = default);
    Task<RecycledShareDto> RecycledAsync(MaterialInsightQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ColourUsageDto>> ColoursAsync(MaterialInsightQuery query, CancellationToken ct = default);
}
