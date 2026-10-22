using System.Text.Json;
using System.Text.RegularExpressions;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.LibraryAssistant.Tools;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>Seeded question patterns (db/seed/ai.saved-questions.sql, QuestionPatterns).</summary>
public sealed partial class LibraryAssistantSeedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly StyleLookupsDto Library = new(
        Customers: [new("ADI", "ADI"), new("SKE", "SKE")],
        Seasons: [new("2026-FW", "2026-FW"), new("2027-SS", "2027-SS")],
        BusinessUnits: [new("RUA", "Running A"), new("TO", "TomWell Other Merch Team")],
        ProductTypes: [new("JACKET", "JACKET"), new("JACKETS", "JACKETS"), new("DRESS", "DRESS"), new("TRACKTOP", "TRACK TOP")],
        WeaveTypes: [new("KNT", "Knit"), new("WVN", "Woven")], MaterialTypes: [], ContentClasses: [], Uoms: [], Suppliers: [],
        SeasonTerms: [new("SS", "Spring/Summer"), new("FW", "Fall/Winter")]);

    /// <summary>A tool that only records the arguments it was given.</summary>
    private sealed class StubTool(string name, IReadOnlyList<string> parameters) : ILibraryTool
    {
        public LibraryToolArgs? Last { get; private set; }
        public string Name => name;
        public string Description => name;
        public string Example => name;
        public IReadOnlyList<string> Parameters => parameters;

        public Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
        {
            Last = a;
            return Task.FromResult(new LibraryToolResult("ok", new LibraryTableDto(["x"], [["1"]], [null], 1), [], new { }));
        }
    }

    private sealed record SeedRow(string Key, string Question, string Tool, IReadOnlyDictionary<string, string> Arguments);

    [GeneratedRegex(@"^\s+\(N'(?<key>(?:[^']|'')*)', N'(?<q>(?:[^']|'')*)', '(?<tool>\w+)', N'(?<args>(?:[^']|'')*)'\),?\s*$")]
    private static partial Regex SeedRowRx();

    private static IReadOnlyList<SeedRow> SeedRows()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "db", "seed", "ai.saved-questions.sql"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadLines(Path.Combine(dir.FullName, "db", "seed", "ai.saved-questions.sql"))
            .Select(l => SeedRowRx().Match(l)).Where(m => m.Success)
            .Select(m => new SeedRow(m.Groups["key"].Value.Replace("''", "'"), m.Groups["q"].Value.Replace("''", "'"), m.Groups["tool"].Value,
                JsonSerializer.Deserialize<Dictionary<string, string>>(m.Groups["args"].Value.Replace("''", "'"))!))
            .ToList();
    }

    /// <summary>A seed wording as someone would type it, and the codes in it the library has.</summary>
    private static (string Typed, Dictionary<string, string> Kinds) Typed(string wording)
    {
        var typed = wording.Replace("{productType}", "jackets").Replace("{styleNo}", "26FA_M_JK01").Replace("{fromStyleNo}", "F2308MR1600")
            .Replace("{materialCode}", "1003401");
        return (typed, new(StringComparer.OrdinalIgnoreCase) { ["26FA_M_JK01"] = "styleNo", ["F2308MR1600"] = "styleNo", ["1003401"] = "materialCode" });
    }

    [Fact]
    public void Every_seed_key_is_the_key_its_wording_reads_as_and_uses_a_real_tool_and_its_arguments()
    {
        var rows = SeedRows();
        Assert.True(rows.Count >= 130, $"{rows.Count} seed rows read");
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct().Count());

        using var scope = factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetServices<ILibraryTool>().ToDictionary(t => t.Name);
        foreach (var r in rows)
        {
            var (typed, kinds) = Typed(r.Question);
            var reading = QuestionPatterns.Read(typed, kinds, Library);
            var keepingProductTypes = !r.Question.Contains("{productType}");
            Assert.Equal(r.Key, keepingProductTypes ? reading.KeyKeepingProductTypes : reading.Key);
            Assert.True(tools.TryGetValue(r.Tool, out var tool), r.Tool);
            Assert.All(r.Arguments.Keys, a => Assert.Contains(a, tool!.Parameters));
            Assert.True(QuestionPatterns.Apply(r.Arguments, keepingProductTypes, reading, tool!.Parameters, Library) is not null, r.Question);   // its own filters fit its tool
        }
    }

    private static (LibraryAssistantService Service, FakeAiJsonClient Text, FakeSavedQuestionRepository Saved, Dictionary<string, StubTool> Tools) Assistant()
    {
        var saved = new FakeSavedQuestionRepository();
        foreach (var r in SeedRows()) saved.Seed(r.Key, r.Question, r.Tool, r.Arguments);
        var stubs = new[]
        {
            new StubTool("supplier_share", ["customer", "seasons"]), new StubTool("style_detail", ["styleNo"]),
            new StubTool("material_lookup", ["materialCode", "material", "customer", "seasons"]), new StubTool("compare_styles", ["styleNo", "fromStyleNo"]),
            new StubTool("style_counts", ["customer", "seasons", "productTypes", "businessUnits", "gender", "weaveType", "material", "looks", "keywords", "groupBy"]),
            new StubTool("standard_trims", ["productTypes", "customer", "seasons"]),
        }.ToDictionary(t => t.Name);
        var styles = new FakeStyleRepository { Lookups = Library, Totals = q => q.Search is "26FA_M_JK01" or "F2308MR1600" ? 1 : 0 };
        var text = new FakeAiJsonClient();
        return (new LibraryAssistantService(text, styles, stubs.Values, saved, materials: new FakeMaterialRepository()), text, saved, stubs);
    }

    [Fact]
    public async Task A_pattern_answers_every_customer_and_season_without_ai()
    {
        var (assistant, text, _, tools) = Assistant();
        var a = await assistant.AskAsync("Who are our main suppliers for ADI in 2027-SS?", []);
        Assert.Equal(("saved", "supplier_share"), (a.Source, a.Tool));
        Assert.Equal(("ADI", "2027-SS"), (tools["supplier_share"].Last!.Customer, Assert.Single(tools["supplier_share"].Last!.Seasons)));

        a = await assistant.AskAsync("who are our main suppliers?", []);
        Assert.Equal("saved", a.Source);
        Assert.Null(tools["supplier_share"].Last!.Customer);

        a = await assistant.AskAsync("top suppliers SKE", []);   // "top" is also a product type: the key that keeps it as a word matches
        Assert.Equal(("saved", "SKE"), (a.Source, tools["supplier_share"].Last!.Customer));
        Assert.Empty(text.Calls);
    }

    [Fact]
    public async Task A_filter_the_tool_does_not_take_is_left_to_the_ai()
    {
        var (assistant, text, _, _) = Assistant();
        await assistant.AskAsync("main suppliers for women", []);   // suppliers have no gender
        Assert.Contains(text.Calls, c => c.Purpose == "a library assistant plan");
    }

    [Fact]
    public async Task Counting_by_a_dimension_ignores_a_filter_on_it_and_keeps_the_others()
    {
        var (assistant, _, _, tools) = Assistant();
        await assistant.AskAsync("men vs women styles", []);
        Assert.Equal(("gender", null), (tools["style_counts"].Last!.GroupBy, tools["style_counts"].Last!.Gender));

        await assistant.AskAsync("how many jackets per season for SKE", []);
        var args = tools["style_counts"].Last!;
        Assert.Equal(("season", "SKE"), (args.GroupBy, args.Customer));
        Assert.Contains("JACKET", args.ProductTypes);
    }

    [Fact]
    public async Task Style_numbers_and_material_codes_fill_their_slots_as_typed()
    {
        var (assistant, _, _, tools) = Assistant();
        var a = await assistant.AskAsync("What fabric does 26FA_M_JK01 use?", []);   // the phrase list alone would read "26fa" as a season
        Assert.Equal(("saved", "26FA_M_JK01"), (a.Source, tools["style_detail"].Last!.StyleNo));

        a = await assistant.AskAsync("show 26FA_M_JK01", []);   // before the phrase list's one-row style search
        Assert.Equal("style_detail", a.Tool);

        await assistant.AskAsync("compare 26FA_M_JK01 with F2308MR1600", []);
        Assert.Equal(("26FA_M_JK01", "F2308MR1600"), (tools["compare_styles"].Last!.StyleNo, tools["compare_styles"].Last!.FromStyleNo));

        a = await assistant.AskAsync("where is 70038500 used", []);   // digits only: the fake material list has it
        Assert.Equal(("material_lookup", "70038500"), (a.Tool, tools["material_lookup"].Last!.MaterialCode));
    }

    [Fact]
    public async Task A_thumbs_up_on_a_pattern_answer_keeps_the_patterns_own_arguments()
    {
        var (assistant, _, saved, _) = Assistant();
        var a = await assistant.AskAsync("main suppliers for ADI", []);
        await assistant.FeedbackAsync(new LibraryFeedbackRequest(a.SavedQuestionId!.Value, true, a.Tool, a.Arguments), "tester");
        var row = saved.Rows.Single(r => r.SavedQuestionId == a.SavedQuestionId);
        Assert.Empty(row.Arguments);   // not customer ADI: the pattern still answers every customer
    }
}
