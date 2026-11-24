using LT.ODM.Application.Costing;

namespace LT.ODM.Application.Abstractions;

/// <summary>Prices per IO: cost.usp_* (db/procedures/cost.procedures.sql).</summary>
public interface ICostingRepository
{
    Task<CostingPageDto> ListAsync(CostingListQuery query, int smallOrderQty, CancellationToken ct = default);

    /// <summary>ShowCost is left false: the caller decides what the user may see.</summary>
    Task<StylePricesDto> GetStyleAsync(int styleId, int smallOrderQty, CancellationToken ct = default);

    Task<CostingImportResultDto> ImportAsync(string fileName, IReadOnlyList<CostingImportRow> rows, string importedBy, CancellationToken ct = default);

    /// <summary>What each style's IO history says it costs (its own, the same style in another season, or the style it was copied from).</summary>
    Task<IReadOnlyList<StylePriceHintDto>> PriceHintsAsync(IReadOnlyList<int> styleIds, int smallOrderQty, CancellationToken ct = default);

    Task<PackageQuotesDto> PackageQuotesAsync(int packageId, int smallOrderQty, CancellationToken ct = default);

    /// <summary>404 as StyleRuleException when the style is not in the package.</summary>
    Task SaveQuoteAsync(int packageId, int packageItemId, decimal fob, string currency, string basisKind, string? basisText, string quotedBy, CancellationToken ct = default);

    Task<IReadOnlyList<QuotedPrice>> QuotedPricesAsync(int packageId, CancellationToken ct = default);

    /// <summary>Quotes outside the client's price band, as client-profile issues (Kind Price).</summary>
    Task<IReadOnlyList<LT.ODM.Application.StyleLibrary.ClientIssueDto>> QuoteIssuesAsync(int packageId, CancellationToken ct = default);

    Task<PlantRatesDto> PlantRatesAsync(bool withDeleted, CancellationToken ct = default);

    Task<IReadOnlyList<PriceGuideRowDto>> PriceGuideAsync(int smallOrderQty, CancellationToken ct = default);

    Task<IReadOnlyList<ConceptCostDto>> MyConceptsAsync(string userName, int smallOrderQty, CancellationToken ct = default);

    Task<CostingOverviewDto> OverviewAsync(int smallOrderQty, decimal netGap, CancellationToken ct = default);

    /// <summary>An estimated FOB for a garment (style.fn_GarmentCategory code) for a client; null when no priced style is of that garment.</summary>
    Task<EstimateDto?> EstimateAsync(string category, string? customerCode, int smallOrderQty, CancellationToken ct = default);

    /// <summary>Adds (rateId null) or changes a rate; returns its id. 404 / 409 as StyleRuleException.</summary>
    Task<int> SavePlantRateAsync(int? rateId, string plantCode, DateOnly effectiveDate, int minOrderQty, string salesTerm, string currency, decimal cost, string changedBy,
        CancellationToken ct = default);

    /// <summary>Marks the rate deleted. 404 as StyleRuleException.</summary>
    Task DeletePlantRateAsync(int rateId, string changedBy, CancellationToken ct = default);
}

/// <summary>Reads the ERP costing export (.xlsx).</summary>
public interface ICostingWorkbookReader
{
    CostingWorkbook Read(Stream xlsx);
}
