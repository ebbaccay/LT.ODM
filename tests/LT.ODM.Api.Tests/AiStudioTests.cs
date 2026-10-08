using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Auth;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>Answers with the JSON set for the request's purpose ("a style search", ...); records what was sent.</summary>
public sealed class FakeAiJsonClient : IAiJsonClient
{
    public bool IsConfigured { get; set; } = true;
    public Dictionary<string, string> Answers { get; } = [];
    public List<(string Purpose, string UserText)> Calls { get; } = [];

    public Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new AiProviderInfo("Fake", "fake-text", IsConfigured, LeavesNetwork: false));

    public Task<string> GenerateJsonAsync(string purpose, string systemInstruction, string userText, JsonObject schema, double temperature, CancellationToken ct = default)
    {
        Calls.Add((purpose, userText));
        return Task.FromResult(Answers.TryGetValue(purpose, out var a) ? a : "{}");
    }
}

public sealed class FakeAiImageClient : IAiImageClient
{
    /// <summary>A real 1x1 PNG: renders are decoded and made smaller before they are stored.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public bool IsConfigured { get; set; } = true;
    public List<(string Prompt, ReferenceImage? Reference)> Calls { get; } = [];

    public Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default)
        => Task.FromResult(new AiProviderInfo("Fake", "fake-image", IsConfigured, LeavesNetwork: false, SupportsReferenceImage: true));

    public Task<GeneratedImage> GenerateImageAsync(string purpose, string prompt, ReferenceImage? reference, CancellationToken ct = default)
    {
        Calls.Add((prompt, reference));
        return Task.FromResult(new GeneratedImage(Png, ImageKind.Png));
    }
}

public sealed class FakeStyleAiRepository : IStyleAiRepository
{
    public List<StyleRenderDto> Renders { get; } = [];

    public Task<BomCheckDto> RunBomCheckAsync(BomCheckQuery query, int maxRows, CancellationToken ct = default)
        => Task.FromResult(new BomCheckDto(1, 3, 2, [new BomCheckRuleCountDto("LcoBrandGap", "High", 1, 1), new BomCheckRuleCountDto("NoSupplier", "Low", 1, 1)],
        [
            Finding(101, "LcoBrandGap", "High", 1.9m, 1.2m),
            Finding(102, "NoSupplier", "Low", null, null),
        ]));

    private static BomCheckFindingDto Finding(int id, string rule, string severity, decimal? value, decimal? refValue)
        => new(id, 7, "S1", "2027-SS", "ADI", id - 100, 10, $"M{id}", "Fleece", "FAB", "yd", value, refValue, rule, severity, value, refValue, null, null, null);

    public Task<IReadOnlyList<StyleRenderDto>> ListRendersAsync(int styleId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StyleRenderDto>>(Renders.Where(r => r.StyleId == styleId).ToList());

    public Task<StyleRenderDto> AddRenderAsync(int styleId, int? colorwayId, string imageUrl, string prompt, string provider, string model, bool usedSketch,
        string createdBy, CancellationToken ct = default)
    {
        var r = new StyleRenderDto(Renders.Count + 1, styleId, colorwayId, null, imageUrl, prompt, provider, model, usedSketch, createdBy, DateTime.UtcNow);
        Renders.Add(r);
        return Task.FromResult(r);
    }

    public ConceptCriteriaDto? LastCriteria { get; private set; }

    public Task<IReadOnlyList<ConceptCandidateDto>> ConceptCandidatesAsync(ConceptCriteriaDto criteria, int take, CancellationToken ct = default)
    {
        LastCriteria = criteria;
        return Task.FromResult<IReadOnlyList<ConceptCandidateDto>>(
        [
            new ConceptCandidateDto(233, "F2308MR1600_SS27", "2027-SS", "ADI", "OTR JACKET M", null, "JACKET", "JACKET", "MALE", "WVN", null, null, 14,
                "RIPSTOP", "100% Recycle PES, Ripstop", "ZIPPER, TIECORD(YDS)", 2),
            new ConceptCandidateDto(84, "SMSURUBER26LPM1", "2026-FW", "ADI", "BER26 LEG JKT M", null, "JACKET", "JACKET", "MALE", "KNT", null, null, 13,
                "RIPSTOP", "100% Recycled Polyester, Ripstop", "ZIPPER", 1),
        ]);
    }

    public Task DeleteRenderAsync(int styleId, int renderId, CancellationToken ct = default)
        => Renders.RemoveAll(r => r.StyleId == styleId && r.RenderId == renderId) > 0
            ? Task.CompletedTask
            : throw new StyleRuleException(404, "The render was not found. It may have been deleted.");
}

public sealed class AiStudioTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "ai" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    // ----- Endpoints -----

