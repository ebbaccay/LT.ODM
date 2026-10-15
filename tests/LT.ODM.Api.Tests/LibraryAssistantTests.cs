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

public sealed class LibraryAssistantTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static readonly StyleLookupsDto Library = new(
        Customers: [new("ADI", "ADI"), new("SKE", "SKE")],
        Seasons: [new("2026-FW", "2026-FW"), new("2027-SS", "2027-SS")],
        BusinessUnits: [new("RUA", "Running A")],
        ProductTypes: [new("JACKET", "JACKET"), new("JACKETS", "JACKETS"), new("DRESS", "DRESS")],
        WeaveTypes: [new("KNT", "Knit"), new("WVN", "Woven")], MaterialTypes: [], ContentClasses: [], Uoms: [], Suppliers: [],
        SeasonTerms: [new("SS", "Spring/Summer"), new("FW", "Fall/Winter")]);

    private static (LibraryAssistantService Service, FakeAiJsonClient Text, FakeStyleRepository Styles) Assistant() => Assistant(new FakeSavedQuestionRepository());

    private static (LibraryAssistantService Service, FakeAiJsonClient Text, FakeStyleRepository Styles) Assistant(FakeSavedQuestionRepository saved)
    {
        var text = new FakeAiJsonClient();
        var styles = new FakeStyleRepository { Lookups = Library };
        return (new LibraryAssistantService(text, styles, [new StyleSearchTool(styles), new StyleCountsTool(styles)], saved), text, styles);
    }

    [Fact]
    public async Task Phrase_list_questions_are_answered_without_ai()
    {
        var (assistant, text, styles) = Assistant();
        var a = await assistant.AskAsync("women's dresses for SS27", []);
        Assert.Equal(("rules", "style_search", true), (a.Source, a.Tool, a.AnswerFromFacts));
        Assert.Equal(("FEMALE", "DRESS", "2027-SS"), (styles.LastListQuery!.Gender, styles.LastListQuery.ProductType, styles.LastListQuery.Season));
        Assert.Empty(text.Calls);
    }

    [Fact]
    public async Task Ai_picks_a_tool_its_codes_are_checked_and_invented_numbers_are_not_shown()
    {
        var (assistant, text, styles) = Assistant();
        text.Answers["a library assistant plan"] = """
            { "tool": "style_counts", "understood": "Jackets counted per season.", "productTypes": ["JACKET", "PARKA"], "customer": "nike", "groupBy": "season" }
            """;
        text.Answers["a library assistant answer"] = """{ "answer": "You have 42 jackets, mostly in 2027-SS." }""";   // 42 is not in the result

        var a = await assistant.AskAsync("how many jackets do we have per season?", []);
        Assert.Equal(("ai", "style_counts", "Jackets counted per season."), (a.Source, a.Tool, a.Understood));
        Assert.Equal("JACKET", styles.LastListQuery!.ProductType);   // PARKA is not a library code
        Assert.Null(styles.LastListQuery.Customer);                   // nor is "nike"
        Assert.True(a.AnswerFromFacts);
        Assert.StartsWith("5 styles", a.Answer);                      // the code's sentence (3 + 2 in the fake counts)
        Assert.Equal(2, a.Table!.Rows.Count);

        text.Answers["a library assistant answer"] = """{ "answer": "5 jackets: 3 in 2027-SS and 2 in 2026-FW." }""";
        a = await assistant.AskAsync("how many jackets do we have per season?", []);
        Assert.Equal(("5 jackets: 3 in 2027-SS and 2 in 2026-FW.", false), (a.Answer, a.AnswerFromFacts));
    }

    [Fact]
    public async Task Arguments_the_question_does_not_support_are_dropped()
    {
        // Seen with qwen2.5:7b: "how many jackets per season?" came back with customer ADI and every season.
        var (assistant, text, styles) = Assistant();
        text.Answers["a library assistant plan"] = """
            { "tool": "style_counts", "understood": "x", "customer": "ADI", "seasons": ["2026-FW", "2027-SS"], "productTypes": ["JACKET", "JACKETS"], "groupBy": "season" }
            """;
        var a = await assistant.AskAsync("how many jackets do we have per season?", []);
        Assert.Equal((null, null, "JACKET,JACKETS"), (styles.LastListQuery!.Customer, styles.LastListQuery.Season, styles.LastListQuery.ProductType));

        // Seen too: the model left the product type out; the phrase list read "jackets", so it is filled in.
        text.Answers["a library assistant plan"] = """{ "tool": "style_counts", "understood": "x", "groupBy": "season" }""";
        await assistant.AskAsync("how many jackets do we have per season?", []);
        Assert.Equal("JACKET,JACKETS", styles.LastListQuery!.ProductType);
        text.Answers["a library assistant plan"] = """
            { "tool": "style_counts", "understood": "x", "customer": "ADI", "seasons": ["2026-FW", "2027-SS"], "productTypes": ["JACKET", "JACKETS"], "groupBy": "season" }
            """;

        // A follow-up keeps what the earlier turn used; a season the question names is kept.
        a = await assistant.AskAsync("and for SS27?", [new LibraryTurnDto("adidas jackets", "style_counts", new Dictionary<string, string> { ["customer"] = "ADI" })]);
        Assert.Equal(("ADI", "2027-SS"), (styles.LastListQuery!.Customer, styles.LastListQuery.Season));
    }

    [Fact]
    public async Task Questions_outside_the_tools_or_without_ai_get_a_plain_reply()
    {
        var (assistant, text, _) = Assistant();
        text.Answers["a library assistant plan"] = """{ "tool": "none", "understood": "A weather question." }""";
        var a = await assistant.AskAsync("what is the weather today?", []);
        Assert.Equal(("none", null), (a.Tool, a.Table));
        Assert.Contains("only look things up", a.Answer);

        text.IsConfigured = false;
        a = await assistant.AskAsync("what is the weather today?", []);
        Assert.Equal("none", a.Source);
        a = await assistant.AskAsync("men's parkas", []);   // understood in part: searched, the rest listed
        Assert.Equal(("rules", "parkas"), (a.Source, Assert.Single(a.Unmatched)));
    }

    [Fact]
    public void Numbers_in_an_answer_must_be_in_the_result()
    {
        Assert.True(LibraryAssistantService.NumbersBacked("1,003 styles, 42.5% recycled", """{"total":1003,"share":42.5}"""));
        Assert.False(LibraryAssistantService.NumbersBacked("about 1,100 styles", """{"total":1003}"""));
        Assert.False(LibraryAssistantService.Usable("The provided JSON data contains details about various materials."));   // seen with qwen2.5:7b
        Assert.True(LibraryAssistantService.Usable("S2808MR0000 uses a recycled polyester ripstop shell."));
    }

    [Fact]
    public async Task Readers_ask_factories_cannot_and_questions_are_checked()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).GetAsync("/api/v1/ai/library-assistant/tools")).StatusCode);
        var viewer = await SignInAsync("Viewer");
        var tools = await viewer.GetFromJsonAsync<List<LibraryToolInfoDto>>("/api/v1/ai/library-assistant/tools");
        Assert.Contains(tools!, t => t.Name == "style_search");
        Assert.Equal(tools!.Count, tools.Select(t => t.Name).Distinct().Count());
        Assert.All(tools, t => Assert.All(t.Parameters, p => Assert.Contains(p, LibraryToolArgs.Catalog.Keys)));
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsJsonAsync("/api/v1/ai/library-assistant/ask", new { question = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsJsonAsync("/api/v1/ai/library-assistant/ask", new { question = "men's jackets", history = Array.Empty<object>() })).StatusCode);
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "la" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}
