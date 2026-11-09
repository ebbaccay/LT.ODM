using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.LibraryAssistant.Tools;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>ai.Guide in memory: guides, and their questions as the procedures keep them.</summary>
public sealed class FakeGuideRepository(FakeSavedQuestionRepository? saved = null) : IGuideRepository
{
    public List<GuideDto> Guides { get; } = [];
    public Dictionary<string, string> QuestionKeys { get; } = [];   // key -> guide code

    public Task<IReadOnlyList<GuideDto>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GuideDto>>(Guides.ToList());

    public Task SaveAsync(GuideDto guide, IReadOnlyList<GuideQuestionKey> questions, string changedBy, CancellationToken ct = default)
    {
        Guides.RemoveAll(g => g.Code == guide.Code);
        Guides.Add(guide with { ChangedBy = changedBy, ChangedHow = "Admin" });
        foreach (var k in QuestionKeys.Where(k => k.Value == guide.Code).Select(k => k.Key).ToList()) QuestionKeys.Remove(k);
        foreach (var q in questions)
        {
            QuestionKeys[q.QuestionKey] = guide.Code;
            saved?.TakeOverForGuide(q.QuestionKey, q.Question, guide.Code);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string code, CancellationToken ct = default)
    {
        if (Guides.RemoveAll(g => g.Code == code) == 0) throw new StyleRuleException(404, "The guide was not found.");
        return Task.CompletedTask;
    }
}

/// <summary>How-to guides: db/seed/ai.guides.sql, HowToGuides, HowToTool and the assistant's guide step.</summary>
public sealed partial class HowToGuideTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static readonly StyleLookupsDto Library = new(
        Customers: [new("ADI", "ADI"), new("SKE", "SKE")],
        Seasons: [new("2026-FW", "2026-FW"), new("2027-SS", "2027-SS")],
        BusinessUnits: [], ProductTypes: [new("JACKET", "JACKET")],
        WeaveTypes: [], MaterialTypes: [], ContentClasses: [], Uoms: [], Suppliers: [],
        SeasonTerms: [new("SS", "Spring/Summer"), new("FW", "Fall/Winter")], Countries: []);

    [GeneratedRegex(@"\('(?<code>[a-z_]+)', N'(?<title>(?:[^']|'')*)', N'(?<who>(?:[^']|'')*)',\s*N'(?<intro>(?:[^']|'')*)',\s*N'(?<steps>\[(?:[^']|'')*\])'\)", RegexOptions.Singleline)]
    private static partial Regex GuideRowRx();

    [GeneratedRegex(@"^\s+\(N'(?<key>(?:[^']|'')*)', N'(?<q>(?:[^']|'')*)', 'how_to', N'\{""guide"":""(?<code>[a-z_]+)""\}'\),?\s*$")]
    private static partial Regex GuideQuestionRx();

