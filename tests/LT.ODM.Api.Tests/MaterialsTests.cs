using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakeMaterialRepository : IMaterialRepository
{
    public MaterialListQuery? LastQuery { get; private set; }

    public Task<PagedResult<MaterialListItemDto>> ListAsync(MaterialListQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        return Task.FromResult(new PagedResult<MaterialListItemDto>(
            [new MaterialListItemDto(1, "70038500", "Ripstop", "WOV", "WOVEN FABRIC", "FAB", "Fabric", 6, 6, 2, 1, 1, "HUA FENG", "2028-SS")], 1));
    }

    public Task<MaterialDetailDto?> GetAsync(int materialId, CancellationToken ct = default)
        => Task.FromResult<MaterialDetailDto?>(materialId == 404 ? null : new MaterialDetailDto(
            new MaterialHeaderDto(materialId, "70038500", "Ripstop", "WOV", "WOVEN FABRIC", "FAB", "Fabric", 6, 6, 1, 2, "x", DateTime.UtcNow, null, null),
            [], [new MaterialBenchmarkDto("JACKET", "JACKET", "yd", 5, 1.1m, 1.3m, 1.4m, 1.5m, 1.9m)], [], [],
            [new SimilarMaterialDto(2, "70038500_1", "Ripstop", "SameDescription", 5)]));

    public MaterialInsightQuery? LastInsight { get; private set; }

    private Task<T> Insight<T>(MaterialInsightQuery q, T result)
    {
        LastInsight = q;
        return Task.FromResult(result);
    }

    public Task<StandardTrimsDto> StandardTrimsAsync(MaterialInsightQuery query, CancellationToken ct = default)
        => Insight(query, new StandardTrimsDto(83, [new TrimTypeDto("ZIP", "ZIPPER", "ACC", 51, 19, 71)], []));

    public Task<SupplierConcentrationDto> SuppliersAsync(MaterialInsightQuery query, CancellationToken ct = default)
        => Insight(query, new SupplierConcentrationDto(new SupplierTotalsDto(942, 2940, 224, 2546, 279, 2326, 28808), []));

    public Task<DuplicatesDto> DuplicatesAsync(MaterialInsightQuery query, CancellationToken ct = default)
        => Insight(query, new DuplicatesDto(342, 1019, []));

    public Task<RecycledShareDto> RecycledAsync(MaterialInsightQuery query, CancellationToken ct = default)
        => Insight(query, new RecycledShareDto([new RecycledRowDto("2027-SS", "2027-SS", 298, 169, 257, 931, 699)], []));

    public Task<IReadOnlyList<ColourUsageDto>> ColoursAsync(MaterialInsightQuery query, CancellationToken ct = default)
        => Insight<IReadOnlyList<ColourUsageDto>>(query, [new ColourUsageDto("BLACK", 3, 400, 300, 200, 6)]);
}

public sealed class MaterialsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "mat" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Readers_see_materials_factories_do_not_and_filters_are_cleaned()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync("/api/v1/materials")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).GetAsync("/api/v1/materials")).StatusCode);

        var costing = await SignInAsync("Costing");
        var page = await costing.GetFromJsonAsync<PagedResult<MaterialListItemDto>>("/api/v1/materials?search=%20ripstop%20&sort=drop&take=999");
        Assert.Equal(1, page!.Total);
        Assert.Equal(("ripstop", "used", 200), (factory.Materials.LastQuery!.Search, factory.Materials.LastQuery.Sort, factory.Materials.LastQuery.Take));

        var detail = await costing.GetFromJsonAsync<MaterialDetailDto>("/api/v1/materials/7");
        Assert.Equal(1.4m, Assert.Single(detail!.Benchmarks).Median);
        Assert.Equal(HttpStatusCode.NotFound, (await costing.GetAsync("/api/v1/materials/404")).StatusCode);
    }

    [Fact]
    public async Task Insights_need_a_reader_and_standard_trims_need_a_product_type()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).GetAsync("/api/v1/materials/insights/suppliers")).StatusCode);

        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync("/api/v1/materials/insights/standard-trims")).StatusCode);
        var trims = await viewer.GetFromJsonAsync<StandardTrimsDto>("/api/v1/materials/insights/standard-trims?productType=JACKET&season=2027-SS");
        Assert.Equal(51, Assert.Single(trims!.Types).Materials);
        Assert.Equal(("JACKET", "2027-SS"), (factory.Materials.LastInsight!.ProductType, factory.Materials.LastInsight.Season));

        foreach (var view in new[] { "suppliers?customer=ADI", "duplicates?contentClass=FAB", "recycled", "colours?season=2027-SS" })
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/materials/insights/{view}")).StatusCode);
        Assert.Equal("2027-SS", factory.Materials.LastInsight.Season);
    }
}
