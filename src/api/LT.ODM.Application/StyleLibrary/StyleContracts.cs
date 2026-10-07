using System.Text.RegularExpressions;
using LT.ODM.Application.ConceptStudio;

namespace LT.ODM.Application.StyleLibrary;

/// <summary>Who may use the Styles screens (/api/v1/styles). Factory users see styles through Garment Quotation instead.</summary>
public static class StyleRoles
{
    public const string Readers = "Admin,Merchandiser,Costing,Viewer";
    public const string Editors = "Admin,Merchandiser";
}

/// <summary>A rule the database refused (duplicate number, changed by someone else, ...). StatusCode is 400, 404 or 409.</summary>
public sealed class StyleRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Sketches and photos of styles, colorways and BOM lines (Files:Root/style-library).</summary>
public interface IStyleImageStore : IImageStore;

public sealed record LookupItem(string Code, string Name);

public sealed record StyleLookupsDto(
    IReadOnlyList<LookupItem> Customers,
    IReadOnlyList<LookupItem> Seasons,
    IReadOnlyList<LookupItem> BusinessUnits,
    IReadOnlyList<LookupItem> ProductTypes,
    IReadOnlyList<LookupItem> WeaveTypes,
    IReadOnlyList<LookupItem> MaterialTypes,
    IReadOnlyList<LookupItem> ContentClasses,
    IReadOnlyList<LookupItem> Uoms,
    IReadOnlyList<LookupItem> Suppliers,
    IReadOnlyList<LookupItem> SeasonTerms);

/// <summary>
/// Season and ProductType may hold several codes separated by commas (AI Studio search: "jackets" covers JACKET, JACKETS, JACKETMDW).
/// Material matches BOM material codes and descriptions.
/// </summary>
public sealed record StyleListQuery(
    string? Search, string? Customer, string? Season, string? BusinessUnit, string? ProductType, string? WeaveType, string? Gender,
    int Skip, int Take, string? Material = null);

public sealed record StyleListItemDto(
    int StyleId, string StyleNo, string BaseStyleNo, string? Description, string? ModelCode, string? ModelName,
    string CustomerCode, string CustomerName, string SeasonCode, string? BusinessUnitCode, string? BusinessUnitName,
    string? ProductTypeCode, string? ProductTypeName, string? WeaveTypeCode, string? Gender, string? ImageUrl, string? SketchUrl,
    int ColorwayCount, int BomLineCount, bool HasHistory, DateTime LastChangedUtc);

public sealed record StyleHeaderDto(
    int StyleId, string CustomerCode, string CustomerName, string SeasonCode, string StyleNo, string BaseStyleNo, string? Description,
    string? ModelCode, string? ModelName, string? WeaveTypeCode, string? WeaveTypeName, string? ProductTypeCode, string? ProductTypeName,
    string? Gender, short? GarmentLeadTimeDays, string? BusinessUnitCode, string? BusinessUnitName, string? SketchUrl, string? ImageUrl,
    DateTime? SourceCreatedUtc, int? ImportBatchId, string CreatedBy, DateTime CreatedUtc, string? UpdatedBy, DateTime? UpdatedUtc, byte[] RowVer);

public sealed record ColorwayDto(
    int ColorwayId, short SortOrder, string ColorwayCode, string? ColorwayName, string Status, string? ImageUrl,
    string CreatedBy, DateTime CreatedUtc, string? UpdatedBy, DateTime? UpdatedUtc, byte[] RowVer);

public sealed record BomLineColorwayDto(int ColorwayId, string? MaterialColorCode, string? MaterialColorDescription);

public sealed record BomLineDto(
    int BomLineId, int LineSeq, int? PartNo, string MaterialCode, string? MaterialDescription, string? MaterialTypeCode, string? MaterialTypeName,
    string? ContentClassCode, string? ContentClassName, string? NominatedSupplierCode, string? NominatedSupplierName,
    string? SupplierCode, string? SupplierName, decimal? LcoConsumption, decimal? BrandConsumption, string? UomCode, string? ImageUrl,
    IReadOnlyList<BomLineColorwayDto> Colorways,
    string CreatedBy, DateTime CreatedUtc, string? UpdatedBy, DateTime? UpdatedUtc, byte[] RowVer, int MaterialId = 0);

/// <summary>A style in the same family (same base number + model, or linked by hand), with the link to the style it came from.</summary>
public sealed record StyleFamilyMemberDto(
    int StyleId, string StyleNo, string SeasonCode, string? ModelName, string? ImageUrl,
    int? SourceStyleId, string? Relation, string? Suffix, string? LinkSource, string? Note, string? LinkedBy, bool IsCurrent);

public sealed record StyleDetailDto(
    StyleHeaderDto Style, IReadOnlyList<ColorwayDto> Colorways, IReadOnlyList<BomLineDto> BomLines, IReadOnlyList<StyleFamilyMemberDto> Family);

