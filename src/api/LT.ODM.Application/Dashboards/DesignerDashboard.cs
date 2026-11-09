namespace LT.ODM.Application.Dashboards;

/// <summary>A designer's styles and what each still misses (style.usp_Dashboard_Designer).</summary>
public interface IDesignerDashboardRepository
{
    /// <param name="userName">The designer: active styles of their business unit team plus the ones they created.</param>
    /// <param name="season">One season; null = all.</param>
    Task<DesignerDashboardData> GetAsync(string userName, string? season, CancellationToken ct = default);
}

/// <summary>What the procedure returns; <see cref="DesignerDashboard.Build"/> turns it into the page's figures.</summary>
public sealed record DesignerDashboardData(
    IReadOnlyList<DesignerSeasonDto> Seasons,
    IReadOnlyList<DesignerStyleFacts> Styles,
    IReadOnlyList<DesignerConceptDto> Concepts,
    IReadOnlyList<DesignerPitchDto> Pitches,
    IReadOnlyList<string>? TeamBusinessUnits = null);

/// <summary>One style with the counts the checks need. Colorways = colorways not dropped.</summary>
public sealed record DesignerStyleFacts(
    int StyleId, string StyleNo, string? Description, string? ModelName, string CustomerCode, string SeasonCode,
    string? ImageUrl, string? SketchUrl, string? ProductTypeCode, string? Gender, int? ConceptId,
    int Colorways, int ColorwaysNotOnBom, int BomLines, int LinesNoColour, int LinesNoDetails, int LinesNoLco, int LinesNoSupplier,
    int Packages, string? Outcome, DateTime CreatedUtc, DateTime LastChangedUtc,
    string? BusinessUnitCode = null, string? CreatedBy = null, string? LastChangedBy = null);

public sealed record DesignerSeasonDto(string SeasonCode, int Styles);

public sealed record DesignerConceptDto(int ConceptId, string Name, string? CustomerCode, string? SeasonCode, string Status, int Styles, DateTime LastChangedUtc);

/// <summary>A designer's style in a package. Outcome: Sampling, Ordered, Dropped or null (no answer yet).</summary>
public sealed record DesignerPitchDto(
    int PackageId, string PackageName, string PackageStatus, string? CustomerCode, int StyleId, string StyleNo, string? Outcome, DateTime AtUtc);

/// <summary>Check: a <see cref="DesignerChecks"/> key. Count: colorways or BOM lines affected (null = the whole style).</summary>
public sealed record DesignerGapDto(string Check, int? Count);

/// <summary>Score: share of the checks passed (0-100, rounded down, so 100 = nothing missing).</summary>
public sealed record DesignerStyleDto(
    int StyleId, string StyleNo, string? Description, string? ModelName, string CustomerCode, string SeasonCode,
    string? ImageUrl, string? SketchUrl, int? ConceptId, int Colorways, int BomLines, int Packages, string? Outcome,
    int Score, IReadOnlyList<DesignerGapDto> Missing, DateTime CreatedUtc, DateTime LastChangedUtc,
    string? BusinessUnitCode = null, string? CreatedBy = null, string? LastChangedBy = null);

/// <summary>Styles: how many styles miss the check (styles where it is blocked by a missing BOM or colorways are not counted).</summary>
public sealed record DesignerCheckCountDto(string Check, int Styles);

/// <summary>AverageScore 0-100. InPackages / Sampling / Ordered: styles (furthest outcome per style).</summary>
public sealed record DesignerTotalsDto(int Styles, int Complete, int AverageScore, int Colorways, int BomLines, int InPackages, int Sampling, int Ordered);

/// <summary>TeamBusinessUnits: the designer's business units (empty = only styles they created are listed).</summary>
public sealed record DesignerDashboardDto(
    string? Season,
    IReadOnlyList<DesignerSeasonDto> Seasons,
    DesignerTotalsDto Totals,
    IReadOnlyList<DesignerCheckCountDto> Checks,
    IReadOnlyList<DesignerStyleDto> Styles,
    IReadOnlyList<DesignerConceptDto> Concepts,
    IReadOnlyList<DesignerPitchDto> Pitches,
    IReadOnlyList<string>? TeamBusinessUnits = null);

