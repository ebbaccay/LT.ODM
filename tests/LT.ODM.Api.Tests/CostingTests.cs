using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.Costing;
using LT.ODM.Infrastructure.Costing;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>Style 7 has two IOs at FOB 8.50 / 8.47888, with cost groups; imports are kept.</summary>
public sealed class FakeCostingRepository : ICostingRepository
{
    public List<(string FileName, IReadOnlyList<CostingImportRow> Rows, string By)> Imports { get; } = [];
    public (CostingListQuery Query, int SmallQty)? LastList { get; private set; }

    private static readonly StylePriceSummaryDto Summary = new(7, "S2725M112", "2027-SS", "ADI", "ESS SS TEE DNM", "T-SHIRT", null, null, "USD", 2, 18_864, 8.5m,
        new DateTime(2026, 9, 25), 8.47888m, 8.5m, 8.49m, 2.4m, 0.5m, 1.07492m, 0.28m, 4.2m);

    private static readonly IoPriceDto[] Ios =
    [
        new("U9ALD9-M", "CM35", "XTIC YuenThai Cambodia", 15_348, new DateTime(2026, 9, 25), new DateTime(2027, 1, 9), 7.678m, 140579, "PCOSTING", "USD", new DateTime(2026, 9, 25), 8.5m, 2.45m, 0.55m, 1.07m, 0.28m, 4.15m, 48.8m, false, false),
        new("U9AL53-M", "CM35", "XTIC YuenThai Cambodia", 12, new DateTime(2026, 5, 29), new DateTime(2026, 9, 10), 7.678m, 139376, "PCOSTING", "USD", new DateTime(2026, 5, 29), 8.47888m, 2.4m, 0.49m, 1.07m, 0.28m, 4.22m, 49.8m, false, true),
    ];

    public Task<CostingPageDto> ListAsync(CostingListQuery query, int smallOrderQty, CancellationToken ct = default)
    {
        LastList = (query, smallOrderQty);
        return Task.FromResult(new CostingPageDto([Summary], 1, null, false));
    }

    public Task<StylePricesDto> GetStyleAsync(int styleId, int smallOrderQty, CancellationToken ct = default)
        => Task.FromResult(styleId == 7 ? new StylePricesDto([Summary], Ios, 1, false, smallOrderQty) : new StylePricesDto([], [], 0, false, smallOrderQty));

    public List<(int PackageId, int ItemId, decimal Fob, string Currency, string Kind, string? Text, string By)> SavedQuotes { get; } = [];

