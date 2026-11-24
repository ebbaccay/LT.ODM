using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Costing;

/// <summary>
/// Who may see IO prices: FOB for Readers (designers too since 2026-11-11, to design to a style's price); the cost groups, net balance and plant rates
/// only for Admin and Costing. Quotes, and prices sent to clients, stay with QuoteEditors.
/// </summary>
public static class CostingRoles
{
    public const string Readers = "Admin,Merchandiser,Designer,Costing";
    public const string QuoteEditors = "Admin,Merchandiser,Costing";
    public const string CostAdmin = "Admin", CostTeam = "Costing", Merchandiser = "Merchandiser";

    /// <summary>Plant cost per minute: factory cost, so not merchandisers.</summary>
    public const string CostEditors = "Admin,Costing";
}

/// <summary>
/// What a style's IO history says it costs. Source: Own (its IOs), Family (the same style in another season: SourceStyleNo) or Origin (the style it was
/// copied from: SourceStyleNo).
/// </summary>
public sealed record StylePriceHintDto(
    int StyleId, string Source, string SourceStyleNo, string CurrencyCode, decimal LatestFob, decimal MinFob, decimal MaxFob, decimal AvgFob, int Ios, long TotalQty,
    DateTime? LatestCostDate, string? LatestIoNo);

public sealed record PriceHintsRequest(IReadOnlyList<int>? StyleIds);

/// <summary>A version of a style's quote in a package (roadmap 5.19). Basis: for LT only, never shown to the client.</summary>
public sealed record QuoteDto(int Version, decimal Fob, string CurrencyCode, string BasisKind, string? BasisText, string QuotedBy, DateTime QuotedUtc);

/// <summary>
/// An estimated FOB for a garment with no IO prices, from Peers priced styles of it (PeerStyles): fabric, trims and other = their average; making =
/// the plant's cost per minute x their average TC SMV (CpmFromRate false: their average making cost, no rate for the plant); EstimatedFob = the costs
/// / (1 - their average net balance share). The cost parts are null for those who may only see FOB.
/// </summary>
public sealed record EstimateDto(
    string Category, int Peers, string PeerStyles, string CurrencyCode, decimal EstimatedFob, decimal? Smv, string? PlantCode, decimal? Cpm, bool CpmFromRate,
    decimal? Making, decimal? Fabric, decimal? Trims, decimal? Other, decimal? NetShare)
{
    public EstimateDto FobOnly() => this with { Cpm = null, Making = null, Fabric = null, Trims = null, Other = null, NetShare = null };
}

/// <summary>
/// A style in a package: what its IO history suggests (null: no prices anywhere), else an estimate from similar styles of the garment, and its quotes,
/// newest first.
/// </summary>
public sealed record PackageItemQuotesDto(int PackageItemId, int StyleId, StylePriceHintDto? Suggestion, IReadOnlyList<QuoteDto> Quotes, EstimateDto? Estimate = null);

public sealed record ClientBandDto(decimal? PriceMin, decimal? PriceMax, string Currency);

public sealed record PackageQuotesDto(ClientBandDto? Band, IReadOnlyList<PackageItemQuotesDto> Items);

public sealed record SaveQuoteRequest(decimal? Fob, string? Currency, string? BasisKind, string? BasisText);

public sealed record QuotedPrice(int PackageItemId, decimal Fob, string CurrencyCode);

public static class QuoteRules
{
    public const decimal MaxFob = 100_000m;
    public static readonly string[] BasisKinds = ["Own", "Family", "Origin", "Estimate", "Typed"];

    public static Dictionary<string, string[]> Validate(SaveQuoteRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.Fob is not { } fob || fob <= 0 || fob > MaxFob) errors["fob"] = ["Enter a FOB above 0."];
        if (r.Currency?.Trim() is not { Length: 3 } c || !c.All(char.IsAsciiLetter)) errors["currency"] = ["Use a 3-letter currency code (e.g. USD)."];
        if (!BasisKinds.Contains(r.BasisKind)) errors["basisKind"] = ["Unknown basis."];
        if (r.BasisText?.Trim().Length > 300) errors["basisText"] = ["Keep the basis under 300 characters."];
        return errors;
    }

    /// <summary>Puts the current quotes on the package's styles, for a line sheet or share link that shows prices.</summary>
    public static StyleLibrary.PackageDetailDto Apply(StyleLibrary.PackageDetailDto package, IReadOnlyList<QuotedPrice> quotes)
    {
        var byItem = quotes.ToDictionary(q => q.PackageItemId);
        return package with
        {
            Items = package.Items.Select(i => byItem.TryGetValue(i.PackageItemId, out var q) ? i with { QuotedFob = q.Fob, QuotedCurrency = q.CurrencyCode } : i).ToList(),
        };
    }
}