    private static string Db(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "db", "seed", file))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "db", "seed", file));
    }

    /// <summary>The seeded guides with their seeded questions, as the API reads them.</summary>
    public static List<GuideDto> SeedGuides()
    {
        var questions = Db("ai.saved-questions.sql").Split('\n').Select(l => GuideQuestionRx().Match(l.TrimEnd('\r'))).Where(m => m.Success)
            .GroupBy(m => m.Groups["code"].Value).ToDictionary(g => g.Key, g => g.Select(m => m.Groups["q"].Value.Replace("''", "'")).ToList());
        return GuideRowRx().Matches(Db("ai.guides.sql")).Select(m =>
        {
            var code = m.Groups["code"].Value;
            var steps = JsonSerializer.Deserialize<List<GuideStepDto>>(m.Groups["steps"].Value.Replace("''", "'"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            return new GuideDto(code, m.Groups["title"].Value.Replace("''", "'"), m.Groups["who"].Value.Replace("''", "'"), m.Groups["intro"].Value.Replace("''", "'"),
                steps, true, questions.GetValueOrDefault(code) ?? []);
        }).ToList();
    }

    [Fact]
    public void Seeded_guides_have_steps_with_app_links_and_their_own_questions()
    {
        // sqlcmd reads a file without BOM in the ANSI code page: curly quotes would be stored garbled. Use “ in JSON instead.
        foreach (var file in new[] { "ai.guides.sql", "ai.saved-questions.sql" })
            Assert.True(Db(file).All(ch => ch < 128), $"{file} must be plain ASCII");

        var guides = SeedGuides();
        Assert.True(guides.Count >= 12, $"{guides.Count} guides read");
        Assert.Equal(guides.Count, guides.Select(g => g.Code).Distinct().Count());
        foreach (var g in guides)
        {
            Assert.Matches(HowToGuides.CodeRx(), g.Code);
            Assert.InRange(g.Steps.Count, 1, HowToGuides.MaxSteps);
            Assert.All(g.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Text), g.Code));
            Assert.All(g.Steps.Where(s => s.Url is not null), s => Assert.True(HowToGuides.SafeUrl(s.Url!), $"{g.Code}: {s.Url}"));
            Assert.True(g.Questions.Count >= 3, $"{g.Code} has {g.Questions.Count} questions");
        }
        // Every seeded guide question points to a seeded guide.
        var codes = guides.Select(g => g.Code).ToHashSet();
        Assert.All(Db("ai.saved-questions.sql").Split('\n').Select(l => GuideQuestionRx().Match(l.TrimEnd('\r'))).Where(m => m.Success),
            m => Assert.Contains(m.Groups["code"].Value, codes));
    }

    [Theory]
    [InlineData("how do I create a package", true)]
    [InlineData("What are the steps to share a package?", true)]
    [InlineData("I want to import styles", true)]
    [InlineData("how many jackets per season", false)]
    [InlineData("show me ADI jackets", false)]
    public void How_to_questions_are_recognised(string question, bool howTo) => Assert.Equal(howTo, HowToGuides.IsHowTo(question));

    [Theory]
    [InlineData("what are the steps to share a package with my client", "share_package")]
    [InlineData("how can I put together a client profile", "client_profile")]
    [InlineData("how should we import the excel workbook", "import_styles")]
    [InlineData("how do I add styles into an existing package", "add_styles_to_package")]
    [InlineData("create_package", "create_package")]
    [InlineData("how do I do it", null)]
    [InlineData("how do I fly to the moon", null)]
    public void Words_point_to_one_guide_or_none(string question, string? code) => Assert.Equal(code, HowToGuides.Match(question, SeedGuides())?.Code);

    [Theory]
    [InlineData("/packages?new=1&customer={customer}&season={season}", "ADI", "2027-SS", "/packages?new=1&customer=ADI&season=2027-SS")]
    [InlineData("/packages?new=1&customer={customer}&season={season}", null, null, "/packages?new=1")]
    [InlineData("/styles?customer={customer}", null, null, "/styles")]
    [InlineData("/packages/offer?customer={customer}", "A&B", null, "/packages/offer?customer=A%26B")]
    public void Links_get_the_customer_and_season_or_leave_them_out(string url, string? customer, string? season, string expected)
        => Assert.Equal(expected, HowToGuides.FillUrl(url, customer, season));

    [Theory]
    [InlineData("/packages", true)]
    [InlineData("https://evil.test", false)]
    [InlineData("//evil.test", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/styles?x=a b", false)]
    public void Only_links_inside_the_app_are_allowed(string url, bool ok) => Assert.Equal(ok, HowToGuides.SafeUrl(url));

    private static (LibraryAssistantService Service, FakeAiJsonClient Text, FakeGuideRepository Guides) Assistant()
    {
        var saved = new FakeSavedQuestionRepository();
        var seed = SeedGuides();
        foreach (var g in seed)
            foreach (var q in g.Questions)
                saved.Seed(QuestionPatterns.Read(q, new Dictionary<string, string>(), Library).KeyKeepingProductTypes!, q, HowToGuides.ToolName,
                    new Dictionary<string, string> { ["guide"] = g.Code });
        var guides = new FakeGuideRepository();
        guides.Guides.AddRange(seed);
        var styles = new FakeStyleRepository { Lookups = Library };
        var text = new FakeAiJsonClient();
        return (new LibraryAssistantService(text, styles, [new StyleSearchTool(styles), new HowToTool(guides)], saved, guides: guides), text, guides);
    }

    [Fact]
    public async Task A_guide_question_gets_the_steps_with_the_customer_and_season_filled_in_without_ai()
    {
        var (assistant, text, _) = Assistant();
        var a = await assistant.AskAsync("How do I create a package for ADI in 2027-SS?", []);
        Assert.Equal(("saved", "how_to"), (a.Source, a.Tool));
        Assert.Equal(["step", "what"], a.Table!.Columns);
        Assert.Equal("/packages?new=1&customer=ADI&season=2027-SS", a.Table.RowLinks[0]);
        Assert.Equal("/packages?new=1&customer=ADI&season=2027-SS", Assert.Single(a.Links).Url);
        Assert.StartsWith("Create a style package (Admin, Merchandiser, Designer):", a.Answer);

        a = await assistant.AskAsync("how do I create a package", []);
        Assert.Equal("/packages?new=1", a.Table!.RowLinks[0]);
        Assert.Empty(text.Calls);
    }

    [Fact]
    public async Task A_how_to_wording_that_was_not_seeded_is_matched_by_its_words_without_ai()
    {
        var (assistant, text, _) = Assistant();
        var a = await assistant.AskAsync("What are the steps to share a package with my client?", []);
        Assert.Equal(("guide", "how_to"), (a.Source, a.Tool));
        Assert.Equal("share_package", a.Arguments["guide"]);
        Assert.Empty(text.Calls);
    }

    [Fact]
    public async Task An_unclear_how_to_goes_to_the_ai_which_can_pick_a_guide_shown_as_written()
    {
        var (assistant, text, _) = Assistant();
        text.Answers["a library assistant plan"] = """{ "tool": "how_to", "understood": "How to send a pitch.", "guide": "send my pitch to the buyer" }""";
        var a = await assistant.AskAsync("how do I wow the brand team", []);   // no guide stands out from these words
        Assert.Equal(("ai", "how_to", "share_package"), (a.Source, a.Tool, a.Arguments["guide"]));
        Assert.DoesNotContain(text.Calls, c => c.Purpose == "a library assistant answer");   // the steps are not reworded
        Assert.NotNull(a.SavedQuestionId);   // a thumbs up makes the wording a Rule for this guide
    }

    [Fact]
    public async Task Saving_a_guide_checks_its_steps_links_and_questions()
    {
        var (assistant, _, guides) = Assistant();
        var steps = new List<GuideStepDto> { new("Open Packages.", "/packages?new=1&customer={customer}") };
        await assistant.SaveGuideAsync("Send_Sample", new SaveGuideRequest("Ask for a sample", "Admin", null, steps, true, ["how do I ask for a sample for ADI", "sample request"]), "admin");
        var g = guides.Guides.Single(x => x.Code == "send_sample");
        Assert.Equal(2, g.Questions.Count);
        Assert.Contains("how i ask a sample", guides.QuestionKeys.Keys);   // ADI and "for" taken out: one wording answers every customer

        await Assert.ThrowsAsync<StyleRuleException>(() => assistant.SaveGuideAsync("x_bad", new SaveGuideRequest("Bad link", null, null,
            [new("Open it.", "https://evil.test")], true, []), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => assistant.SaveGuideAsync("x_empty", new SaveGuideRequest("No steps", null, null, [], true, []), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => assistant.SaveGuideAsync("x_q", new SaveGuideRequest("Only filters", null, null, steps, true, ["ADI 2027-SS"]), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => assistant.SaveGuideAsync("9bad", new SaveGuideRequest("Bad code", null, null, steps, true, []), "admin"));
    }

    [Fact]
    public async Task Readers_see_guides_only_admins_change_them()
    {
        factory.Guides.Guides.Clear();
        factory.Guides.Guides.AddRange(SeedGuides());
        var viewer = await SignInAsync("Viewer");
        var list = await viewer.GetFromJsonAsync<List<GuideDto>>("/api/v1/ai/library-assistant/guides");
        Assert.Contains(list!, g => g.Code == "create_package");
        var body = new { title = "Ask for a sample", steps = new[] { new { text = "Open Packages.", url = "/packages" } }, questions = new[] { "sample request" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/ai/library-assistant/guides/send_sample", body)).StatusCode);

        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/ai/library-assistant/guides/send_sample", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/ai/library-assistant/guides/send_sample",
            new { title = "x", steps = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/ai/library-assistant/guides/send_sample")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/v1/ai/library-assistant/guides/send_sample")).StatusCode);
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