    public Task<IReadOnlyList<StylePriceHintDto>> PriceHintsAsync(IReadOnlyList<int> styleIds, int smallOrderQty, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StylePriceHintDto>>(styleIds.Where(id => id == 7)
            .Select(id => new StylePriceHintDto(id, "Own", "S2725M112", "USD", 8.5m, 8.47888m, 8.5m, 8.49m, 2, 18_864, new DateTime(2026, 9, 25), "U9ALD9-M")).ToList());

    public Task<PackageQuotesDto> PackageQuotesAsync(int packageId, int smallOrderQty, CancellationToken ct = default)
        => Task.FromResult(new PackageQuotesDto(new ClientBandDto(8m, 14m, "USD"), [new PackageItemQuotesDto(1, 7, null, [])]));

    public Task SaveQuoteAsync(int packageId, int packageItemId, decimal fob, string currency, string basisKind, string? basisText, string quotedBy, CancellationToken ct = default)
    {
        if (packageItemId == 99) throw new LT.ODM.Application.StyleLibrary.StyleRuleException(404, "This style is no longer in the package.");
        SavedQuotes.Add((packageId, packageItemId, fob, currency, basisKind, basisText, quotedBy));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QuotedPrice>> QuotedPricesAsync(int packageId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<QuotedPrice>>([]);

    public Task<IReadOnlyList<LT.ODM.Application.StyleLibrary.ClientIssueDto>> QuoteIssuesAsync(int packageId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LT.ODM.Application.StyleLibrary.ClientIssueDto>>([new(4, "Price", "20.00 USD (band 8.00 to 14.00)")]);

    public List<(int? RateId, string Plant, DateOnly From, int MinQty, string Term, string Currency, decimal Cost, string By)> SavedRates { get; } = [];

    public Task<EstimateDto?> EstimateAsync(string category, string? customerCode, int smallOrderQty, CancellationToken ct = default)
        => Task.FromResult(category == "HOODIE"
            ? new EstimateDto("HOODIE", 5, "S2725M113, S2725W217", "USD", 18.9m, 21m, "CM35", 0.14m, true, 2.94m, 3.2m, 0.84m, 0.22m, 0.5m)
            : null);

    public string? LastMyConceptsUser { get; private set; }

    public Task<IReadOnlyList<PriceGuideRowDto>> PriceGuideAsync(int smallOrderQty, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PriceGuideRowDto>>([new("HOODIE", "Hoodies", "USD", 5, 15.38m, 20.37m, 20.61m, 18.6m, 18.9m, 25.5m)]);

    public Task<IReadOnlyList<ConceptCostDto>> MyConceptsAsync(string userName, int smallOrderQty, CancellationToken ct = default)
    {
        LastMyConceptsUser = userName;
        return Task.FromResult<IReadOnlyList<ConceptCostDto>>([new(8, "Denim hoodie", "Hoodies", "ADI", "USD 15-17 FOB", 18.54m, "USD", 5, "Over", 1.54m)]);
    }

    public Task<CostingOverviewDto> OverviewAsync(int smallOrderQty, decimal netGap, CancellationToken ct = default)
        => Task.FromResult(new CostingOverviewDto(new CostingHealthDto(DateTime.UtcNow, "Costing.xlsx", "ebaccay", 0, 22, 2521, 134, 0, 2, 6, 0, null), [], [],
            new QuoteCountsDto(3, 1, 1, 2), [], netGap));

    public Task<PlantRatesDto> PlantRatesAsync(bool withDeleted, CancellationToken ct = default)
        => Task.FromResult(new PlantRatesDto(
            [new PlantRateDto(1, "CM35", "XTIC YuenThai Cambodia", new DateTime(2026, 1, 1), 1, "FOB", "USD", 0.14m, false, true, "seed", DateTime.UtcNow, null, null)],
            [new LT.ODM.Application.StyleLibrary.LookupItem("CM35", "XTIC YuenThai Cambodia")]));

    public Task<int> SavePlantRateAsync(int? rateId, string plantCode, DateOnly effectiveDate, int minOrderQty, string salesTerm, string currency, decimal cost, string changedBy,
        CancellationToken ct = default)
    {
        if (plantCode == "NOPE") throw new LT.ODM.Application.StyleLibrary.StyleRuleException(404, "This plant is not in Settings > Reference lists > Plants.");
        SavedRates.Add((rateId, plantCode, effectiveDate, minOrderQty, salesTerm, currency, cost, changedBy));
        return Task.FromResult(rateId ?? 2);
    }

    public Task DeletePlantRateAsync(int rateId, string changedBy, CancellationToken ct = default)
        => rateId == 99 ? throw new LT.ODM.Application.StyleLibrary.StyleRuleException(404, "This rate no longer exists. Reload the list.") : Task.CompletedTask;

    public Task<CostingImportResultDto> ImportAsync(string fileName, IReadOnlyList<CostingImportRow> rows, string importedBy, CancellationToken ct = default)
    {
        Imports.Add((fileName, rows, importedBy));
        return Task.FromResult(new CostingImportResultDto(new CostingImportDto(1, fileName, rows.Count, rows.Count, 0, 0, importedBy, DateTime.UtcNow), [], []));
    }
}

public sealed class CostingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static readonly object?[] Headings =
    [
        "CUST", "SEASON", "STYLE ID", "IO NO", "PLANT CODE", "PLANT NAME", "ORDER QTY", "LCO DATE", "PLAN DELDT", "MERCH SMV", "TC SMV", "COST VER#", "STATUS", "CURRENCY", "COST DT",
        "COST TYPE", "FOB", "MAIN FAB", "TRIMS", "MAT FRT", "CM", "CLAIM", "NET BAT", "PERC TO FOB",
    ];

    private static object?[] Row(object? styleNo, object? ioNo, object? version, object? type, object? fob, object? net)
        => ["ADI", "2027-SS", styleNo, ioNo, "CM35", "XTIC", "1200", "2026-05-29", "2026-09-10", "7.678", "7.678", version, "Completed", "usd", "2026-05-29 13:55:04", type, fob, "2.402384", "0.49265", "0.12", "1.07492", "0.1", net, "49.87"];

    private async Task<(HttpClient Client, string Name)> SignInAsync(string role)
    {
        var name = "ct" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, name);
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string name = "Costing.xlsx")
        => new() { { new ByteArrayContent(bytes), "file", name } };

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Factory")]
    public async Task Prices_are_only_for_admin_merchandiser_designer_and_costing(string role)
    {
        var (client, _) = await SignInAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/costing/styles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/costing/styles/7")).StatusCode);
    }

    [Fact]
    public async Task Merchandisers_see_fob_but_not_costs_or_net_balance()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        var style = (await merch.GetFromJsonAsync<StylePricesDto>("/api/v1/costing/styles/7"))!;
        Assert.False(style.ShowCost);
        Assert.Equal(8.5m, style.Summaries.Single().LatestFob);
        Assert.Null(style.Summaries.Single().AvgNet);
        Assert.Null(style.Summaries.Single().AvgFabric);
        Assert.All(style.Ios, i => Assert.True(i.Fabric is null && i.Trims is null && i.Making is null && i.Other is null && i.NetBalance is null && i.PercToFob is null));

        var page = (await merch.GetFromJsonAsync<CostingPageDto>("/api/v1/costing/styles"))!;
        Assert.False(page.ShowCost);
        Assert.Null(page.Items.Single().AvgNet);
    }

    [Theory]
    [InlineData("Costing")]
    [InlineData("Admin")]
    public async Task Costing_and_admin_see_the_cost_groups(string role)
    {
        var (client, _) = await SignInAsync(role);
        var style = (await client.GetFromJsonAsync<StylePricesDto>("/api/v1/costing/styles/7"))!;
        Assert.True(style.ShowCost);
        Assert.Equal(4.2m, style.Summaries.Single().AvgNet);
        Assert.Equal(4.15m, style.Ios[0].NetBalance);
        Assert.True(style.Ios[1].IsSmall);
        Assert.Empty((await client.GetFromJsonAsync<StylePricesDto>("/api/v1/costing/styles/99"))!.Ios);
    }

    [Fact]
    public async Task The_list_pages_and_uses_the_small_order_setting()
    {
        var (admin, _) = await SignInAsync("Admin");
        await admin.GetFromJsonAsync<CostingPageDto>("/api/v1/costing/styles?search=M112&customer=ADI&season=2027-SS&page=3&pageSize=10");
        var (query, small) = factory.Costing.LastList!.Value;
        Assert.Equal(new CostingListQuery("M112", "ADI", "2027-SS", 20, 10), query);
        Assert.Equal(50, small);
    }

    [Fact]
    public async Task Only_admin_loads_the_export_and_it_must_be_a_workbook()
    {
        var (costing, _) = await SignInAsync("Costing");
        Assert.Equal(HttpStatusCode.Forbidden, (await costing.PostAsync("/api/v1/costing/imports", Upload(TestWorkbook.Create(("Costing", [Headings]))))).StatusCode);

        var (admin, _) = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/costing/imports", Upload([1, 2, 3, 4, 5], "Costing.csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/costing/imports", Upload([1, 2, 3, 4, 5]))).StatusCode);
        var wrong = await admin.PostAsync("/api/v1/costing/imports", Upload(TestWorkbook.Create(("Sheet1", [new object?[] { "CUST", "SEASON" }]))));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("STYLE ID", await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_export_is_loaded_with_who_did_it()
    {
        var (admin, name) = await SignInAsync("Admin");
        var xlsx = TestWorkbook.Create(("Costing", [Headings, Row("S2725M112", "U9AL53-M", "139376", "PCOSTING", "8.47888", "4.28893")]));
        var response = await admin.PostAsync("/api/v1/costing/imports", Upload(xlsx));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (file, rows, by) = factory.Costing.Imports.Last();
        Assert.Equal(("Costing.xlsx", name), (file, by));
        Assert.Equal("U9AL53-M", rows.Single().IoNo);
    }

    [Fact]
    public async Task Designers_see_fob_but_set_no_quotes_and_send_no_prices()
    {
        var (designer, _) = await SignInAsync("Designer");
        var style = (await designer.GetFromJsonAsync<StylePricesDto>("/api/v1/costing/styles/7"))!;
        Assert.False(style.ShowCost);
        Assert.Equal(8.5m, style.Summaries.Single().LatestFob);
        Assert.Null(style.Summaries.Single().AvgNet);
        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync("/api/v1/style-packages/5/quotes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await designer.PostAsJsonAsync("/api/v1/costing/prices", new { styleIds = new[] { 7 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await designer.PutAsJsonAsync("/api/v1/style-packages/5/items/1/quote", new { fob = 9, currency = "USD", basisKind = "Typed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await designer.GetAsync("/api/v1/costing/rates")).StatusCode);
        var check = await designer.GetFromJsonAsync<LT.ODM.Application.StyleLibrary.ClientCheckDto>("/api/v1/style-packages/5/client-check");
        Assert.DoesNotContain(check!.Issues, i => i.Kind == "Price");
    }

    [Theory]
    [InlineData("Viewer")]
    public async Task Quotes_and_hints_are_not_for_those_who_cannot_see_prices(string role)
    {
        var (client, _) = await SignInAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/style-packages/5/quotes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/v1/style-packages/5/items/1/quote", new { fob = 9, currency = "USD", basisKind = "Typed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/costing/prices", new { styleIds = new[] { 7 } })).StatusCode);
        var check = await client.GetFromJsonAsync<LT.ODM.Application.StyleLibrary.ClientCheckDto>("/api/v1/style-packages/5/client-check");
        Assert.DoesNotContain(check!.Issues, i => i.Kind == "Price");
    }

    [Fact]
    public async Task A_quote_is_checked_and_saved_with_who_set_it()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        var bad = await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/1/quote", new { fob = 0, currency = "dollars", basisKind = "Guess" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        foreach (var field in new[] { "fob", "currency", "basisKind" }) Assert.Contains(field, body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/1/quote",
            new { fob = 8.123456, currency = " usd ", basisKind = "Own", basisText = " From 2 IOs " })).StatusCode);
        Assert.Equal((5, 1, 8.12346m, "USD", "Own", "From 2 IOs", name), factory.Costing.SavedQuotes.Last());
        Assert.Equal(HttpStatusCode.NotFound, (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/99/quote", new { fob = 9, currency = "USD", basisKind = "Typed" })).StatusCode);
    }

    [Fact]
    public async Task Merchandisers_get_hints_and_the_price_band_issue()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        var response = await merch.PostAsJsonAsync("/api/v1/costing/prices", new { styleIds = new[] { 7, 8, 7 } });
        Assert.Equal(7, Assert.Single((await response.Content.ReadFromJsonAsync<List<StylePriceHintDto>>())!).StyleId);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/costing/prices", new { styleIds = Enumerable.Range(1, 101) })).StatusCode);
        var check = await merch.GetFromJsonAsync<LT.ODM.Application.StyleLibrary.ClientCheckDto>("/api/v1/style-packages/5/client-check");
        Assert.Contains(check!.Issues, i => i.Kind == "Price");
    }

