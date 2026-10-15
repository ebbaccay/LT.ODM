using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.LibraryAssistant.Tools;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>ai.SavedQuestion in memory, with the procedures' rules.</summary>
public sealed class FakeSavedQuestionRepository : ISavedQuestionRepository
{
    public List<SavedQuestionDto> Rows { get; } = [];
    private readonly Dictionary<int, string> keys = [];

    public Task<SavedRuleDto?> UseRuleAsync(string questionKey, CancellationToken ct = default)
    {
        var i = Rows.FindIndex(r => keys[r.SavedQuestionId] == questionKey && r.Status == SavedQuestionStatuses.Rule);
        if (i < 0) return Task.FromResult<SavedRuleDto?>(null);
        Rows[i] = Rows[i] with { RuleHits = Rows[i].RuleHits + 1 };
        return Task.FromResult<SavedRuleDto?>(new SavedRuleDto(Rows[i].SavedQuestionId, Rows[i].Question, Rows[i].Tool, Rows[i].Arguments));
    }

    public Task<int> RecordAsync(string questionKey, string question, string tool, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        var i = Rows.FindIndex(r => keys[r.SavedQuestionId] == questionKey);
        if (i < 0)
        {
            var id = Rows.Count + 1;
            keys[id] = questionKey;
            Rows.Add(new SavedQuestionDto(id, question, tool, arguments, SavedQuestionStatuses.Recorded, 1, 0, DateTime.UtcNow, DateTime.UtcNow, null, null, null));
            return Task.FromResult(id);
        }
        var r = Rows[i];
        Rows[i] = r.Status == SavedQuestionStatuses.Rule ? r with { AskCount = r.AskCount + 1 } : r with { AskCount = r.AskCount + 1, Tool = tool, Arguments = arguments };
        return Task.FromResult(r.SavedQuestionId);
    }

    public Task FeedbackAsync(int savedQuestionId, bool helpful, string tool, IReadOnlyDictionary<string, string> arguments, string changedBy, CancellationToken ct = default)
    {
        var i = Find(savedQuestionId);
        Rows[i] = helpful
            ? Rows[i] with { Status = SavedQuestionStatuses.Rule, Tool = tool, Arguments = arguments, ChangedBy = changedBy, ChangedHow = "Feedback" }
            : Rows[i] with { Status = SavedQuestionStatuses.Removed, ChangedBy = changedBy, ChangedHow = "Feedback" };
        return Task.CompletedTask;
    }

    public Task<PagedResult<SavedQuestionDto>> ListAsync(string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        var list = Rows.Where(r => status is null || r.Status == status).ToList();
        return Task.FromResult(new PagedResult<SavedQuestionDto>(list.Skip(skip).Take(take).ToList(), list.Count));
    }

    public Task<IReadOnlyList<string>> PopularAsync(int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Rows.Where(r => r.Status == SavedQuestionStatuses.Rule).Take(take).Select(r => r.Question).ToList());

    public Task SaveAsync(int savedQuestionId, string status, IReadOnlyDictionary<string, string> arguments, string changedBy, CancellationToken ct = default)
    {
        var i = Find(savedQuestionId);
        Rows[i] = Rows[i] with { Status = status, Arguments = arguments, ChangedBy = changedBy, ChangedHow = "Admin" };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int savedQuestionId, CancellationToken ct = default)
    {
        Rows.RemoveAt(Find(savedQuestionId));
        return Task.CompletedTask;
    }

    private int Find(int id)
        => Rows.FindIndex(r => r.SavedQuestionId == id) is var i and >= 0 ? i : throw new StyleRuleException(404, "The saved question was not found.");
}