    [Fact]
    public async Task Readers_use_ai_studio_factories_do_not_and_only_editors_render()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync("/api/v1/ai/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).GetAsync("/api/v1/ai/status")).StatusCode);

        var viewer = await SignInAsync("Viewer");
        var status = await viewer.GetFromJsonAsync<AiStatusDto>("/api/v1/ai/status");
        Assert.Equal(("Fake", false), (status!.Text.Provider, status.Text.LeavesNetwork));
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/ai/bom-check?season=2027-SS")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/ai/style-render/7")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/ai/style-render/7", new { useSketch = false })).StatusCode);

        var merchandiser = await SignInAsync("Merchandiser");
        var response = await merchandiser.PostAsJsonAsync("/api/v1/ai/style-render/7", new { prompt = "A plain grey adidas hoodie", useSketch = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var render = await response.Content.ReadFromJsonAsync<StyleRenderDto>();
        Assert.StartsWith("/api/v1/styles/images/", render!.ImageUrl);
        Assert.DoesNotContain("adidas", render.Prompt, StringComparison.OrdinalIgnoreCase);   // brand words never reach the image model
        Assert.Equal(HttpStatusCode.OK, (await merchandiser.GetAsync(render.ImageUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merchandiser.DeleteAsync($"/api/v1/ai/style-render/7/renders/{render.RenderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await merchandiser.DeleteAsync($"/api/v1/ai/style-render/7/renders/{render.RenderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await merchandiser.GetAsync("/api/v1/ai/style-render/404")).StatusCode);
    }

    [Fact]
    public async Task Ai_calls_check_input_and_answer_503_when_no_provider_is_set_up()
    {
        var client = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/ai/style-search", new { query = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/ai/style-render/7", new { prompt = new string('a', 2001), useSketch = false })).StatusCode);
        // A style that was not reused from another needs a style to compare with.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/ai/style-compare?styleId=7")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/ai/style-compare?styleId=7&fromStyleId=8")).StatusCode);

        factory.TextAi.IsConfigured = false;
        factory.ImageAi.IsConfigured = false;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/v1/ai/style-search", new { query = "mens jackets" })).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/v1/ai/bom-check/explain", new { season = "2027-SS" })).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/v1/ai/style-render/7", new { useSketch = false })).StatusCode);
            // The rules still work without AI.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/ai/bom-check")).StatusCode);
        }
        finally
        {
            factory.TextAi.IsConfigured = true;
            factory.ImageAi.IsConfigured = true;
        }
    }

    // ----- Assistant: AI answers are checked -----

    private static StyleAiAssistant Assistant(FakeAiJsonClient text, FakeStyleRepository styles)
        => new(text, new FakeAiImageClient(), styles, new FakeStyleAiRepository(), new NoImages(), new FakeAiConnectionResolver());

    private sealed class NoImages : IStyleImageStore
    {
        public Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default) => Task.FromResult("0".PadLeft(32, '0') + ".png");
        public (Stream Content, string ContentType)? Open(string fileName) => null;
    }

    [Fact]
    public async Task Search_keeps_only_library_codes()
    {
        var text = new FakeAiJsonClient();
        text.Answers["a style search"] = """
            { "customer": "adi", "seasons": ["2099-XX"], "productTypes": ["JACKET", "GOWN"], "gender": "male", "material": "recycled",
              "keywords": "", "explanation": "Men's adidas jackets in recycled fabric." }
            """;
        var styles = new FakeStyleRepository();
        var result = await Assistant(text, styles).SearchAsync("adidas mens jackets recycled");

        Assert.Equal("ADI", result.Filters.Customer);      // the library's spelling
        Assert.Empty(result.Filters.Seasons);              // not a library season
        Assert.Empty(result.Filters.ProductTypes);         // the fake library has no product types
        Assert.Equal("MALE", result.Filters.Gender);
        Assert.Null(result.Filters.Search);
        Assert.Equal(("ADI", "recycled", "MALE"), (styles.LastListQuery!.Customer, styles.LastListQuery.Material, styles.LastListQuery.Gender));
    }

    [Fact]
    public async Task Explanations_only_point_at_findings_that_were_sent()
    {
        var text = new FakeAiJsonClient();
        text.Answers["a BOM check explanation"] = """
            { "summary": "One real gap.",
              "priorities": [ { "bomLineId": 101, "why": "LCO 1.9 vs brand 1.2.", "action": "Recheck the marker." },
                              { "bomLineId": 999, "why": "Invented.", "action": "x" } ],
              "ruleNotes": [ { "ruleCode": "NoSupplier", "note": "Fill in SAP vendors." }, { "ruleCode": "PeerOutlier", "note": "Not present." } ] }
            """;
        var assistant = Assistant(text, new FakeStyleRepository());
        var result = await assistant.ExplainAsync(await assistant.BomCheckAsync(new BomCheckQuery(7, null, null)));

        Assert.Equal(101, Assert.Single(result.Priorities).BomLineId);
        Assert.Equal("NoSupplier", Assert.Single(result.RuleNotes).RuleCode);
    }

    [Fact]
    public async Task Concept_matches_keep_library_codes_and_only_candidate_styles()
    {
        var text = new FakeAiJsonClient();
        text.Answers["concept criteria"] = """
            { "customer": "adi", "productTypes": ["JACKET", "CAPE"], "gender": "male", "weaveType": "WVN",
              "keywords": ["rip-stop", "recycle", "a"], "materialTypes": ["ZIP", "TELEPORTER"], "explanation": "Men's woven ripstop jackets." }
            """;
        text.Answers["concept matches"] = """
            { "matches": [
                { "styleId": 233, "fit": 140, "why": "Same recycled ripstop shell.", "reuse": "Pattern and shell.", "differs": "Needs a packable hood." },
                { "styleId": 999, "fit": 90, "why": "Invented.", "reuse": "-", "differs": "-" },
                { "styleId": 84, "fit": 70, "why": "Ripstop but knit body.", "reuse": "Trims.", "differs": "Woven shell." }
            ] }
            """;
        var repo = new FakeStyleAiRepository();
        var styles = new FakeStyleRepository();
        var result = await new ConceptMatcher(text, styles, repo).MatchAsync(new ConceptMatchRequest(
            "SS28 Trail", "adidas", "SS28", "EU", "18", ["Sustainable"], "Light men's woven running jacket", ["Trail jacket - $18"], ["Recycled ripstop"], []));

        Assert.Equal("ADI", result.Criteria.Customer);
        Assert.Equal("MALE", result.Criteria.Gender);
        Assert.Equal(["RIP-STOP", "RECYCLE"], result.Criteria.Keywords);           // upper case, too-short words dropped
        Assert.Empty(result.Criteria.MaterialTypes);                                  // not on the (fake) library lists
        Assert.Same(result.Criteria, repo.LastCriteria);
        Assert.Equal([233, 84], result.Matches.Select(m => m.Style.StyleId));      // the invented style is dropped
        Assert.Equal(100, result.Matches[0].Fit);                                   // clamped
        Assert.Equal(2, result.CandidatesScored);
    }

    [Fact]
    public async Task Concept_matches_need_a_drafted_concept_and_a_reader()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).PostAsJsonAsync("/api/v1/ai/concept-matches", new { name = "x", brief = "y" })).StatusCode);
        var client = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/ai/concept-matches", new { name = "Only a name" })).StatusCode);
    }

    private sealed class FakeSpecs : IMaterialSpecRepository
    {
        public List<MaterialSpecReading> Saved { get; } = [];

        public Task<MaterialSpecPageDto> ListAsync(MaterialSpecQuery query, CancellationToken ct = default)
            => Task.FromResult(new MaterialSpecPageDto(new MaterialSpecCountsDto(0, 0, 0, 0, 0, 0), [], 0));
        public Task<MaterialSpecDto?> GetAsync(int materialId, CancellationToken ct = default) => Task.FromResult<MaterialSpecDto?>(null);

        public Task<IReadOnlyList<MaterialToReadDto>> ToReadAsync(string? contentClass, string? materialType, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MaterialToReadDto>>(
            [
                new MaterialToReadDto(1, "F1", "70% COTTON 30% RECYCLED POLYESTER,SOLID FLEECE, 320GSM", "FAB", "KNIT", 40),
                new MaterialToReadDto(2, "F2", "96%REC.POLYESTER 4%SPANDEX ,1X1 RIB", "FAB", "RIBB", 40),
            ]);

        public Task<int> SaveAsync(IReadOnlyList<MaterialSpecReading> readings, string provider, string model, string readBy, CancellationToken ct = default)
        {
            Saved.AddRange(readings);
            return Task.FromResult(readings.Count);
        }

        public Task<int> ReviewAsync(IReadOnlyList<int> materialIds, string status, string reviewedBy, CancellationToken ct = default) => Task.FromResult(materialIds.Count);
    }

    [Fact]
    public async Task Material_readings_are_checked_before_they_are_stored()
    {
        var text = new FakeAiJsonClient();
        text.Answers["material descriptions"] = """
            { "items": [
                { "materialId": 1, "fibres": [ { "fibre": "COTTON", "percent": 70, "recycled": false }, { "fibre": "polyester", "percent": 30, "recycled": true } ],
                  "construction": "Fleece", "weightGsm": 320, "widthCm": 9999, "contentClass": "fab", "confidence": 0.95 },
                { "materialId": 2, "fibres": [ { "fibre": "Polyester", "percent": 96, "recycled": true }, { "fibre": "Elastane", "percent": 14, "recycled": false } ],
                  "construction": "1x1 rib", "contentClass": "XYZ", "confidence": 0.9 },
                { "materialId": 77, "fibres": [], "confidence": 1 }
            ] }
            """;
        var specs = new FakeSpecs();
        var result = await new MaterialReader(text, specs).ReadNextAsync(new ReadMaterialSpecsRequest("FAB", null), "tester");

        Assert.Equal((2, 38), (result.Read, result.Remaining));
        var fleece = specs.Saved.Single(s => s.MaterialId == 1);
        Assert.Equal("Cotton 70% · Recycled polyester 30%", fleece.Composition);
        Assert.Equal((30m, 320m, (decimal?)null, "FAB"), (fleece.RecycledPct!.Value, fleece.WeightGsm!.Value, fleece.WidthCm, fleece.SuggestedContentClass));
        Assert.Contains("Width", fleece.Notes);                                      // out-of-range width left out, noted
        var rib = specs.Saved.Single(s => s.MaterialId == 2);
        Assert.Contains("110%", rib.Notes);                                          // percentages do not add up
        Assert.Equal(0.5m, rib.Confidence);                                          // ... so confidence is capped
        Assert.Null(rib.SuggestedContentClass);                                      // not a content class
        Assert.DoesNotContain(specs.Saved, s => s.MaterialId == 77);                 // not sent
    }

    // ----- Rules -----

    private static BomLineDto Line(int id, string cc, int? part, string material, decimal? lco, decimal? brand, string? type = null, string? desc = null,
        string? supplier = null, params BomLineColorwayDto[] colours)
        => new(id, id, part, material, desc ?? material, type, null, cc, null, null, null, null, supplier, lco, brand, "yd", null, colours, "x", DateTime.UtcNow,
            null, null, []);

    private static StyleDetailDto Detail(int id, string styleNo, ColorwayDto[] colorways, params BomLineDto[] lines)
        => new(new StyleHeaderDto(id, "ADI", "adidas", "2027-SS", styleNo, styleNo, null, null, "RUN JKT", "KNT", "Knit", "JACKET", "JACKET", "MALE",
                null, null, null, null, null, null, null, "x", DateTime.UtcNow, null, null, []),
            colorways, lines, []);

    private static ColorwayDto Colorway(int id, string code, string? name, string status = "INRANGE")
        => new(id, (short)id, code, name, status, null, "x", DateTime.UtcNow, null, null, []);

    [Fact]
    public void Compare_pairs_lines_and_colorways_and_reports_what_changed()
    {
        var from = Detail(1, "S1_FW26", [Colorway(1, "KG1", "BLACK"), Colorway(2, "KG2", "WHITE"), Colorway(3, "KG3", "RED")],
            Line(1, "FAB", 10, "FLEECE-1", null, 1.775m, supplier: "TOP SPORTS"),
            Line(2, "FAB", 20, "RIB-1", null, 0.171m),
            Line(3, "ACC", 200, "ZIP-OLD", 1, 1),
            Line(4, "LNP", 800, "LABEL", 1, 0));
        var to = Detail(2, "S1_SS27", [Colorway(4, "KG1", "BLACK"), Colorway(5, "JX9", "WHITE"), Colorway(6, "KG4", "NAVY")],
            Line(1, "FAB", 10, "FLEECE-1", null, 1.9m, supplier: "TOP SPORTS"),
            Line(2, "FAB", 25, "RIB-1", null, 0.171m),
            Line(3, "ACC", 200, "ZIP-NEW", 1, 1),
            Line(4, "TRI", 991, "THREAD", 110, 110));

        var diff = StyleComparer.Compare(from, to, "CarryOver");

        Assert.Equal(1, diff.UnchangedColorways);
        Assert.Contains(diff.Colorways, c => c is { Change: "Recoded", ColorwayCode: "JX9", FromCode: "KG2" });
        Assert.Contains(diff.Colorways, c => c is { Change: "Added", ColorwayCode: "KG4" });
        Assert.Contains(diff.Colorways, c => c is { Change: "Removed", ColorwayCode: "KG3" });

        var fleece = Assert.Single(diff.BomLines, b => b.MaterialCode == "FLEECE-1");
        Assert.Equal(new FieldChangeDto("brandConsumption", "1.775", "1.9"), Assert.Single(fleece.Fields));
        Assert.Equal("partNo", Assert.Single(Assert.Single(diff.BomLines, b => b.MaterialCode == "RIB-1").Fields).Field);
        Assert.Contains(diff.BomLines, b => b is { Change: "Swapped", MaterialCode: "ZIP-NEW", FromMaterialCode: "ZIP-OLD" });
        Assert.Contains(diff.BomLines, b => b is { Change: "Added", MaterialCode: "THREAD" });
        Assert.Contains(diff.BomLines, b => b is { Change: "Removed", MaterialCode: "LABEL" });
        Assert.Equal(0, diff.UnchangedLines);
    }

    [Fact]
    public void Render_facts_use_visible_parts_and_drop_brands_and_yarn_specs()
    {
        var black = new BomLineColorwayDto(1, "A0", "CORE BLACK");
        var d = Detail(1, "S1", [Colorway(1, "KG1", "CORE BLACK")],
            Line(1, "FAB", 10, "F1", null, 1.7m, desc: "70% COTTON 30% RECYCLED POLYESTER,SOLID FLEECE,32s/1 cotton + 75D/36F PET-REC", colours: black),
            Line(2, "ACC", 200, "Z1", 1, 1, type: "ZIP", colours: new BomLineColorwayDto(1, "W", "WHITE")),
            Line(3, "ACC", 210, "H1", 1, 1, type: "HT TR", desc: "\"ADIDAS\" HEAT-TRANSFER"),
            Line(4, "LNP", 800, "L1", 1, 0, type: "LBL", desc: "ADIDAS CARE LABEL"),
            Line(5, "TRI", 991, "T1", 110, 110, type: "TRI", desc: "COATS THREAD"));

        var facts = RenderPrompt.Facts(d, 1);
        Assert.Equal("70% cotton 30% recycled polyester, solid fleece", Assert.Single(facts.Fabrics));
        Assert.Equal(["core black"], facts.Colours);
        Assert.Equal(["zipper (white)", "heat-transfer graphic"], facts.Details);    // labels and thread are not visible

        var prompt = RenderPrompt.Build(facts, withSketch: true);
        Assert.StartsWith("Photorealistic e-commerce product photo of a men's jacket. Main fabric: 70% cotton", prompt);
        Assert.Contains("attached flat sketch", prompt);
        Assert.DoesNotContain("adidas", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Design_prompt_describes_the_style_and_uses_the_sketch_in_image_mode()
    {
        var black = new BomLineColorwayDto(1, "A0", "CORE BLACK");
        var d = Detail(1, "S1", [Colorway(1, "KG1", "CORE BLACK"), Colorway(2, "KG2", "WHITE/NAVY"), Colorway(3, "KG3", "RED", "DROPPED")],
            Line(1, "FAB", 10, "F1", null, 1.7m, desc: "100%Recycle Polyester,Single jersey,FD 60D/60F FDY+FD 30D/36F DTY", colours: black),
            Line(2, "ACC", 200, "Z1", 1, 1, type: "ZIP", colours: new BomLineColorwayDto(1, "W", "WHITE")),
            Line(3, "LNP", 800, "L1", 1, 0, type: "LBL", desc: "ADIDAS CARE LABEL"));
        d = d with { Style = d.Style with { Description = "MALE; 100%POLYESTER(100%RECYCLED); Solid; 101.0 G/SQM; JACKET", ModelName = "ADI365 RUN JKT" } };

        var all = DesignPrompt.Build(d, "text", null);
        Assert.Equal("text", all.Mode);
        Assert.StartsWith("Design a men's jacket. Main fabric: 100% recycled polyester, single jersey, about 101 g/m². ", all.Prompt);
        Assert.Contains("Colourways: core black; white/navy.", all.Prompt);   // dropped colorways are left out
        Assert.Contains("Visible details: zipper. ", all.Prompt);
        Assert.Contains("Made with recycled fibres.", all.Prompt);
        Assert.Contains("technical flats", all.Prompt);
        Assert.DoesNotContain("adidas", all.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ADI365", all.Prompt);   // model names can carry brand codes

        var one = DesignPrompt.Build(d, "text", 1);
        Assert.Contains("Colours: core black. ", one.Prompt);
        Assert.Contains("zipper (white)", one.Prompt);

        Assert.Equal("text", DesignPrompt.Build(d, "image", 1).Mode);   // nothing to attach yet
        var withSketch = d with { Style = d.Style with { SketchUrl = "/api/v1/styles/images/0123456789abcdef0123456789abcdef.jpg" } };
        var image = DesignPrompt.Build(withSketch, "image", 1);
        Assert.Equal(("image", "sketch"), (image.Mode, image.ReferenceKind));
        Assert.StartsWith("Use the attached flat sketch as the base design.", image.Prompt);
        Assert.Contains("Turn it into a photorealistic men's jacket.", image.Prompt);
    }

    [Theory]
    [InlineData("MALE; 100%POLYESTER; Solid; 101.0 G/SQM; T-SHIRT", "101")]
    [InlineData("fleece 280gsm", "280")]
    [InlineData("no weight here", null)]
    public void Design_prompt_reads_the_fabric_weight_from_the_description(string description, string? expected)
        => Assert.Equal(expected, DesignPrompt.Weight(description));

    [Theory]
    [InlineData("100% recycle pa, plain weave", "100% recycled polyamide, plain weave")]
    [InlineData("100% rec. pes, plain", "100% recycled polyester, plain")]
    [InlineData("88%recycled poly 12% ea, interlock", "88% recycled polyester 12% elastane, interlock")]
    [InlineData("70% cotton 30% recycled polyester, solid fleece", "70% cotton 30% recycled polyester, solid fleece")]
    public void Prompts_spell_out_fibre_shorthand(string fabric, string expected)
        => Assert.Equal(expected, RenderPrompt.Readable(fabric));

    [Fact]
    public void Render_prompt_uses_plain_fabric_words()
    {
        var d = Detail(1, "S1", [Colorway(1, "KG1", "BLACK")],
            Line(1, "FAB", 10, "F1", null, 1.2m, desc: "100%Recycle Polyester,Single jersey,FD 60D/60F FDY+FD 30D/36F DTY"),
            Line(2, "FAB", 20, "F2", null, 0.3m, desc: "100% REC. PA,Plain weave"));
        var prompt = RenderPrompt.Build(RenderPrompt.Facts(d, 1), withSketch: false);
        Assert.Contains("Main fabric: 100% recycled polyester, single jersey.", prompt);
        Assert.Contains("Also uses 100% recycled polyamide, plain weave.", prompt);
    }

    // ----- Providers -----

    [Fact]
    public void Gemini_schema_upper_cases_types_but_not_property_names()
    {
        var schema = AiJson.Obj(new() { ["type"] = AiJson.Str(), ["items"] = AiJson.Arr(AiJson.Int()) }, "type");
        var g = GeminiApi.ToGeminiSchema(schema);
        Assert.Equal("OBJECT", (string?)g["type"]);
        Assert.Equal("STRING", (string?)g["properties"]!["type"]!["type"]);
        Assert.Equal("INTEGER", (string?)g["properties"]!["items"]!["items"]!["type"]);
        Assert.Equal("object", (string?)schema["type"]);   // the shared schema is not changed
    }

    [Fact]
    public void Older_gemini_schemas_are_sent_to_openai_compatible_servers_in_standard_case()
    {
        var s = LT.ODM.Infrastructure.Ai.OpenAiApi.ToStandardSchema(new JsonObject
        {
            ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "OBJECT", ["properties"] = new JsonObject { ["type"] = new JsonObject { ["type"] = "STRING" } } },
        });
        Assert.Equal(("array", "object", "string"), ((string?)s["type"], (string?)s["items"]!["type"], (string?)s["items"]!["properties"]!["type"]!["type"]));
    }

    [Theory]
    [InlineData("<think>reasoning</think>\n{\"a\":1}", "{\"a\":1}")]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("  ", null)]
    public void In_house_answers_lose_reasoning_and_fences(string raw, string? expected)
        => Assert.Equal(expected, OpenAiApi.CleanJson(raw));
}