    [Fact]
    public void The_client_reads_the_quote_to_two_decimals()
    {
        var (fob, currency) = (19.44941m, "USD");
        Assert.Equal("USD 19.45 FOB", LT.ODM.Application.StyleLibrary.LineSheetContent.Price(Item(fob, currency)));
        Assert.Null(LT.ODM.Application.StyleLibrary.LineSheetContent.Price(Item(null, null)));
    }

    private static LT.ODM.Application.StyleLibrary.PackageItemDto Item(decimal? fob, string? currency)
        => new(1, 7, 1, null, null, true, null, "S1", "2027-SS", "ADI", null, null, null, null, null, null, null, null, null, null, true, false, 0, 0, "x", DateTime.UtcNow, null, null, [0], [], [])
        {
            QuotedFob = fob, QuotedCurrency = currency,
        };

    [Fact]
    public async Task Merchandisers_cannot_see_or_change_plant_rates()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.Forbidden, (await merch.GetAsync("/api/v1/costing/rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await merch.PostAsJsonAsync("/api/v1/costing/rates", new { plantCode = "CM35" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await merch.DeleteAsync("/api/v1/costing/rates/1")).StatusCode);
    }

    [Fact]
    public async Task Costing_keeps_plant_rates_checked_and_cleaned()
    {
        var (costing, name) = await SignInAsync("Costing");
        var list = (await costing.GetFromJsonAsync<PlantRatesDto>("/api/v1/costing/rates"))!;
        Assert.Equal(0.14m, list.Rates.Single().Cost);
        Assert.Equal("CM35", list.Plants.Single().Code);

        var bad = await costing.PostAsJsonAsync("/api/v1/costing/rates", new { plantCode = "", minOrderQty = -1, salesTerm = "F O B", currency = "dollars", cost = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        foreach (var field in new[] { "plantCode", "effectiveDate", "minOrderQty", "salesTerm", "currency", "cost" }) Assert.Contains(field, body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await costing.PutAsJsonAsync("/api/v1/costing/rates/1",
            new { plantCode = " cm35 ", effectiveDate = "2026-07-01", minOrderQty = 1, salesTerm = "fob", currency = "usd", cost = 0.1456789 })).StatusCode);
        Assert.Equal((1, "CM35", new DateOnly(2026, 7, 1), 1, "FOB", "USD", 0.14568m, name), factory.Costing.SavedRates.Last());
        Assert.Equal(HttpStatusCode.NotFound, (await costing.PostAsJsonAsync("/api/v1/costing/rates",
            new { plantCode = "NOPE", effectiveDate = "2026-07-01", minOrderQty = 1, salesTerm = "FOB", currency = "USD", cost = 0.2 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await costing.DeleteAsync("/api/v1/costing/rates/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await costing.DeleteAsync("/api/v1/costing/rates/99")).StatusCode);
    }

    [Fact]
    public async Task An_estimate_shows_its_parts_only_to_admin_and_costing()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        var forMerch = (await merch.GetFromJsonAsync<EstimateDto>("/api/v1/costing/estimate?category=hoodie&customer=ADI"))!;
        Assert.Equal(18.9m, forMerch.EstimatedFob);
        Assert.True(forMerch.Cpm is null && forMerch.Making is null && forMerch.Fabric is null && forMerch.NetShare is null);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.GetAsync("/api/v1/costing/estimate?category=DRESS")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.GetAsync("/api/v1/costing/estimate")).StatusCode);

        var (costing, _) = await SignInAsync("Costing");
        Assert.Equal(2.94m, (await costing.GetFromJsonAsync<EstimateDto>("/api/v1/costing/estimate?category=HOODIE"))!.Making);
        var (designer, _) = await SignInAsync("Designer");
        var forDesigner = (await designer.GetFromJsonAsync<EstimateDto>("/api/v1/costing/estimate?category=HOODIE"))!;
        Assert.True(forDesigner.EstimatedFob == 18.9m && forDesigner.Making is null);
        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/costing/estimate?category=HOODIE")).StatusCode);
    }

    [Theory]
    [InlineData("USD 15-17 FOB", "HOODIE", 18.54, "USD", "Over", 1.54)]
    [InlineData("USD 18-20 FOB", "HOODIE", 18.54, "USD", "Within", null)]
    [InlineData("20-25", "HOODIE", 18.54, "USD", "Under", 1.46)]
    [InlineData("EUR 18", "HOODIE", 18.54, "USD", "NoCompare", null)]
    [InlineData(null, "HOODIE", 18.54, "USD", "NoTarget", null)]
    [InlineData("USD 18", null, null, null, "NoCategory", null)]
    [InlineData("USD 18", "DRESS", null, null, "NoEstimate", null)]
    public void A_concept_is_judged_against_its_target_price(string? target, string? category, double? estimate, string? currency, string status, double? gap)
    {
        var judged = TargetPrice.Judge(target, category, (decimal?)estimate, currency);
        Assert.Equal((status, (decimal?)gap), judged);
    }

    [Fact]
    public async Task Dashboard_panels_follow_the_price_roles()
    {
        var (designer, name) = await SignInAsync("Designer");
        Assert.Equal("Hoodies", (await designer.GetFromJsonAsync<List<PriceGuideRowDto>>("/api/v1/costing/guide"))!.Single().CategoryName);
        Assert.Equal("Over", (await designer.GetFromJsonAsync<List<ConceptCostDto>>("/api/v1/costing/my-concepts"))!.Single().Status);
        Assert.Equal(name, factory.Costing.LastMyConceptsUser);
        Assert.Equal(HttpStatusCode.Forbidden, (await designer.GetAsync("/api/v1/costing/overview")).StatusCode);
        var (merch, _) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.Forbidden, (await merch.GetAsync("/api/v1/costing/overview")).StatusCode);
        var (costing, _) = await SignInAsync("Costing");
        Assert.Equal(22, (await costing.GetFromJsonAsync<CostingOverviewDto>("/api/v1/costing/overview"))!.Health.StylesWithPrices);
    }

    private static CostingWorkbook Read(params object?[][] rows)
        => new CostingWorkbookReader().Read(new MemoryStream(TestWorkbook.Create(("Costing", [Headings, .. rows]))));

    [Fact]
    public void The_reader_keeps_five_decimals_and_maps_the_categories()
    {
        var wb = Read(Row("S2725M112", "U9AL53-M", "139376", "pcosting", "8.47888", "4.28893"));
        Assert.Empty(wb.Problems);
        Assert.Empty(wb.Issues);
        var r = wb.Rows.Single();
        Assert.Equal(("PCOSTING", "USD", 8.47888m, 139376, 1200), (r.CostType, r.Currency, r.Fob, r.CostingVersion, r.OrderQty!.Value));
        Assert.Equal(2.40238m, r.Lines.Single(l => l.Code == "MAINFAB").Amount);
        Assert.Equal(0.12m, r.Lines.Single(l => l.Code == "MATFRT").Amount);
        Assert.Equal(new DateOnly(2026, 9, 10), r.PlanDeliveryDate);
    }

    [Fact]
    public void The_reader_points_out_bad_rows_and_a_net_that_does_not_add_up()
    {
        var wb = Read(
            Row("S1", "IO1", "1", "XCOSTING", "8", "1"),
            Row(null, "IO2", "2", "PCOSTING", "8", "1"),
            Row("S3", "IO3", "3", "PCOSTING", "8.47888", "1"),
            Row("S4", "IO4", "4", "FCOSTING", "0", null),
            Row("S4", "IO4", "4", "FCOSTING", "9", null));
        Assert.Equal(["IO3", "IO4"], wb.Rows.Select(r => r.IoNo));
        Assert.Equal(9m, wb.Rows.Single(r => r.IoNo == "IO4").Fob);
        Assert.Contains(wb.Issues, i => i.Contains("XCOSTING"));
        Assert.Contains(wb.Issues, i => i.StartsWith("Row 3:"));
        Assert.Contains(wb.Issues, i => i.Contains("IO3") && i.Contains("kept as sent"));
        Assert.Contains(wb.Issues, i => i.Contains("twice"));
    }
}
