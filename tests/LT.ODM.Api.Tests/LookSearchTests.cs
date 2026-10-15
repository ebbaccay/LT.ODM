using LT.ODM.Application.Abstractions;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Api.Tests;

/// <summary>Smart search: phrase list, then the built-in image model (only when a picture stands out), then the AI.</summary>
public sealed class LookSearchTests
{
    private static readonly StyleLookupsDto Library = new([new("ADI", "ADI")], [new("2027-SS", "2027-SS")], [],
        [new("JACKET", "JACKET"), new("DRESS", "DRESS")], [], [], [], [], [], [new("SS", "Spring/Summer")]);

    private sealed class NoImages : IStyleImageStore
    {
        public Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default) => Task.FromResult("x.png");
        public (Stream Content, string ContentType)? Open(string fileName) => null;
    }

    private static (StyleAiAssistant Assistant, FakeAiJsonClient Text, FakeImageSearchRepository Pictures, FakeStyleRepository Styles) Build()
    {
        var text = new FakeAiJsonClient();
        var styles = new FakeStyleRepository { Lookups = Library };
        var pictures = new FakeImageSearchRepository { Similarity = 0.10m };
        var model = new FakeImageEmbeddingModel { IsAvailable = true };
        var resolver = new FakeAiConnectionResolver();
        var assistant = new StyleAiAssistant(text, new FakeAiImageClient(), styles, new FakeStyleAiRepository(), new NoImages(), resolver,
            new LookSearch(model, pictures, resolver));
        return (assistant, text, pictures, styles);
    }

    [Fact]
    public async Task A_request_about_looks_is_answered_from_the_pictures_without_the_ai_server()
    {
        var (assistant, text, pictures, _) = Build();
        pictures.Stats = new ImageVectorStatsDto(56, 0.01, 0.025);   // the match (0.10) is 3.6 spreads above the average

        var r = await assistant.SearchAsync("denim top with button front", useAi: true);
        Assert.Equal(("look", "S2708MR0230"), (r.Source, Assert.Single(r.Results.Items).StyleNo));
        Assert.Empty(text.Calls);

        // "Search with AI instead": the look step is skipped.
        text.Answers["a style search"] = """{ "explanation": "Tops." }""";
        Assert.Equal("ai", (await assistant.SearchAsync("denim top with button front", useAi: true, skipLook: true)).Source);
    }

    [Fact]
    public async Task Requests_that_are_not_about_looks_go_on_to_the_ai()
    {
        var (assistant, text, pictures, _) = Build();
        text.Answers["a style search"] = """{ "explanation": "Styles from a vendor." }""";

        // Every picture scores about the same ("styles from vendor ABC"): nothing stands out.
        pictures.Stats = new ImageVectorStatsDto(56, 0.07, 0.02);
        Assert.Equal("ai", (await assistant.SearchAsync("styles from vendor ABC", useAi: true)).Source);

        // Too few pictures to tell: skipped as well.
        pictures.Stats = new ImageVectorStatsDto(8, 0.01, 0.025);
        Assert.Equal("ai", (await assistant.SearchAsync("styles from vendor ABC", useAi: true)).Source);
    }

    [Fact]
    public async Task Look_matches_must_also_meet_what_the_phrase_list_understood()
    {
        var (assistant, text, pictures, _) = Build();
        pictures.Stats = new ImageVectorStatsDto(56, 0.01, 0.025);
        text.Answers["a style search"] = """{ "explanation": "Dresses." }""";

        // The phrase list read "dresses"; the fake library lists no style for it, so the picture match (a jacket) is not used.
        Assert.Equal("ai", (await assistant.SearchAsync("dresses with a floral print", useAi: true)).Source);
    }

    [Fact]
    public async Task When_every_word_is_understood_but_nothing_matches_the_pictures_are_searched()
    {
        var (assistant, text, pictures, _) = Build();
        pictures.Stats = new ImageVectorStatsDto(139, 0.01, 0.025);

        // "navy dresses": the phrase list understands it all, but the fake library has no such style.
        var r = await assistant.SearchAsync("navy dresses", useAi: true);
        Assert.Equal(("look", true), (r.Source, r.PhraseListFoundNothing));
        Assert.Empty(text.Calls);
    }

    [Fact]
    public void A_hooded_sweatshirt_is_filed_as_hooded_sweat()
    {
        var l = new StyleLookupsDto([], [], [], [new("HOODED SWEAT", "HOODED SWEAT"), new("SWEATSHIRTLS", "SWEATSHIRT (LONG SLEEVE)")], [], [], [], [], [], []);
        Assert.Equal(["HOODED SWEAT"], StyleQueryParser.Parse("navy hooded sweatshirt with a print", l).Filters.ProductTypes);
        Assert.Equal(["HOODED SWEAT", "SWEATSHIRTLS"], StyleQueryParser.Parse("sweatshirts", l).Filters.ProductTypes.Order());
    }

    [Fact]
    public async Task The_library_assistant_answers_questions_about_looks_from_the_pictures_before_the_ai()
    {
        var text = new FakeAiJsonClient();
        var styles = new FakeStyleRepository { Lookups = Library };
        var pictures = new FakeImageSearchRepository { Similarity = 0.10m };
        var resolver = new FakeAiConnectionResolver();
        var looks = new LookSearch(new FakeImageEmbeddingModel { IsAvailable = true }, pictures, resolver);
        // The picture step needs the look-alike tool registered (the step builds the answer itself).
        var assistant = new LT.ODM.Application.LibraryAssistant.LibraryAssistantService(text, styles,
            [new LT.ODM.Application.LibraryAssistant.Tools.StyleSearchTool(styles), new LookAlikeToolStub()], new FakeSavedQuestionRepository(), looks);

        pictures.Stats = new ImageVectorStatsDto(139, 0.01, 0.025);
        var a = await assistant.AskAsync("a denim top with a button front", []);
        Assert.Equal(("look", "look_alike", "S2708MR0230"), (a.Source, a.Tool, a.Table!.Rows[0][0]));
        Assert.Empty(text.Calls);

        // Nothing stands out ("who supplies ..."): the AI is asked.
        pictures.Stats = new ImageVectorStatsDto(139, 0.07, 0.02);
        text.Answers["a library assistant plan"] = """{ "tool": "none", "understood": "x" }""";
        Assert.Equal("ai", (await assistant.AskAsync("who supplies our denim", [])).Source);
    }

    private sealed class LookAlikeToolStub : LT.ODM.Application.LibraryAssistant.ILibraryTool
    {
        public string Name => "look_alike";
        public string Description => "";
        public string Example => "";
        public IReadOnlyList<string> Parameters => ["look"];
        public Task<LT.ODM.Application.LibraryAssistant.LibraryToolResult> RunAsync(LT.ODM.Application.LibraryAssistant.LibraryToolArgs args, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