/// <summary>A plant's cost per minute from a date, for orders from MinOrderQty, per sales term and currency. InForce: the rate used today.</summary>
public sealed record PlantRateDto(
    int RateId, string PlantCode, string PlantName, DateTime EffectiveDate, int MinOrderQty, string SalesTerm, string CurrencyCode, decimal Cost, bool IsDeleted,
    bool InForce, string CreatedBy, DateTime CreatedUtc, string? UpdatedBy, DateTime? UpdatedUtc);

public sealed record PlantRatesDto(IReadOnlyList<PlantRateDto> Rates, IReadOnlyList<LookupItem> Plants);

public sealed record SavePlantRateRequest(string? PlantCode, DateOnly? EffectiveDate, int? MinOrderQty, string? SalesTerm, string? Currency, decimal? Cost);

public static class PlantRateRules
{
    public const decimal MaxCost = 1000m;

    public static Dictionary<string, string[]> Validate(SavePlantRateRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.PlantCode) || r.PlantCode.Trim().Length > 20) errors["plantCode"] = ["Choose a plant."];
        if (r.EffectiveDate is null) errors["effectiveDate"] = ["Enter the date the rate applies from."];
        if (r.MinOrderQty is not { } q || q < 0) errors["minOrderQty"] = ["Enter a minimum order quantity of 0 or more."];
        if (r.SalesTerm?.Trim() is not { Length: > 0 and <= 10 } t || !t.All(char.IsAsciiLetter)) errors["salesTerm"] = ["Use letters only, up to 10 (e.g. FOB)."];
        if (r.Currency?.Trim() is not { Length: 3 } c || !c.All(char.IsAsciiLetter)) errors["currency"] = ["Use a 3-letter currency code (e.g. USD)."];
        if (r.Cost is not { } cost || cost <= 0 || cost > MaxCost) errors["cost"] = ["Enter a cost per minute above 0."];
        return errors;
    }
}

/// <summary>Costing section of the configuration.</summary>
public sealed class CostingOptions
{
    public const string SectionName = "Costing";

    /// <summary>IOs below this quantity (samples, one-offs) stay listed but are left out of a style's figures.</summary>
    public int SmallOrderQty { get; set; } = 50;
}

public sealed record CostingListQuery(string? Search, string? Customer, string? Season, int Skip, int Take);

/// <summary>
/// A style's prices over its IOs, in one currency: latest FOB, range, and the averages weighted by order quantity. The cost groups
/// and AvgNet are null for those who may only see FOB. Fabric = main and sub fabric; Trims = trims and local trims; Making = CM and subcon.
/// </summary>
public sealed record StylePriceSummaryDto(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? ModelName, string? ProductTypeName, string? ImageUrl, string? SketchUrl,
    string CurrencyCode, int Ios, long TotalQty, decimal LatestFob, DateTime? LatestCostDate, decimal MinFob, decimal MaxFob, decimal AvgFob,
    decimal? AvgFabric, decimal? AvgTrims, decimal? AvgMaking, decimal? AvgOther, decimal? AvgNet);

/// <summary>
/// One IO with the costing that stands for it (CostType PCOSTING pre-costing, FCOSTING final costing). FinalPending: a final costing exists
/// but its main fabric is not computed yet. IsSmall: below CostingOptions.SmallOrderQty, left out of the figures.
/// </summary>
public sealed record IoPriceDto(
    string IoNo, string? PlantCode, string? PlantName, int? OrderQty, DateTime? LcoDate, DateTime? PlanDeliveryDate, decimal? TcSmv,
    int CostingVersion, string CostType, string CurrencyCode, DateTime? CostDate, decimal Fob,
    decimal? Fabric, decimal? Trims, decimal? Making, decimal? Other, decimal? NetBalance, decimal? PercToFob, bool FinalPending, bool IsSmall);

public sealed record CostingImportDto(int ImportId, string FileName, int RowsRead, int Added, int Updated, int Unmatched, string ImportedBy, DateTime ImportedUtc);

public sealed record CostingPageDto(IReadOnlyList<StylePriceSummaryDto> Items, int Total, CostingImportDto? LastImport, bool ShowCost);

/// <summary>One style's page: a summary per currency (normally one) and its IOs. Unfinished: IOs with no costing that has a FOB yet.</summary>
public sealed record StylePricesDto(IReadOnlyList<StylePriceSummaryDto> Summaries, IReadOnlyList<IoPriceDto> Ios, int Unfinished, bool ShowCost, int SmallOrderQty);

public sealed record UnmatchedStyleDto(string Customer, string Season, string StyleNo, int Rows);

/// <summary>What an upload did. Unmatched styles are not in the library (import them first, then load the file again).</summary>
public sealed record CostingImportResultDto(CostingImportDto Import, IReadOnlyList<UnmatchedStyleDto> UnmatchedStyles, IReadOnlyList<string> Issues);