/// <summary>Landing page: what is in the library, where it is concentrated, and what changed last.</summary>
public sealed record StyleDashboardDto(
    StyleDashboardTotalsDto Totals,
    IReadOnlyList<DashboardSeasonDto> Seasons,
    IReadOnlyList<DashboardCountDto> Customers,
    IReadOnlyList<DashboardCountDto> MaterialTypes,
    IReadOnlyList<DashboardMaterialDto> Materials,
    IReadOnlyList<DashboardCountDto> Suppliers,
    IReadOnlyList<DashboardCountDto> ProductTypes,
    IReadOnlyList<DashboardRecentStyleDto> RecentStyles,
    DashboardImportDto? LastImport);

public sealed record StyleDashboardTotalsDto(
    int Styles, int Colorways, int ColorwaysInRange, int ColorwaysWithBom, int BomLines, int Materials, int Suppliers, int Customers,
    int Seasons, int Families, int ReusedStyles, int StylesWithImage, int StylesWithBom, int StylesWithColorways);

public sealed record DashboardSeasonDto(string SeasonCode, int Styles, int Colorways, int BomLines);

/// <summary>A customer, supplier, material type or product type with its count (Styles; Colorways or BomLines when they apply).</summary>
public sealed record DashboardCountDto(string Code, string Name, int Styles, int? Colorways, int? BomLines);

public sealed record DashboardMaterialDto(string MaterialCode, string? Description, string? MaterialTypeName, int Styles, int BomLines);

public sealed record DashboardRecentStyleDto(
    int StyleId, string StyleNo, string? ModelName, string CustomerCode, string SeasonCode, string? ImageUrl, string? SketchUrl,
    int ColorwayCount, int BomLineCount, string LastChangedBy, DateTime LastChangedUtc);

public sealed record DashboardImportDto(int BatchId, string FileName, string? FinishedBy, DateTime? FinishedUtc);

/// <summary>New style when RowVer is null; otherwise the RowVer read with the style.</summary>
public sealed record SaveStyleRequest(
    byte[]? RowVer, string CustomerCode, string SeasonCode, string StyleNo, string? Description, string? ModelCode, string? ModelName,
    string? WeaveTypeCode, string? ProductTypeCode, string? Gender, short? GarmentLeadTimeDays, string? BusinessUnitCode,
    string? SketchUrl, string? ImageUrl);

public sealed record CopyStyleRequest(string SeasonCode, string StyleNo);

public sealed record SaveColorwayRequest(byte[]? RowVer, string ColorwayCode, string? ColorwayName, string? Status, short? SortOrder, string? ImageUrl);

public sealed record SaveBomLineRequest(
    byte[]? RowVer, int? PartNo, string MaterialCode, string? MaterialDescription, string? MaterialTypeCode, string? ContentClassCode,
    string? NominatedSupplierCode, string? NominatedSupplierName, string? SupplierCode, string? SupplierName,
    decimal? LcoConsumption, decimal? BrandConsumption, string? UomCode, string? ImageUrl, IReadOnlyList<BomLineColorwayDto>? Colorways);

public sealed record SetStyleHistoryRequest(int SourceStyleId, string Relation, string? Note);

/// <summary>The same limits as the style.* tables, checked before the database so users get field-level messages.</summary>
public static partial class StyleValidation
{
    public static readonly string[] Genders = ["MALE", "FEMALE", "UNISEX", "KIDS"];
    public static readonly string[] Statuses = ["INRANGE", "DROPPED"];
    public static readonly string[] Relations = ["CarryOver", "Variant"];

    [GeneratedRegex(@"^[0-9]{4}-[A-Z]{2,4}$")]
    private static partial Regex SeasonRegex();

    /// <summary>Images uploaded here (/api/v1/styles/images/&lt;32 hex&gt;.ext) or an https:// address (e.g. from the import).</summary>
    [GeneratedRegex(@"^(/api/v1/styles/images/[0-9a-f]{32}\.(jpg|png|gif|webp)|https://\S+)$")]
    private static partial Regex ImageUrlRegex();

