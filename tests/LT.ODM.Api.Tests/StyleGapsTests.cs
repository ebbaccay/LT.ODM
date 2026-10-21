using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakeStyleGapRepository : IStyleGapRepository
{
    public List<(string Action, string? Field, object? Request, string? ChangedBy)> Writes { get; } = [];

    /// <summary>Styles the AI step is asked about (two by default).</summary>
    public List<GapStyleToAsk> ToAsk { get; set; } =
    [
        new(1, "P226M001", "LONG PANTS", null, "KNT", null, "LONG PANTS", 30),
        new(2, "K272ST09", "ROUND NECK PULLOVER", null, "KNT", null, "PULLOVER", 30),
    ];

    public List<GapAiAnswer> SavedAnswers { get; } = [];

    public Task<StyleGapSummaryDto> SummaryAsync(bool withSuggestions, CancellationToken ct = default)
        => Task.FromResult(new StyleGapSummaryDto(10, 4, withSuggestions ? 3 : null, withSuggestions ? 2 : null, 1, withSuggestions ? 1 : null,
            withSuggestions ? 0 : null, 2, 5, 6, 100, 99, 0, 7));

    public GapListQuery? LastList { get; private set; }

    public Task<GapPageDto> ListAsync(GapListQuery query, CancellationToken ct = default)
    {
        LastList = query;
        return Task.FromResult(new GapPageDto([], 0, new Dictionary<string, int> { ["3"] = 2 }));
    }

    public Task<int> AcceptAsync(AcceptGapsRequest request, string field, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("accept", field, request, changedBy));
        return Task.FromResult(request.Items?.Count ?? 2);
    }

    public Task<int> DismissAsync(string field, IReadOnlyList<GapValueDto> items, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("dismiss", field, items, changedBy));
        return Task.FromResult(items.Count);
    }

    public Task<IReadOnlyList<GapStyleToAsk>> ToAskAsync(string field, string? customer, string? season, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GapStyleToAsk>>(ToAsk.Take(take).ToList());

    public Task<int> SaveAiAsync(string field, IReadOnlyList<GapAiAnswer> answers, string provider, string model, string createdBy, CancellationToken ct = default)
    {
        SavedAnswers.AddRange(answers);
        return Task.FromResult(answers.Count);
    }

    public Task<IReadOnlyList<GapSeasonDto>> SeasonsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GapSeasonDto>>([new("FA26", 2026, "FA", "2026-FA", 194, 0, true), new("26Q2", 2026, null, null, 105, 0, false)]);

    public Task<MergeSeasonResultDto> MergeSeasonAsync(string from, string to, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("merge", null, (from, to), changedBy));
        return Task.FromResult(new MergeSeasonResultDto(194, 0));
    }

    public Task<IReadOnlyList<LcoLineDto>> LcoForStyleAsync(int styleId, bool withAllowance, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LcoLineDto>>(
            [new(9, 1, "M1", "Fabric", "FAB", "yd", 1.2m, 0.03m, withAllowance ? 1.236m : 1.2m, withAllowance ? "BrandAllowance" : "Brand", null)]);

    public Task<IReadOnlyList<LcoGroupDto>> LcoSummaryAsync(bool withAllowance, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LcoGroupDto>>([new("SKE", "Skechers", "2027-SS", 10, 10, 2)]);

    public Task<ApplyLcoResultDto> ApplyLcoAsync(ApplyLcoRequest request, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("lco", null, request, changedBy));
        return Task.FromResult(new ApplyLcoResultDto(10, 2));
    }

    public Task<IReadOnlyList<SimilarStyleDto>> SimilarAsync(int styleId, int top, CancellationToken ct = default)
        => styleId == 404
            ? throw new StyleRuleException(404, "This style no longer exists.")
            : Task.FromResult<IReadOnlyList<SimilarStyleDto>>(
                [new(2, "ADI", "adidas", "2027-SS", "S2", null, null, "JACKET", "MALE", "WVN", null, null, true, true, true, 12, 2, 81, "100% PES")]);
}

