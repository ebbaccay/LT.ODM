using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Auth;
using LT.ODM.Application.CostOptimization;
using LT.ODM.Infrastructure.Ai;
using LT.ODM.Infrastructure.Tms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LT.ODM.Api.Tests;

public sealed class FakeCostSuggestionAi : ICostSuggestionAi
{
    public bool IsConfigured { get; set; } = true;

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(IsConfigured);
    public bool Fail { get; set; }

    public Task<IReadOnlyList<CostSuggestionDto>> SuggestAsync(CostSuggestionRequest request, CancellationToken ct = default)
        => Fail
            ? throw new AiServiceException("The AI service could not be reached. Try again in a minute.")
            : Task.FromResult<IReadOnlyList<CostSuggestionDto>>([new("Fabric", "Use 10oz recycled twill", "Lighter weight", 8.10m, 7.40m, 0.70m)]);
}

public sealed class CostOptimizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static readonly object Style = new
    {
        styleName = "Utility Jogger", currentFob = 16.10, targetFob = 15.50, fabricCost = 8.10, fabricDesc = "11oz twill", trimCost = 1.20,
        trimDesc = "", laborCost = 5.40, overheadCost = 0.40, marginCost = 1.00, bomSummary = "", cmtSummary = "",
    };

    private async Task<HttpClient> SignInAsync()
    {
        var name = "co" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, ["Merchandiser"]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Suggestions_need_sign_in_valid_costs_and_a_configured_ai()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/cost-optimization/suggestions", Style)).StatusCode);

        var client = await SignInAsync();
        var ok = await client.PostAsJsonAsync("/api/v1/cost-optimization/suggestions", Style);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Fabric", (await ok.Content.ReadFromJsonAsync<CostSuggestionDto[]>())![0].Category);

        var noFob = await client.PostAsJsonAsync("/api/v1/cost-optimization/suggestions", new { styleName = "x", currentFob = 0, fabricCost = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, noFob.StatusCode);

        factory.CostAi.Fail = true;
        try { Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync("/api/v1/cost-optimization/suggestions", Style)).StatusCode); }
        finally { factory.CostAi.Fail = false; }

        factory.CostAi.IsConfigured = false;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/v1/cost-optimization/suggestions", Style)).StatusCode); }
        finally { factory.CostAi.IsConfigured = true; }
    }

    private sealed class StubHandler(string modelJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = modelJson } } } } } })),
            });
    }

    private static Task<IReadOnlyList<CostSuggestionDto>> Gemini(string modelJson)
        => new CostSuggestionAi(TestAiText.Gemini(new StubHandler(modelJson)))
            .SuggestAsync(new CostSuggestionRequest("Jogger", 16.10m, 15.50m, 8.10m, "twill", 1.20m, "", 5.40m, 0.40m, 1.00m, "", ""));

    [Fact]
    public async Task Unusable_ai_ideas_are_dropped_and_savings_recomputed()
    {
        var ideas = await Gemini("""
            [
              { "category": "fabric", "title": "Use 10oz twill", "detail": "d", "fromPrice": 8.10, "toPrice": 7.40, "saving": 9.99 },
              { "category": "Marketing", "title": "Unknown category", "detail": "", "fromPrice": 1, "toPrice": 0.5, "saving": 0.5 },
              { "category": "Trim", "title": "Price goes up", "detail": "", "fromPrice": 1.20, "toPrice": 1.50, "saving": -0.3 },
              { "category": "Labor", "title": "", "detail": "no title", "fromPrice": 5.4, "toPrice": 5.0, "saving": 0.4 },
              { "category": "Overhead", "title": "Saves more than it costs", "detail": "", "fromPrice": 9, "toPrice": 0, "saving": 9 }
            ]
            """);
        var only = Assert.Single(ideas);
        Assert.Equal("Fabric", only.Category);
        Assert.Equal(0.70m, only.Saving);   // fromPrice - toPrice, not the model's 9.99
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"title\":\"an object, not a list\"}")]
    [InlineData("[]")]
    public async Task Answers_without_usable_ideas_become_readable_errors(string modelJson)
        => await Assert.ThrowsAsync<AiServiceException>(() => Gemini(modelJson));

    // ----- Procedure bindings -----

    private static ClaimsPrincipal User()
        => new(new ClaimsIdentity([new Claim("preferred_username", "mchan"), new Claim("location", "HKG"), new Claim("user_group", "MER")], "test", "preferred_username", "role"));

    private List<Microsoft.Data.SqlClient.SqlParameter> Bind(string proc, string json, string[] declared)
    {
        using var doc = JsonDocument.Parse(json);
        var catalog = factory.Services.GetRequiredService<TmsProcedureCatalog>();
        return TmsParameterBinder.Bind(catalog.Resolve(proc, User())!, declared, doc.RootElement, User());
    }

    [Fact]
    public void Review_actors_come_from_the_token_but_the_target_factory_comes_from_the_screen()
    {
        var mirror = Bind("web_rd_co_ins_sbu_review", """{ "submitted_by": "x", "location": "DG01", "user_group": "x" }""", ["@submitted_by", "@location", "@user_group"]);
        Assert.Equal("mchan", mirror.Single(p => p.ParameterName == "@submitted_by").Value);
        // @location here is the factory the review is sent to, not the sender's location.
        Assert.Equal("DG01", mirror.Single(p => p.ParameterName == "@location").Value);
        Assert.Equal("MER", mirror.Single(p => p.ParameterName == "@user_group").Value);

        var submit = Bind("web_rd_co_submit_review", """{ "session_id": 4, "submitted_by": "x", "location": "x" }""", ["@session_id", "@submitted_by", "@location"]);
        Assert.Equal("mchan", submit.Single(p => p.ParameterName == "@submitted_by").Value);
        Assert.Equal("HKG", submit.Single(p => p.ParameterName == "@location").Value);

        var recall = Bind("web_rd_co_recall_review", """{ "session_id": 4, "recalled_by": "x" }""", ["@session_id", "@recalled_by"]);
        Assert.Equal("mchan", recall.Single(p => p.ParameterName == "@recalled_by").Value);

        var session = Bind("web_rd_co_load_session", """{ "gq_qid": "Q1", "created_by": "x", "location": "x" }""", ["@gq_qid", "@created_by", "@location"]);
        Assert.Equal("mchan", session.Single(p => p.ParameterName == "@created_by").Value);
        Assert.Equal("HKG", session.Single(p => p.ParameterName == "@location").Value);
    }
}