    public static Dictionary<string, string[]> Style(SaveStyleRequest r)
    {
        var e = new Errors();
        e.Required("customerCode", r.CustomerCode, 32, "Enter the customer code.");
        e.Required("styleNo", r.StyleNo, 40, "Enter the style number.");
        if (string.IsNullOrWhiteSpace(r.SeasonCode) || !SeasonRegex().IsMatch(r.SeasonCode.Trim()))
            e.Add("seasonCode", "Enter the season as year-term, e.g. 2027-SS.");
        e.Max("description", r.Description, 400);
        e.Max("modelCode", r.ModelCode, 60);
        e.Max("modelName", r.ModelName, 100);
        e.Max("weaveTypeCode", r.WeaveTypeCode, 8);
        e.Max("productTypeCode", r.ProductTypeCode, 40);
        e.Max("businessUnitCode", r.BusinessUnitCode, 16);
        if (!string.IsNullOrWhiteSpace(r.Gender) && !Genders.Contains(r.Gender.Trim().ToUpperInvariant()))
            e.Add("gender", "Choose Male, Female, Unisex or Kids.");
        if (r.GarmentLeadTimeDays is < 0 or > 730) e.Add("garmentLeadTimeDays", "Lead time must be between 0 and 730 days.");
        e.Image("sketchUrl", r.SketchUrl);
        e.Image("imageUrl", r.ImageUrl);
        return e.Result;
    }

    public static Dictionary<string, string[]> Copy(CopyStyleRequest r)
    {
        var e = new Errors();
        e.Required("styleNo", r.StyleNo, 40, "Enter the new style number.");
        if (string.IsNullOrWhiteSpace(r.SeasonCode) || !SeasonRegex().IsMatch(r.SeasonCode.Trim()))
            e.Add("seasonCode", "Enter the season as year-term, e.g. 2027-SS.");
        return e.Result;
    }

    public static Dictionary<string, string[]> Colorway(SaveColorwayRequest r)
    {
        var e = new Errors();
        e.Required("colorwayCode", r.ColorwayCode, 20, "Enter the colorway (article) code.");
        e.Max("colorwayName", r.ColorwayName, 150);
        if (!string.IsNullOrWhiteSpace(r.Status) && !Statuses.Contains(r.Status.Trim().ToUpperInvariant()))
            e.Add("status", "Choose In range or Dropped.");
        if (r.SortOrder is < 0) e.Add("sortOrder", "Order must be 0 or more.");
        e.Image("imageUrl", r.ImageUrl);
        return e.Result;
    }

    public static Dictionary<string, string[]> BomLine(SaveBomLineRequest r)
    {
        var e = new Errors();
        e.Required("materialCode", r.MaterialCode, 64, "Enter the material code.");
        e.Max("materialDescription", r.MaterialDescription, 4000);
        e.Max("materialTypeCode", r.MaterialTypeCode, 16);
        e.Max("contentClassCode", r.ContentClassCode, 8);
        e.Max("nominatedSupplierCode", r.NominatedSupplierCode, 32);
        e.Max("nominatedSupplierName", r.NominatedSupplierName, 100);
        e.Max("supplierCode", r.SupplierCode, 32);
        e.Max("supplierName", r.SupplierName, 150);
        e.Max("uomCode", r.UomCode, 8);
        if (r.PartNo is < 0) e.Add("partNo", "Part number must be 0 or more.");
        if (r.LcoConsumption is < 0 or >= 1_000_000_000_000m) e.Add("lcoConsumption", "Consumption must be 0 or more.");
        if (r.BrandConsumption is < 0 or >= 1_000_000_000_000m) e.Add("brandConsumption", "Consumption must be 0 or more.");
        foreach (var c in r.Colorways ?? [])
        {
            if ((c.MaterialColorCode?.Length ?? 0) > 80) e.Add("colorways", "Material colour codes are at most 80 characters.");
            if ((c.MaterialColorDescription?.Length ?? 0) > 150) e.Add("colorways", "Material colour descriptions are at most 150 characters.");
        }
        if (r.Colorways is { } list && list.Select(c => c.ColorwayId).Distinct().Count() != list.Count)
            e.Add("colorways", "A colorway is listed twice.");
        e.Image("imageUrl", r.ImageUrl);
        return e.Result;
    }

    public static Dictionary<string, string[]> History(SetStyleHistoryRequest r)
    {
        var e = new Errors();
        if (r.SourceStyleId <= 0) e.Add("sourceStyleId", "Choose the style this one was reused from.");
        if (!Relations.Contains(r.Relation)) e.Add("relation", "Choose Carry-over or Variant.");
        e.Max("note", r.Note, 400);
        return e.Result;
    }

    /// <summary>Trims text and turns blanks into null, so the database stores one kind of "empty".</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class Errors
    {
        public Dictionary<string, string[]> Result { get; } = [];

        public void Add(string field, string message)
            => Result[field] = Result.TryGetValue(field, out var existing) ? existing.Contains(message) ? existing : [.. existing, message] : [message];

        public void Required(string field, string? value, int max, string message)
        {
            if (string.IsNullOrWhiteSpace(value)) Add(field, message);
            else Max(field, value, max);
        }

        public void Max(string field, string? value, int max)
        {
            if (value?.Trim().Length > max) Add(field, $"At most {max} characters.");
        }

        public void Image(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && (value.Length > 500 || !ImageUrlRegex().IsMatch(value.Trim())))
                Add(field, "Upload the image again.");
        }
    }
}