public sealed class StyleGapsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "gap" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Readers_see_the_gaps_but_only_editors_change_them()
    {
        var viewer = await SignInAsync("Viewer");
        var summary = await viewer.GetFromJsonAsync<StyleGapSummaryDto>("/api/v1/style-gaps/summary");
        Assert.Equal(3, summary!.GenderSuggested);   // the page asks for the suggestion counts
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/style-gaps/gender?show=none&minConfidence=9&take=999")).StatusCode);
        Assert.Equal(new GapListQuery("gender", null, null, "none", 3, 0, 200), factory.StyleGaps.LastList);

        var items = new[] { new { styleId = 1, value = "FEMALE" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/style-gaps/gender/accept", new { items })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/style-gaps/seasons/merge", new { from = "FA26", to = "2026-FA" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/style-gaps/lco/apply", new { all = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).GetAsync("/api/v1/style-gaps/summary")).StatusCode);
    }

    [Fact]
    public async Task Accepting_and_dismissing_check_the_field_and_the_items()
    {
        var merch = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.NotFound, (await merch.PostAsJsonAsync("/api/v1/style-gaps/colour/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-gaps/gender/dismiss", new { items = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await merch.PostAsJsonAsync("/api/v1/style-gaps/gender/accept", new { items = Enumerable.Range(1, 1001).Select(i => new { styleId = i, value = "MALE" }) })).StatusCode);

        var ok = await merch.PostAsJsonAsync("/api/v1/style-gaps/PRODUCTTYPE/accept", new { customer = " SKE ", minConfidence = 3 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var write = factory.StyleGaps.Writes.Last(w => w.Action == "accept");
        Assert.Equal("productType", write.Field);
        Assert.Equal("SKE", ((AcceptGapsRequest)write.Request!).Customer);
    }

    [Fact]
    public async Task Season_merges_and_lco_fills_need_a_target_and_a_scope()
    {
        var merch = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-gaps/seasons/merge", new { from = "FA26" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await merch.PostAsJsonAsync("/api/v1/style-gaps/seasons/merge", new { from = "FA26", to = "2026-FA" })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-gaps/lco/apply", new { withAllowance = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await merch.PostAsJsonAsync("/api/v1/style-gaps/lco/apply", new { customer = "SKE", season = "2027-SS" })).StatusCode);
        var lines = await merch.GetFromJsonAsync<List<LcoLineDto>>("/api/v1/style-gaps/lco/styles/5?withAllowance=true");
        Assert.Equal("BrandAllowance", lines!.Single().Source);
    }

    [Fact]
    public async Task Similar_styles_are_read_by_every_library_reader()
    {
        var viewer = await SignInAsync("Viewer");
        var similar = await viewer.GetFromJsonAsync<List<SimilarStyleDto>>("/api/v1/styles/7/similar");
        Assert.True(similar!.Single().SameBrand);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/styles/404/similar")).StatusCode);
    }

    [Fact]
    public async Task Ai_answers_are_kept_only_for_styles_sent_and_values_on_the_list()
    {
        var text = new FakeAiJsonClient();
        text.Answers["style genders"] = """
            { "items": [
                { "styleId": 1, "value": "male", "reason": "M in the style number" },
                { "styleId": 2, "value": "-", "reason": "nothing shows it" },
                { "styleId": 2, "value": "FEMALE", "reason": "second answer for the same style" },
                { "styleId": 99, "value": "FEMALE", "reason": "not sent" }
            ] }
            """;
        var gaps = new FakeStyleGapRepository();
        var result = await new GapAdvisor(text, gaps).AskNextAsync(GapFields.Gender, null, null, "tester");

        Assert.Equal(new AskAiGapsResultDto(1, 1, 28), result);
        Assert.Equal(["MALE", "-"], gaps.SavedAnswers.Select(a => a.Value));
        Assert.DoesNotContain("SKE", text.Calls.Single().UserText);   // no customer is sent

        text.Answers["style garments"] = """{ "items": [ { "styleId": 1, "value": "TROUSERS", "reason": "not a category" }, { "styleId": 2, "value": "pullover", "reason": "" } ] }""";
        gaps.SavedAnswers.Clear();
        await new GapAdvisor(text, gaps).AskNextAsync(GapFields.ProductType, null, null, "tester");
        Assert.Equal("PULLOVER", gaps.SavedAnswers.Single().Value);
    }

    [Fact]
    public async Task Asking_ai_needs_the_text_job()
    {
        var merch = await SignInAsync("Merchandiser");
        factory.TextAi.IsConfigured = false;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await merch.PostAsJsonAsync("/api/v1/style-gaps/gender/ask-ai", new { })).StatusCode);
        }
        finally
        {
            factory.TextAi.IsConfigured = true;
        }
    }
}
