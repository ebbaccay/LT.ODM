using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Costing;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Costing;

/// <summary>Prices per IO: cost.usp_Costing_List, usp_StylePrices_Get, usp_Import_Save (db/procedures/cost.procedures.sql).</summary>
public sealed class CostingRepository(IDbConnectionFactory connectionFactory) : ICostingRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct, int? timeout = null)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: timeout);

    private static StylePriceSummaryDto Map(SummaryRow r) => new(r.StyleId, r.StyleNo, r.SeasonCode, r.CustomerCode, r.ModelName, r.ProductTypeName, r.ImageUrl, r.SketchUrl,
        r.CurrencyCode.Trim(), r.Ios, r.TotalQty, r.LatestFob, r.LatestCostDate, r.MinFob, r.MaxFob, r.AvgFob, r.AvgFabric, r.AvgTrims, r.AvgMaking, r.AvgOther, r.AvgNet);

    private static CostingImportDto Map(ImportRow r) => new(r.ImportId, r.FileName, r.RowsRead, r.Added, r.Updated, r.Unmatched, r.ImportedBy, DateTime.SpecifyKind(r.ImportedUtc, DateTimeKind.Utc));

    public async Task<CostingPageDto> ListAsync(CostingListQuery q, int smallOrderQty, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("cost.usp_Costing_List",
            new { q.Search, q.Customer, q.Season, SmallQty = smallOrderQty, q.Skip, q.Take }, ct));
        var rows = (await grid.ReadAsync<SummaryRow>()).ToList();
        var last = await grid.ReadSingleOrDefaultAsync<ImportRow>();
        return new CostingPageDto(rows.Select(Map).ToList(), rows.FirstOrDefault()?.Total ?? 0, last is null ? null : Map(last), false);
    }

    public async Task<StylePricesDto> GetStyleAsync(int styleId, int smallOrderQty, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        List<SummaryRow> summaries;
        using (var grid = await conn.QueryMultipleAsync(Proc("cost.usp_Costing_List", new { StyleId = styleId, SmallQty = smallOrderQty, Take = 20 }, ct)))
            summaries = (await grid.ReadAsync<SummaryRow>()).ToList();
        using var ios = await conn.QueryMultipleAsync(Proc("cost.usp_StylePrices_Get", new { StyleId = styleId, SmallQty = smallOrderQty }, ct));
        var rows = (await ios.ReadAsync<IoRow>()).Select(r => new IoPriceDto(r.IoNo, r.PlantCode, r.PlantName, r.OrderQty, r.LcoDate, r.PlanDeliveryDate, r.TcSmv, r.CostingVersion,
            r.CostType, r.CurrencyCode.Trim(), r.CostDate, r.Fob, r.Fabric, r.Trims, r.Making, r.Other, r.NetBalance, r.PercToFob, r.FinalPending, r.IsSmall)).ToList();
        var unfinished = await ios.ReadSingleAsync<int>();
        return new StylePricesDto(summaries.Select(Map).ToList(), rows, unfinished, false, smallOrderQty);
    }

    public async Task<CostingImportResultDto> ImportAsync(string fileName, IReadOnlyList<CostingImportRow> rows, string importedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("cost.usp_Import_Save",
            new { FileName = fileName, ImportedBy = importedBy, Rows = JsonSerializer.Serialize(rows, Json) }, ct, 300));
        var import = await grid.ReadSingleAsync<ImportRow>();
        var unmatched = (await grid.ReadAsync<UnmatchedRow>()).Select(r => new UnmatchedStyleDto(r.Customer, r.Season, r.StyleNo, r.Rows)).ToList();
        return new CostingImportResultDto(Map(import), unmatched, []);
    }

    public async Task<IReadOnlyList<StylePriceHintDto>> PriceHintsAsync(IReadOnlyList<int> styleIds, int smallOrderQty, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<HintRow>(Proc("cost.usp_Price_Hints", new { StyleIds = JsonSerializer.Serialize(styleIds), SmallQty = smallOrderQty }, ct)))
            .Select(Map).ToList();
    }

    private static StylePriceHintDto Map(HintRow r) => new(r.StyleId, r.Source!, r.SourceStyleNo!, r.CurrencyCode!, r.LatestFob!.Value, r.MinFob!.Value, r.MaxFob!.Value, r.AvgFob!.Value,
        r.Ios!.Value, r.TotalQty!.Value, r.LatestCostDate, r.LatestIoNo);

    public async Task<PackageQuotesDto> PackageQuotesAsync(int packageId, int smallOrderQty, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("cost.usp_Quote_List", new { PackageId = packageId, SmallQty = smallOrderQty }, ct));
        var band = await grid.ReadSingleOrDefaultAsync<BandRow>();
        var items = (await grid.ReadAsync<HintItemRow>()).ToList();
        var quotes = (await grid.ReadAsync<QuoteRow>()).ToLookup(q => q.PackageItemId);
        return new PackageQuotesDto(
            band is null ? null : new ClientBandDto(band.PriceMin, band.PriceMax, band.Currency),
            items.Select(i => new PackageItemQuotesDto(i.PackageItemId, i.StyleId, i.Source is null ? null : Map(i),
                quotes[i.PackageItemId].Select(q => new QuoteDto(q.Version, q.Fob, q.CurrencyCode, q.BasisKind, q.BasisText, q.QuotedBy, DateTime.SpecifyKind(q.QuotedUtc, DateTimeKind.Utc))).ToList(),
                i.EstFob is null ? null : new EstimateDto(i.EstCategory!, i.EstPeers ?? 0, i.EstPeerStyles ?? "", i.EstCurrency!, i.EstFob.Value, i.EstSmv, i.EstPlant, i.EstCpm,
                    i.EstCpmFromRate ?? false, i.EstMaking, i.EstFabric, i.EstTrims, i.EstOther, i.EstNetShare))).ToList());
    }

    public async Task SaveQuoteAsync(int packageId, int packageItemId, decimal fob, string currency, string basisKind, string? basisText, string quotedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            await conn.ExecuteAsync(Proc("cost.usp_Quote_Save", new
            {
                PackageId = packageId, PackageItemId = packageItemId, Fob = fob, CurrencyCode = currency, BasisKind = basisKind, BasisText = basisText, QuotedBy = quotedBy,
            }, ct));
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    public async Task<IReadOnlyList<QuotedPrice>> QuotedPricesAsync(int packageId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<PriceRow>(Proc("cost.usp_Quote_Prices", new { PackageId = packageId }, ct)))
            .Select(r => new QuotedPrice(r.PackageItemId, r.Fob, r.CurrencyCode)).ToList();
    }

    public async Task<IReadOnlyList<ClientIssueDto>> QuoteIssuesAsync(int packageId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<IssueRow>(Proc("cost.usp_Quote_Check", new { PackageId = packageId }, ct)))
            .Select(r => new ClientIssueDto(r.StyleId, r.Kind, r.Detail)).ToList();
    }

    public async Task<PlantRatesDto> PlantRatesAsync(bool withDeleted, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("cost.usp_PlantRate_List", new { WithDeleted = withDeleted }, ct));
        var rates = (await grid.ReadAsync<RateRow>()).Select(r => new PlantRateDto(r.RateId, r.PlantCode, r.PlantName, r.EffectiveDate, r.MinOrderQty, r.SalesTerm,
            r.CurrencyCode, r.Cost, r.IsDeleted, r.InForce, r.CreatedBy, DateTime.SpecifyKind(r.CreatedUtc, DateTimeKind.Utc), r.UpdatedBy,
            r.UpdatedUtc is { } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : null)).ToList();
        var plants = (await grid.ReadAsync<PlantRow>()).Select(p => new LookupItem(p.Code, p.Name)).ToList();
        return new PlantRatesDto(rates, plants);
    }

    public Task<int> SavePlantRateAsync(int? rateId, string plantCode, DateOnly effectiveDate, int minOrderQty, string salesTerm, string currency, decimal cost, string changedBy,
        CancellationToken ct = default)
        => Rule(async conn => await conn.QuerySingleAsync<int>(Proc("cost.usp_PlantRate_Save", new
        {
            RateId = rateId, PlantCode = plantCode, EffectiveDate = effectiveDate.ToDateTime(TimeOnly.MinValue), MinOrderQty = minOrderQty, SalesTerm = salesTerm,
            CurrencyCode = currency, Cost = cost, ChangedBy = changedBy,
        }, ct)), ct);

    public Task DeletePlantRateAsync(int rateId, string changedBy, CancellationToken ct = default)
        => Rule(async conn => await conn.ExecuteAsync(Proc("cost.usp_PlantRate_Delete", new { RateId = rateId, ChangedBy = changedBy }, ct)), ct);

    /// <summary>Runs a write whose rule errors (THROW 50400-50499) become StyleRuleException.</summary>
    private async Task<T> Rule<T>(Func<System.Data.IDbConnection, Task<T>> write, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            return await write(conn);
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    public async Task<IReadOnlyList<PriceGuideRowDto>> PriceGuideAsync(int smallOrderQty, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<GuideRow>(Proc("cost.usp_Costing_Guide", new { SmallQty = smallOrderQty }, ct)))
            .Select(r => new PriceGuideRowDto(r.Category, r.CategoryName, r.CurrencyCode, r.Styles, r.LowFob, r.TypicalFob, r.HighFob, r.LowSmv, r.TypicalSmv, r.HighSmv)).ToList();
    }

    public async Task<IReadOnlyList<ConceptCostDto>> MyConceptsAsync(string userName, int smallOrderQty, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<MyConceptRow>(Proc("cost.usp_Costing_MyConcepts", new { UserName = userName, SmallQty = smallOrderQty }, ct)))
            .Select(r =>
            {
                var (status, gap) = TargetPrice.Judge(r.TargetPrice, r.ProductCategoryCode, r.EstimatedFob, r.CurrencyCode);
                return new ConceptCostDto(r.ConceptId, r.Name, r.CategoryName, r.CustomerCode, r.TargetPrice, r.EstimatedFob, r.CurrencyCode, r.Peers ?? 0, status, gap);
            }).ToList();
    }

    public async Task<CostingOverviewDto> OverviewAsync(int smallOrderQty, decimal netGap, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("cost.usp_Costing_Overview", new { SmallQty = smallOrderQty, NetGap = netGap }, ct));
        var h = await grid.ReadSingleAsync<HealthRow>();
        var net = (await grid.ReadAsync<NetRow>()).Select(r => new NetByGarmentDto(r.Category, r.CurrencyCode, r.Styles, r.Ios, r.Qty, r.Value, r.NetPct)).ToList();
        var drops = (await grid.ReadAsync<DropRow>()).Select(r => new NetDropDto(r.StyleId, r.StyleNo, r.IoNo, r.CostDate, r.Fob, r.CurrencyCode, r.IoPct, r.StylePct)).ToList();
        var counts = await grid.ReadSingleAsync<CountRow>();
        var items = (await grid.ReadAsync<AttentionRow>()).Select(r => new QuoteAttentionDto(r.PackageId, r.PackageName, r.StyleNo, r.Kind, r.Fob, r.CurrencyCode, r.PriceMin, r.PriceMax)).ToList();
        return new CostingOverviewDto(
            new CostingHealthDto(h.LastImportUtc is { } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : null, h.LastImportFile, h.LastImportBy, h.LastImportUnmatched ?? 0, h.StylesWithPrices,
                h.ActiveStyles, h.Ios, h.IosUnfinished, h.FinalPending, h.SmallIos, h.PlantsWithoutRate, h.PlantsWithoutRateList),
            net, drops, new QuoteCountsDto(counts.NoQuote, counts.OutOfBand, counts.Estimated, counts.Packages), items, netGap);
    }

    private sealed class GuideRow
    {
        public string Category { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public int Styles { get; set; }
        public decimal LowFob { get; set; }
        public decimal TypicalFob { get; set; }
        public decimal HighFob { get; set; }
        public decimal? LowSmv { get; set; }
        public decimal? TypicalSmv { get; set; }
        public decimal? HighSmv { get; set; }
    }

    private sealed class MyConceptRow
    {
        public int ConceptId { get; set; }
        public string Name { get; set; } = "";
        public string? ProductCategoryCode { get; set; }
        public string? CategoryName { get; set; }
        public string? CustomerCode { get; set; }
        public string? TargetPrice { get; set; }
        public decimal? EstimatedFob { get; set; }
        public string? CurrencyCode { get; set; }
        public int? Peers { get; set; }
    }

    private sealed class HealthRow
    {
        public DateTime? LastImportUtc { get; set; }
        public string? LastImportFile { get; set; }
        public string? LastImportBy { get; set; }
        public int? LastImportUnmatched { get; set; }
        public int StylesWithPrices { get; set; }
        public int ActiveStyles { get; set; }
        public int Ios { get; set; }
        public int IosUnfinished { get; set; }
        public int FinalPending { get; set; }
        public int SmallIos { get; set; }
        public int PlantsWithoutRate { get; set; }
        public string? PlantsWithoutRateList { get; set; }
    }

    private sealed class NetRow
    {
        public string Category { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public int Styles { get; set; }
        public int Ios { get; set; }
        public long Qty { get; set; }
        public decimal Value { get; set; }
        public decimal? NetPct { get; set; }
    }

    private sealed class DropRow
    {
        public int StyleId { get; set; }
        public string StyleNo { get; set; } = "";
        public string IoNo { get; set; } = "";
        public DateTime? CostDate { get; set; }
        public decimal Fob { get; set; }
        public string CurrencyCode { get; set; } = "";
        public decimal IoPct { get; set; }
        public decimal StylePct { get; set; }
    }

    private sealed class CountRow
    {
        public int NoQuote { get; set; }
        public int OutOfBand { get; set; }
        public int Estimated { get; set; }
        public int Packages { get; set; }
    }

    private sealed class AttentionRow
    {
        public int PackageId { get; set; }
        public string PackageName { get; set; } = "";
        public string StyleNo { get; set; } = "";
        public string Kind { get; set; } = "";
        public decimal? Fob { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
    }

    private sealed class RateRow
    {
        public int RateId { get; set; }
        public string PlantCode { get; set; } = "";
        public string PlantName { get; set; } = "";
        public DateTime EffectiveDate { get; set; }
        public int MinOrderQty { get; set; }
        public string SalesTerm { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public decimal Cost { get; set; }
        public bool IsDeleted { get; set; }
        public bool InForce { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedUtc { get; set; }
    }

    private sealed class PlantRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private class HintRow
    {
        public int StyleId { get; set; }
        public string? Source { get; set; }
        public string? SourceStyleNo { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal? LatestFob { get; set; }
        public decimal? MinFob { get; set; }
        public decimal? MaxFob { get; set; }
        public decimal? AvgFob { get; set; }
        public int? Ios { get; set; }
        public long? TotalQty { get; set; }
        public DateTime? LatestCostDate { get; set; }
        public string? LatestIoNo { get; set; }
    }

    private sealed class HintItemRow : HintRow
    {
        public int PackageItemId { get; set; }
        public string? EstCategory { get; set; }
        public int? EstPeers { get; set; }
        public string? EstPeerStyles { get; set; }
        public string? EstCurrency { get; set; }
        public decimal? EstFob { get; set; }
        public decimal? EstSmv { get; set; }
        public string? EstPlant { get; set; }
        public decimal? EstCpm { get; set; }
        public bool? EstCpmFromRate { get; set; }
        public decimal? EstMaking { get; set; }
        public decimal? EstFabric { get; set; }
        public decimal? EstTrims { get; set; }
        public decimal? EstOther { get; set; }
        public decimal? EstNetShare { get; set; }
    }

    public async Task<EstimateDto?> EstimateAsync(string category, string? customerCode, int smallOrderQty, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var r = await conn.QuerySingleOrDefaultAsync<EstimateRow>(Proc("cost.usp_Estimate", new { Category = category, CustomerCode = customerCode, SmallQty = smallOrderQty }, ct));
        return r is null ? null : new EstimateDto(r.Category, r.Peers, r.PeerStyles, r.CurrencyCode, r.EstimatedFob, r.Smv, r.PlantCode, r.Cpm, r.CpmFromRate, r.Making, r.Fabric,
            r.Trims, r.Other, r.NetShare);
    }

    private sealed class EstimateRow
    {
        public string Category { get; set; } = "";
        public int Peers { get; set; }
        public string PeerStyles { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public decimal? Smv { get; set; }
        public string? PlantCode { get; set; }
        public decimal? Cpm { get; set; }
        public bool CpmFromRate { get; set; }
        public decimal? Making { get; set; }
        public decimal? Fabric { get; set; }
        public decimal? Trims { get; set; }
        public decimal? Other { get; set; }
        public decimal? NetShare { get; set; }
        public decimal EstimatedFob { get; set; }
    }

    private sealed class BandRow
    {
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public string Currency { get; set; } = "";
    }

    private sealed class QuoteRow
    {
        public int PackageItemId { get; set; }
        public int Version { get; set; }
        public decimal Fob { get; set; }
        public string CurrencyCode { get; set; } = "";
        public string BasisKind { get; set; } = "";
        public string? BasisText { get; set; }
        public string QuotedBy { get; set; } = "";
        public DateTime QuotedUtc { get; set; }
    }

    private sealed class PriceRow
    {
        public int PackageItemId { get; set; }
        public decimal Fob { get; set; }
        public string CurrencyCode { get; set; } = "";
    }

    private sealed class IssueRow
    {
        public int StyleId { get; set; }
        public string Kind { get; set; } = "";
        public string Detail { get; set; } = "";
    }

    private sealed class SummaryRow
    {
        public int StyleId { get; set; }
        public string StyleNo { get; set; } = "";
        public string SeasonCode { get; set; } = "";
        public string CustomerCode { get; set; } = "";
        public string? ModelName { get; set; }
        public string? ProductTypeName { get; set; }
        public string? ImageUrl { get; set; }
        public string? SketchUrl { get; set; }
        public string CurrencyCode { get; set; } = "";
        public int Ios { get; set; }
        public long TotalQty { get; set; }
        public decimal LatestFob { get; set; }
        public DateTime? LatestCostDate { get; set; }
        public decimal MinFob { get; set; }
        public decimal MaxFob { get; set; }
        public decimal AvgFob { get; set; }
        public decimal? AvgFabric { get; set; }
        public decimal? AvgTrims { get; set; }
        public decimal? AvgMaking { get; set; }
        public decimal? AvgOther { get; set; }
        public decimal? AvgNet { get; set; }
        public int Total { get; set; }
    }

    private sealed class IoRow
    {
        public string IoNo { get; set; } = "";
        public string? PlantCode { get; set; }
        public string? PlantName { get; set; }
        public int? OrderQty { get; set; }
        public DateTime? LcoDate { get; set; }
        public DateTime? PlanDeliveryDate { get; set; }
        public decimal? TcSmv { get; set; }
        public int CostingVersion { get; set; }
        public string CostType { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public DateTime? CostDate { get; set; }
        public decimal Fob { get; set; }
        public decimal? Fabric { get; set; }
        public decimal? Trims { get; set; }
        public decimal? Making { get; set; }
        public decimal? Other { get; set; }
        public decimal? NetBalance { get; set; }
        public decimal? PercToFob { get; set; }
        public bool FinalPending { get; set; }
        public bool IsSmall { get; set; }
    }

    private sealed class ImportRow
    {
        public int ImportId { get; set; }
        public string FileName { get; set; } = "";
        public int RowsRead { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Unmatched { get; set; }
        public string ImportedBy { get; set; } = "";
        public DateTime ImportedUtc { get; set; }
    }

    private sealed class UnmatchedRow
    {
        public string Customer { get; set; } = "";
        public string Season { get; set; } = "";
        public string StyleNo { get; set; } = "";
        public int Rows { get; set; }
    }
}
