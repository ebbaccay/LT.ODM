using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.LibraryAssistant.Tools;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>ai.Synonym in memory.</summary>
public sealed class FakeSynonymRepository : ISynonymRepository
{
    public List<SynonymDto> Rows { get; } = [];

    public Task<IReadOnlyList<SynonymDto>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SynonymDto>>(Rows.ToList());

    public Task SaveAsync(string word, string kind, string value, string changedBy, CancellationToken ct = default)
    {
        var i = Rows.FindIndex(r => r.Word == word);
        if (i >= 0) Rows[i] = Rows[i] with { Kind = kind, Value = value, ChangedBy = changedBy };
        else Rows.Add(new SynonymDto(Rows.Count == 0 ? 1 : Rows.Max(r => r.SynonymId) + 1, word, kind, value, changedBy, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int synonymId, CancellationToken ct = default)
    {
        if (Rows.RemoveAll(r => r.SynonymId == synonymId) == 0) throw new StyleRuleException(404, "The synonym was not found.");
        return Task.CompletedTask;
    }
}

/// <summary>Teaching the assistant: synonyms read before every question, and unanswered questions for Admins to review.</summary>
public sealed class AssistantReviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static readonly StyleLookupsDto Library = new(
        Customers: [new("ADI", "ADI"), new("SKE", "SKE")],
        Seasons: [new("2026-FW", "2026-FW"), new("2027-SS", "2027-SS")],
        BusinessUnits: [new("RUA", "Running A")], ProductTypes: [new("JACKET", "JACKET"), new("TOP", "TOP")],
        WeaveTypes: [new("KNT", "Knit"), new("WVN", "Woven")], MaterialTypes: [], ContentClasses: [], Uoms: [], Suppliers: [],
        SeasonTerms: [new("SS", "Spring/Summer"), new("FW", "Fall/Winter")], Countries: []);

    private sealed record Setup(LibraryAssistantService Assistant, FakeAiJsonClient Text, FakeSavedQuestionRepository Saved, FakeGuideRepository Guides,
        FakeSynonymRepository Synonyms, FakeStyleRepository Styles);

    private static Setup Make(StyleLookupsDto? lookups = null)
    {
        var saved = new FakeSavedQuestionRepository();
        var guides = new FakeGuideRepository(saved);
        guides.Guides.AddRange(HowToGuideTests.SeedGuides());
        var synonyms = new FakeSynonymRepository();
        var styles = new FakeStyleRepository { Lookups = lookups ?? Library };
        var text = new FakeAiJsonClient();
        var assistant = new LibraryAssistantService(text, styles, [new StyleSearchTool(styles), new StyleCountsTool(styles), new HowToTool(guides)], saved,
            guides: guides, synonyms: synonyms);
        return new Setup(assistant, text, saved, guides, synonyms, styles);
    }

    // ----- Synonyms -----

    [Fact]
    public void Synonyms_replace_whole_words_plurals_too_longest_first()
    {
        List<SynonymDto> list = [new(1, "tee", SynonymKinds.ProductType, "TOP"), new(2, "gents", SynonymKinds.Gender, "MALE"),
            new(3, "puffer jacket", SynonymKinds.ProductType, "JACKET"), new(4, "puffer", SynonymKinds.Words, "padded")];
        var (q, readAs) = Synonyms.Apply("Gents Tees and puffer jackets, no puffers? steep", list, Library);
        Assert.Equal("men TOP and JACKET, no padded? steep", q);   // "steep" keeps its "tee"
        Assert.Equal(["puffer jacket → JACKET", "puffer → padded", "gents → MALE", "tee → TOP"], readAs);
        Assert.Equal(("nothing here", 0), (Synonyms.Apply("nothing here", list, Library).Question, Synonyms.Apply("nothing here", list, Library).ReadAs.Count));
    }

    [Fact]
    public async Task A_synonym_is_read_by_the_phrase_list_and_shown_with_the_answer()
    {
        var s = Make();
        await s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("Puffers", "productType", "jacket"), "admin");   // stored as "puffers" / JACKET
        await s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("gents", "gender", "male"), "admin");
        var a = await s.Assistant.AskAsync("gents puffers for ADI", []);
        Assert.Equal(("rules", "style_search"), (a.Source, a.Tool));
        Assert.Equal(("JACKET", "MALE", "ADI"), (s.Styles.LastListQuery!.ProductType, s.Styles.LastListQuery.Gender, s.Styles.LastListQuery.Customer));
        Assert.Equal(["puffers → JACKET", "gents → MALE"], a.ReadAs!);
        Assert.Empty(s.Text.Calls);
    }

    [Fact]
    public async Task A_words_synonym_helps_find_a_guide()
    {
        var s = Make();
        await s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("line plan", "words", "package"), "admin");
        var a = await s.Assistant.AskAsync("how do I build a line plan", []);
        Assert.Equal(("how_to", "create_package"), (a.Tool, a.Arguments["guide"]));
    }

    [Fact]
    public async Task Synonyms_are_checked_before_they_are_saved()
    {
        var s = Make();
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("tees", "productType", "NOPE"), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("tees", "colour", "TOP"), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("a b c d e", "productType", "TOP"), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("pitch", "words", "Pitch"), "admin"));
        await s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("  Tees ", "productType", "top"), "admin");
        await s.Assistant.SaveSynonymAsync(new SaveSynonymRequest("tees", "productType", "JACKET"), "admin");   // same word: changed, not added
        Assert.Equal(("tees", "JACKET"), (s.Synonyms.Rows.Single().Word, s.Synonyms.Rows.Single().Value));
    }

    // ----- Pictures -----

    private static readonly StyleLookupsDto WithTees = Library with
    {
        ProductTypes = [.. Library.ProductTypes, new("TSHIRTSL", "T-SHIRT (SHORT SLEEVE)")],
    };

    [Theory]
    [InlineData("product tees with images", true)]
    [InlineData("tees pictures only", true)]
    [InlineData("ADI jackets that have a sketch", true)]
    [InlineData("jackets without pictures", false)]
    [InlineData("jackets with no picture", false)]
    [InlineData("jackets without a photo", false)]
    [InlineData("jackets", null)]
    public void The_phrase_list_reads_with_and_without_pictures(string question, bool? hasPicture)
    {
        var r = StyleQueryParser.Parse(question, WithTees);
        Assert.Equal(hasPicture, r.Filters.HasPicture);
        Assert.True(r.Complete, string.Join(",", r.Unmatched));
    }

    [Fact]
    public void With_and_without_pictures_in_one_question_is_left_to_the_ai()
        => Assert.False(StyleQueryParser.Parse("jackets with pictures and without pictures", WithTees).Complete);

    [Fact]
    public async Task Product_tees_with_images_is_answered_by_the_phrase_list_without_ai()
    {
        var s = Make(WithTees);
        var a = await s.Assistant.AskAsync("product tees with images", []);
        Assert.Equal(("rules", "style_search", "with"), (a.Source, a.Tool, a.Arguments["pictures"]));
        Assert.Equal((true, "TSHIRTSL"), (s.Styles.LastListQuery!.HasPicture, s.Styles.LastListQuery.ProductType));
        Assert.Empty(s.Text.Calls);
    }

    [Fact]
    public void Links_to_the_styles_list_show_the_same_styles()
    {
        LibraryToolArgs Args(string? pictures) => LibraryToolArgs.FromDisplay(pictures is null
            ? new Dictionary<string, string> { ["customer"] = "ADI" }
            : new Dictionary<string, string> { ["customer"] = "ADI", ["pictures"] = pictures }, Library);
        Assert.Equal("/styles?customer=ADI", Args("with").StylesUrl());               // the list's default: with pictures
        Assert.Equal("/styles?customer=ADI&pictures=all", Args(null).StylesUrl());   // all styles, as the answer counted them
        Assert.Equal((true, false), (Args("with").ListQuery(5).HasPicture, Args("without").ListQuery(5).HasPicture));
    }

    [Fact]
    public void Picture_words_of_a_guide_question_stay_part_of_it()
    {
        var reading = QuestionPatterns.Read("how do I search by picture", new Dictionary<string, string>(), Library);
        Assert.Equal(("how i search", "how i search picture"), (reading.Key, reading.KeyKeepingProductTypes));
        Assert.Empty(reading.FiltersKeepingProductTypes);
        Assert.Contains("pictures", reading.Filters);   // "how many styles per season with pictures" fits the counting pattern this way
    }

    // ----- Unanswered questions -----

    [Fact]
    public async Task Questions_nobody_could_answer_are_kept_for_review_and_counted()
    {
        var s = Make();
        s.Text.Answers["a library assistant plan"] = """{ "tool": "none", "understood": "Not about the library." }""";
        await s.Assistant.AskAsync("what is the weather in Manila?", []);
        await s.Assistant.AskAsync("What is the weather in Manila", []);
        var row = s.Saved.Rows.Single();
        Assert.Equal((SavedQuestionStatuses.Unanswered, "none", 2), (row.Status, row.Tool, row.AskCount));

        // A follow-up depends on the earlier turn: not kept.
        await s.Assistant.AskAsync("and in Hanoi?", [new LibraryTurnDto("weather", "none", new Dictionary<string, string>())]);
        Assert.Single(s.Saved.Rows);

        // AI off and nothing understood: kept too.
        s.Text.IsConfigured = false;
        await s.Assistant.AskAsync("who designed our logo", []);
        Assert.Equal(2, s.Saved.Rows.Count(r => r.Status == SavedQuestionStatuses.Unanswered));
    }

    [Fact]
    public async Task An_unanswered_question_added_to_a_guide_opens_it_from_then_on()
    {
        var s = Make();
        s.Text.Answers["a library assistant plan"] = """{ "tool": "none", "understood": "?" }""";
        await s.Assistant.AskAsync("how do we get the buyer excited about our range", []);
        var id = s.Saved.Rows.Single().SavedQuestionId;

        await s.Assistant.AddToGuideAsync(id, new SavedQuestionToGuideRequest("share_package"), "admin");
        Assert.Contains("how do we get the buyer excited about our range", s.Guides.Guides.Single(g => g.Code == "share_package").Questions);
        Assert.DoesNotContain(s.Saved.Rows, r => r.SavedQuestionId == id);   // left the unanswered list

        var calls = s.Text.Calls.Count;
        var a = await s.Assistant.AskAsync("How do we get the buyer excited about our range?", []);
        Assert.Equal(("saved", "share_package", calls), (a.Source, a.Arguments["guide"], s.Text.Calls.Count));
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.AddToGuideAsync(id, new SavedQuestionToGuideRequest("share_package"), "admin"));
    }

    [Fact]
    public async Task An_unanswered_question_is_answered_by_choosing_a_look_up_and_its_filters()
    {
        var s = Make();
        s.Text.Answers["a library assistant plan"] = """{ "tool": "none", "understood": "?" }""";
        await s.Assistant.AskAsync("give me the season split", []);
        var id = s.Saved.Rows.Single().SavedQuestionId;

        // A Rule needs a look-up, and only real ones.
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.SaveAsync(id, new SaveSavedQuestionRequest("Rule", null), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.SaveAsync(id, new SaveSavedQuestionRequest("Rule", null, "drop_tables"), "admin"));
        await Assert.ThrowsAsync<StyleRuleException>(() => s.Assistant.SaveAsync(id, new SaveSavedQuestionRequest("Unanswered", null), "admin"));

        await s.Assistant.SaveAsync(id, new SaveSavedQuestionRequest("Rule", new Dictionary<string, string> { ["groupBy"] = "season" }, "style_counts"), "admin");
        var calls = s.Text.Calls.Count;
        var a = await s.Assistant.AskAsync("give me the season split", []);
        Assert.Equal(("saved", "style_counts", calls), (a.Source, a.Tool, s.Text.Calls.Count));

        // Ignore = Removed: asked again, it is counted but stays ignored.
        await s.Assistant.SaveAsync(id, new SaveSavedQuestionRequest("Removed", null), "admin");
        s.Text.Answers["a library assistant plan"] = """{ "tool": "none", "understood": "?" }""";
        await s.Assistant.AskAsync("give me the season split", []);
        Assert.Equal(SavedQuestionStatuses.Removed, s.Saved.Rows.Single().Status);
    }

    [Fact]
    public async Task Only_admins_keep_synonyms_and_review_questions()
    {
        factory.Synonyms.Rows.Clear();
        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/ai/library-assistant/synonyms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/ai/library-assistant/synonyms", new { word = "tees", kind = "words", value = "t-shirt" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/ai/library-assistant/saved/1/to-guide", new { guide = "create_package" })).StatusCode);

        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/ai/library-assistant/synonyms", new { word = "tees", kind = "words", value = "t-shirt" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/ai/library-assistant/synonyms", new { word = "", kind = "words", value = "x" })).StatusCode);
        var list = await admin.GetFromJsonAsync<List<SynonymDto>>("/api/v1/ai/library-assistant/synonyms");
        var id = Assert.Single(list!).SynonymId;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/ai/library-assistant/synonyms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/ai/library-assistant/synonyms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/ai/library-assistant/saved?status=Unanswered")).StatusCode);
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