public sealed record CostLine(string Code, decimal Amount);

/// <summary>One costing version of the ERP export. Money at 5 decimals, as the ERP calculates it.</summary>
public sealed record CostingImportRow(
    string Cust, string Season, string StyleNo, string IoNo, string? PlantCode, string? PlantName, int? OrderQty, DateOnly? LcoDate, DateOnly? PlanDeliveryDate,
    decimal? MerchSmv, decimal? TcSmv, int CostingVersion, string CostType, string? Status, string Currency, DateTime? CostDate, decimal Fob, decimal? NetBalance,
    decimal? PercToFob, IReadOnlyList<CostLine> Lines);

/// <summary>Problems: the file cannot be read at all. Issues: rows left out, or worth a look.</summary>
public sealed record CostingWorkbook(IReadOnlyList<CostingImportRow> Rows, IReadOnlyList<string> Problems, IReadOnlyList<string> Issues);

// ----- Home dashboard panels -----

/// <summary>A garment's prices in one currency: the styles with IO prices and the low, typical (median) and high of their latest FOB and TC SMV.</summary>
public sealed record PriceGuideRowDto(
    string Category, string CategoryName, string CurrencyCode, int Styles, decimal LowFob, decimal TypicalFob, decimal HighFob, decimal? LowSmv, decimal? TypicalSmv, decimal? HighSmv);

/// <summary>
/// One of the user's concepts against its target price. Status: Within, Over, Under (room), NoCompare (target in another currency), NoTarget, NoCategory,
/// NoEstimate (no priced style of that garment). Gap: by how much, in the estimate's currency.
/// </summary>
public sealed record ConceptCostDto(
    int ConceptId, string Name, string? CategoryName, string? CustomerCode, string? TargetPrice, decimal? EstimatedFob, string? CurrencyCode, int Peers, string Status, decimal? Gap);

public sealed record CostingHealthDto(
    DateTime? LastImportUtc, string? LastImportFile, string? LastImportBy, int LastImportUnmatched, int StylesWithPrices, int ActiveStyles, int Ios, int IosUnfinished,
    int FinalPending, int SmallIos, int PlantsWithoutRate, string? PlantsWithoutRateList);

public sealed record NetByGarmentDto(string Category, string CurrencyCode, int Styles, int Ios, long Qty, decimal Value, decimal? NetPct);

public sealed record NetDropDto(int StyleId, string StyleNo, string IoNo, DateTime? CostDate, decimal Fob, string CurrencyCode, decimal IoPct, decimal StylePct);

public sealed record QuoteCountsDto(int NoQuote, int OutOfBand, int Estimated, int Packages);

/// <summary>Kind: NoQuote, OutOfBand (outside the client's price band) or Estimate (quoted from an estimate).</summary>
public sealed record QuoteAttentionDto(int PackageId, string PackageName, string StyleNo, string Kind, decimal? Fob, string? CurrencyCode, decimal? PriceMin, decimal? PriceMax);

public sealed record CostingOverviewDto(
    CostingHealthDto Health, IReadOnlyList<NetByGarmentDto> NetByGarment, IReadOnlyList<NetDropDto> NetDrops, QuoteCountsDto Quotes, IReadOnlyList<QuoteAttentionDto> QuoteItems,
    decimal NetGap);

public static class TargetPrice
{
    /// <summary>The figures of a target price as typed ("USD 12-14 FOB", "14.50"): lowest, highest and currency (3 letters other than FOB), or null.</summary>
    public static (decimal Low, decimal High, string? Currency)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var numbers = System.Text.RegularExpressions.Regex.Matches(text, @"\d+(?:\.\d+)?")
            .Select(m => decimal.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).Where(n => n > 0).ToList();
        if (numbers.Count == 0) return null;
        var currency = System.Text.RegularExpressions.Regex.Matches(text.ToUpperInvariant(), @"\b[A-Z]{3}\b").Select(m => m.Value).FirstOrDefault(v => v != "FOB");
        return (numbers.Min(), numbers.Max(), currency);
    }

    /// <summary>How an estimate stands against the target; the estimate is read to 2 decimals, as clients are quoted.</summary>
    public static (string Status, decimal? Gap) Judge(string? target, string? category, decimal? estimate, string? currency)
    {
        if (category is null) return ("NoCategory", null);
        if (estimate is not { } e) return ("NoEstimate", null);
        if (Parse(target) is not { } t) return ("NoTarget", null);
        if (t.Currency is not null && t.Currency != currency) return ("NoCompare", null);
        var fob = Math.Round(e, 2, MidpointRounding.AwayFromZero);
        return fob > t.High ? ("Over", fob - t.High) : fob < t.Low ? ("Under", t.Low - fob) : ("Within", null);
    }
}
