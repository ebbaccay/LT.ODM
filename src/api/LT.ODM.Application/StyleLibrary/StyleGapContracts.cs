namespace LT.ODM.Application.StyleLibrary;

// ----- Fill the gaps (/api/v1/style-gaps, db/procedures/style.gaps.procedures.sql) -----

/// <summary>The fields Fill the gaps suggests values for, and the confidence levels (3 High, 2 Medium, 1 Check: clues disagree).</summary>
public static class GapFields
{
    public const string Gender = "gender";
    public const string ProductType = "productType";
    public static readonly string[] All = [Gender, ProductType];
    public static readonly string[] Genders = ["MALE", "FEMALE", "UNISEX", "KIDS"];

    /// <summary>Garment categories (style.fn_GarmentCategory); the AI answers with one, the database turns it into the customer's product type.</summary>
    public static readonly string[] Categories =
        ["JACKET", "VEST", "HOODIE", "PULLOVER", "POLO", "TEE", "SHIRT", "TANK", "TOP", "PANTS", "SHORTS", "TIGHTS", "SKIRT", "DRESS", "BRA",
         "UNDERWEAR", "SLEEPWEAR", "SET", "ACCESSORY"];

    /// <summary>The field as written (any case), or null when it is not one of <see cref="All"/>.</summary>
    public static string? Parse(string? field) => All.FirstOrDefault(f => string.Equals(f, field?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// What the library is missing. The *Suggested / *High counts are filled only for the Fill the gaps page (null on the home
/// dashboard, where working them out would slow the page). LeadTimeMissing counts 0 or empty lead times.
/// </summary>
public sealed record StyleGapSummaryDto(
    int Styles, int GenderMissing, int? GenderSuggested, int? GenderHigh, int ProductTypeMissing, int? ProductTypeSuggested, int? ProductTypeHigh,
    int SeasonsToMerge, int StylesInSeasonsToMerge, int LeadTimeMissing, int LcoMissing, int LcoFromBrand, int LcoEstimates, int SupplierMissing);

/// <summary>Show: suggested | none | all. MinConfidence 1-3.</summary>
public sealed record GapListQuery(string Field, string? Customer, string? Season, string Show, int MinConfidence, int Skip, int Take);

/// <summary>
/// A style missing the field, with the suggestion (Value null: none). Value is the gender or product type code; ValueName its
/// name; Category the garment read from the description (product type). Source: Rule or AI. Confidence 0 = no suggestion.
/// </summary>
public sealed record GapItemDto(
    int StyleId, string CustomerCode, string Account, string SeasonCode, string StyleNo, string? Description, string? ModelName,
    string? WeaveTypeCode, string? Gender, string? ProductTypeName, string? ImageUrl, string? SketchUrl,
    string? Value, string? ValueName, string? Category, int Confidence, string? Reason, string? Source);

/// <summary>Counts: styles in the scope by confidence ("3", "2", "1", "0" = no suggestion).</summary>
public sealed record GapPageDto(IReadOnlyList<GapItemDto> Items, int Total, IReadOnlyDictionary<string, int> Counts);

public sealed record GapValueDto(int StyleId, string Value);

/// <summary>Items: the suggestions to accept (or values chosen by hand). Without items: every suggestion in the scope with at least MinConfidence.</summary>
public sealed record AcceptGapsRequest(string? Field, IReadOnlyList<GapValueDto>? Items, string? Customer, string? Season, int? MinConfidence);

public sealed record DismissGapsRequest(string? Field, IReadOnlyList<GapValueDto>? Items);

public sealed record AskAiGapsRequest(string? Field, string? Customer, string? Season);

/// <summary>Saved: answers stored as suggestions; Unclear: styles the AI could not read (not asked again); Remaining: still to ask.</summary>
public sealed record AskAiGapsResultDto(int Saved, int Unclear, int Remaining);

/// <summary>A style the AI is asked about (description and model name only: no customer, no costs).</summary>
public sealed record GapStyleToAsk(int StyleId, string StyleNo, string? Description, string? ModelName, string? WeaveTypeCode, string? Gender,
    string? ProductTypeName, int Remaining);

/// <summary>The AI's answer for one style; Value "-" = could not tell.</summary>
public sealed record GapAiAnswer(int StyleId, string Value, string? Reason);

/// <summary>A season not named year-term; Suggested is the year-term season its name shows (null for quarters and other names).</summary>
public sealed record GapSeasonDto(string SeasonCode, int? SeasonYear, string? Term, string? Suggested, int Styles, int Conflicts, bool SuggestedExists);

public sealed record MergeSeasonRequest(string? From, string? To);

public sealed record MergeSeasonResultDto(int Moved, int Conflicts);

// ----- LCO consumption -----

/// <summary>Source: Brand, BrandAllowance (brand x (1 + allowance)), SameProductType or SameMaterial (median of Peers styles); null = nothing to go by.</summary>
public sealed record LcoLineDto(
    int BomLineId, int LineSeq, string MaterialCode, string? MaterialDescription, string? ContentClassCode, string? UomCode,
    decimal? BrandConsumption, decimal? ConsumptionAllowance, decimal? Suggested, string? Source, int? Peers);

public sealed record LcoGroupDto(string CustomerCode, string Account, string SeasonCode, int Lines, int Suggested, int Styles);

/// <summary>Which lines to fill: BomLineIds, a style, a customer and/or season, or All (the whole library).</summary>
public sealed record ApplyLcoRequest(IReadOnlyList<int>? BomLineIds, int? StyleId, string? Customer, string? Season, bool All, bool WithAllowance);

public sealed record ApplyLcoResultDto(int Updated, int Styles);

// ----- Similar styles (/api/v1/styles/{id}/similar) -----

/// <summary>
/// A style like the one opened. Score 0-100 (shared materials 65, same garment 20, weave 10, gender 5). SameBrand = same customer
/// or brand; other brands are know-how only and never pitched. MainFabric: its fabric line with the largest consumption.
/// </summary>
public sealed record SimilarStyleDto(
    int StyleId, string CustomerCode, string CustomerName, string SeasonCode, string StyleNo, string? Description, string? ModelName,
    string? ProductTypeName, string? Gender, string? WeaveTypeCode, string? ImageUrl, string? SketchUrl, bool IsActive, bool SameBrand,
    bool SameGarment, int SharedMaterials, int SharedFabrics, int Score, string? MainFabric);
