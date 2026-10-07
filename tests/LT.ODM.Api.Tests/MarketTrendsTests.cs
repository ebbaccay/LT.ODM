using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Auth;
using LT.ODM.Application.MarketTrends;
using LT.ODM.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LT.ODM.Api.Tests;

public sealed class FakeTrendAnalysisAi : ITrendAnalysisAi
{
    public bool IsConfigured { get; set; } = true;

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(IsConfigured);

    public Task<TrendAnalysisDto> AnalyzeAsync(TrendAnalysisRequest request, CancellationToken ct = default)
        => Task.FromResult(new TrendAnalysisDto(
            [new RegionTrendDto("eu", "Eco Denim", "Revival", "Strong", 40)],
            [new StyleTrendDto(request.Styles![0].ItemRecid, 82, ["Eco Denim"], "Fits the trend.", "growing", 30)],
            ["Lead with recycled denim."]));
}

public sealed class MarketTrendsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static object Collection(int styles = 2) => new
    {
        conceptName = "SS27 Eco Denim", season = "SS27", targetMarket = "EU", targetFob = 15.5,
        activeTags = new[] { "Sustainable" }, fabricDirection = new[] { "Recycled twill" }, sustainabilityNotes = Array.Empty<string>(),
        styles = Enumerable.Range(1, styles).Select(i => new { itemRecid = i, styleName = $"Style {i}", styleCode = $"S-{i}", category = "Bottoms", sbu = "SBU", country = "China", fobPrice = 14.0 }),
    };

    private async Task<HttpClient> SignInAsync()
    {
        var name = "mt" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, ["Merchandiser"]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Analysis_needs_sign_in_styles_and_a_configured_ai()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/market-trends/analysis", Collection())).StatusCode);

        var client = await SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/market-trends/analysis", Collection())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/market-trends/analysis", Collection(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/market-trends/analysis", Collection(MarketTrendRules.MaxStyles + 1))).StatusCode);

        factory.TrendAi.IsConfigured = false;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/v1/market-trends/analysis", Collection())).StatusCode); }
        finally { factory.TrendAi.IsConfigured = true; }
    }

    private sealed class StubHandler(string modelJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = modelJson } } } } } })),
            });
    }

    private static Task<TrendAnalysisDto> Gemini(string modelJson)
        => new TrendAnalysisAi(TestAiText.Gemini(new StubHandler(modelJson)))
            .AnalyzeAsync(new TrendAnalysisRequest("C", "SS27", "EU", 15, [], [], [],
                [new TrendStyleInput(1, "Jean", "J", "Bottoms", "SBU", "China", 14), new TrendStyleInput(2, "Tee", "T", "Tops", "SBU", "China", 6)]));

    [Fact]
    public async Task Unknown_regions_and_styles_are_dropped_and_numbers_clamped()
    {
        var result = await Gemini("""
            {
              "regions": [
                { "id": "EU", "label": "Eco Denim", "strength": "strong", "growth": 900 },
                { "id": "mars", "label": "Space Wear", "strength": "Strong", "growth": 50 },
                { "id": "eu", "label": "Duplicate", "strength": "Strong", "growth": 10 },
                { "id": "na", "label": "Workwear", "strength": "Very hot", "growth": 20 }
              ],
              "styles": [
                { "item_recid": 1, "trend_score": 140, "trend_tags": ["Eco, Denim", "A", "B", "C"], "insight": "Jean fits.", "tag_type": "rocketing", "growth_pct": -400 },
                { "item_recid": 99, "trend_score": 50, "trend_tags": [], "insight": "Not in this collection.", "tag_type": "growing", "growth_pct": 5 }
              ],
              "ai_insights": ["One", "", "Two"]
            }
            """);
        var region = Assert.Single(result.Regions);
        Assert.Equal(("eu", "Strong", 500), (region.Id, region.Strength, region.GrowthPct));
        var style = Assert.Single(result.Styles);
        Assert.Equal((1, 100, -100, "emerging"), (style.ItemRecid, style.TrendScore, style.GrowthPct, style.TagType));
        Assert.Equal(["Eco  Denim", "A", "B"], style.TrendTags);   // commas would break the comma-separated column
        Assert.Equal(["One", "Two"], result.Insights);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"regions\":[],\"styles\":[{\"item_recid\":42,\"trend_score\":10}],\"ai_insights\":[]}")]
    public async Task Answers_without_a_usable_analysis_become_readable_errors(string modelJson)
        => await Assert.ThrowsAsync<AiServiceException>(() => Gemini(modelJson));
}