/// <summary>
/// What a style needs before it is complete, in the order a designer usually works. Every check counts towards the score;
/// a check that needs a BOM (or colorways) the style does not have yet fails without being listed, so the list names
/// what to do next (add the BOM) rather than everything that follows from it.
/// </summary>
public static class DesignerChecks
{
    public const string Picture = "picture";             // a sketch or a photo
    public const string ProductType = "productType";
    public const string Gender = "gender";
    public const string Colorways = "colorways";         // at least one colorway not dropped
    public const string Bom = "bom";                     // at least one BOM line
    public const string ColorwaysOnBom = "colorwaysOnBom";   // every colorway has colours on the BOM
    public const string BomColours = "bomColours";       // fabric and trim lines have a colour for every colorway
    public const string BomDetails = "bomDetails";       // lines have a UOM, material type and content class
    public const string Consumption = "consumption";     // lines have an LCO consumption
    public const string Suppliers = "suppliers";         // fabric, trim and accessory lines have a supplier

    public static readonly string[] All =
        [Picture, ProductType, Gender, Colorways, Bom, ColorwaysOnBom, BomColours, BomDetails, Consumption, Suppliers];

    /// <summary>The checks the style misses (listed) and its score.</summary>
    public static (int Score, IReadOnlyList<DesignerGapDto> Missing) Evaluate(DesignerStyleFacts s)
    {
        var missing = new List<DesignerGapDto>();
        var passed = 0;
        void Check(string key, bool ok, bool blocked = false, int? count = null)
        {
            if (ok) passed++;
            else if (!blocked) missing.Add(new DesignerGapDto(key, count));
        }

        var hasBom = s.BomLines > 0;
        var hasColorways = s.Colorways > 0;
        Check(Picture, s.ImageUrl is not null || s.SketchUrl is not null);
        Check(ProductType, !string.IsNullOrEmpty(s.ProductTypeCode));
        Check(Gender, !string.IsNullOrEmpty(s.Gender));
        Check(Colorways, hasColorways);
        Check(Bom, hasBom);
        Check(ColorwaysOnBom, hasBom && hasColorways && s.ColorwaysNotOnBom == 0, !hasBom || !hasColorways, s.ColorwaysNotOnBom);
        Check(BomColours, hasBom && hasColorways && s.LinesNoColour == 0, !hasBom || !hasColorways, s.LinesNoColour);
        Check(BomDetails, hasBom && s.LinesNoDetails == 0, !hasBom, s.LinesNoDetails);
        Check(Consumption, hasBom && s.LinesNoLco == 0, !hasBom, s.LinesNoLco);
        Check(Suppliers, hasBom && s.LinesNoSupplier == 0, !hasBom, s.LinesNoSupplier);
        return (passed * 100 / All.Length, missing);
    }
}

public static class DesignerDashboard
{
    public static DesignerDashboardDto Build(string? season, DesignerDashboardData data)
    {
        var styles = data.Styles.Select(s =>
        {
            var (score, missing) = DesignerChecks.Evaluate(s);
            return new DesignerStyleDto(s.StyleId, s.StyleNo, s.Description, s.ModelName, s.CustomerCode, s.SeasonCode, s.ImageUrl, s.SketchUrl,
                s.ConceptId, s.Colorways, s.BomLines, s.Packages, s.Outcome, score, missing, s.CreatedUtc, s.LastChangedUtc,
                s.BusinessUnitCode, s.CreatedBy, s.LastChangedBy);
        }).ToList();

        var checks = DesignerChecks.All
            .Select(c => new DesignerCheckCountDto(c, styles.Count(s => s.Missing.Any(m => m.Check == c))))
            .ToList();
        var totals = new DesignerTotalsDto(
            styles.Count,
            styles.Count(s => s.Score == 100),
            styles.Count == 0 ? 0 : (int)styles.Average(s => s.Score),
            styles.Sum(s => s.Colorways),
            styles.Sum(s => s.BomLines),
            styles.Count(s => s.Packages > 0),
            styles.Count(s => s.Outcome == "Sampling"),
            styles.Count(s => s.Outcome == "Ordered"));
        return new DesignerDashboardDto(season, data.Seasons, totals, checks, styles, data.Concepts, data.Pitches, data.TeamBusinessUnits ?? []);
    }
}