public sealed class SavedQuestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static readonly StyleLookupsDto Library = new(
        Customers: [new("ADI", "ADI")], Seasons: [new("2027-SS", "2027-SS")], BusinessUnits: [],
        ProductTypes: [new("JACKET", "JACKET"), new("DRESS", "DRESS")],
        WeaveTypes: [], MaterialTypes: [], ContentClasses: [], Uoms: [], Suppliers: [], SeasonTerms: [new("SS", "Spring/Summer")]);

    private const string Plan = """{ "tool": "style_counts", "understood": "Jackets per season.", "productTypes": ["JACKET"], "groupBy": "season" }""";

    [Fact]
    public void The_same_question_written_differently_has_the_same_key()
    {
        Assert.Equal(LibraryAssistantService.QuestionKey("How many Jackets per season?"), LibraryAssistantService.QuestionKey("how many jacket per season"));
        Assert.Equal(LibraryAssistantService.QuestionKey("Men's jackets by season"), LibraryAssistantService.QuestionKey("mens jacket by season!"));
        Assert.NotEqual(LibraryAssistantService.QuestionKey("men's jackets by season"), LibraryAssistantService.QuestionKey("women's jackets by season"));
    }

    [Fact]
    public async Task A_thumbs_up_answers_the_question_again_without_ai_and_a_thumbs_down_brings_ai_back()
    {
        var saved = new FakeSavedQuestionRepository();
        var text = new FakeAiJsonClient();
        var styles = new FakeStyleRepository { Lookups = Library };
        var assistant = new LibraryAssistantService(text, styles, [new StyleSearchTool(styles), new StyleCountsTool(styles)], saved);
        text.Answers["a library assistant plan"] = Plan;

        // The AI answers; the question is recorded, not yet a rule.
        var first = await assistant.AskAsync("How many jackets per season?", []);
        Assert.Equal(("ai", 2), (first.Source, text.Calls.Count));
        var id = first.SavedQuestionId!.Value;
        Assert.Equal(SavedQuestionStatuses.Recorded, saved.Rows.Single().Status);

        // Thumbs up with what was shown: the same question, written differently, is answered at once - even with AI off.
        await assistant.FeedbackAsync(new LibraryFeedbackRequest(id, true, first.Tool, first.Arguments), "merch");
        text.IsConfigured = false;
        var again = await assistant.AskAsync("how many JACKET per season", []);
        Assert.Equal(("saved", "style_counts", id), (again.Source, again.Tool, again.SavedQuestionId));
        Assert.Equal("JACKET", styles.LastListQuery!.ProductType);
        Assert.Equal(2, text.Calls.Count);   // no new AI call
        Assert.Equal(1, saved.Rows.Single().RuleHits);

        // Thumbs down on the saved answer: removed, so the AI is asked next time.
        text.IsConfigured = true;
        await assistant.FeedbackAsync(new LibraryFeedbackRequest(id, false, again.Tool, again.Arguments), "merch");
        Assert.Equal("ai", (await assistant.AskAsync("How many jackets per season?", [])).Source);
    }

    [Fact]
    public async Task Follow_ups_and_relative_dates_are_not_saved()
    {
        var saved = new FakeSavedQuestionRepository();
        var text = new FakeAiJsonClient();
        var styles = new FakeStyleRepository { Lookups = Library };
        var assistant = new LibraryAssistantService(text, styles, [new StyleSearchTool(styles), new StyleCountsTool(styles)], saved);
        text.Answers["a library assistant plan"] = Plan;

        Assert.Null((await assistant.AskAsync("how many jackets this year?", [])).SavedQuestionId);
        Assert.Null((await assistant.AskAsync("and per customer?", [new LibraryTurnDto("jackets", "style_counts", new Dictionary<string, string>())])).SavedQuestionId);
        Assert.Empty(saved.Rows);
    }

    [Fact]
    public void Saved_arguments_are_checked_like_the_ais()
    {
        var a = LibraryToolArgs.FromDisplay(new Dictionary<string, string>
        {
            ["productTypes"] = "JACKET, PARKA", ["customer"] = "NIKE", ["seasons"] = "2027-SS", ["hack"] = "x",
        }, Library);
        Assert.Equal(new Dictionary<string, string> { ["seasons"] = "2027-SS", ["productTypes"] = "JACKET" }, a.Display());
    }

    [Fact]
    public async Task Readers_give_feedback_only_admins_manage_the_list()
    {
        factory.SavedQuestions.Rows.Clear();
        await factory.SavedQuestions.RecordAsync("jacket per season", "Jackets per season?", "style_counts", new Dictionary<string, string> { ["groupBy"] = "season" });
        var id = factory.SavedQuestions.Rows.Single().SavedQuestionId;

        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.NoContent, (await viewer.PostAsJsonAsync("/api/v1/ai/library-assistant/feedback",
            new { savedQuestionId = id, helpful = true, tool = "style_counts", arguments = new Dictionary<string, string> { ["groupBy"] = "season" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/ai/library-assistant/saved")).StatusCode);
        Assert.Contains("Jackets per season?", (await viewer.GetFromJsonAsync<List<string>>("/api/v1/ai/library-assistant/popular"))!);

        var admin = await SignInAsync("Admin");
        var list = await admin.GetFromJsonAsync<PagedResult<SavedQuestionDto>>("/api/v1/ai/library-assistant/saved?status=Rule");
        Assert.Equal(("viewer", 1), (list!.Items.Single().ChangedBy![..6], list.Total));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/v1/ai/library-assistant/saved/{id}", new { status = "Bogus" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/ai/library-assistant/saved/{id}", new { status = "Removed", arguments = new { groupBy = "customer" } })).StatusCode);
        Assert.Equal(("Removed", "customer"), (factory.SavedQuestions.Rows.Single().Status, factory.SavedQuestions.Rows.Single().Arguments["groupBy"]));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/ai/library-assistant/saved/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/ai/library-assistant/saved/{id}")).StatusCode);
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = role.ToLowerInvariant() + Guid.NewGuid().ToString("N")[..6];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}
