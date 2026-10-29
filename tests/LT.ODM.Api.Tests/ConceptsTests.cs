using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Auth;
using LT.ODM.Application.Concepts;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakeConceptRepository : IConceptRepository
{
    public List<(string Action, int? ConceptId, object? Request, string ChangedBy)> Writes { get; } = [];
    public ConceptListQuery? LastList { get; private set; }
    public List<(int ConceptId, string Url, string? Caption, PictureVector Vector)> Images { get; } = [];
    public List<(int ImageId, string Model, float[]? Vector, string? Error)> Vectors { get; } = [];
    public string? LastModel { get; private set; }

    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];

    public Task<ConceptPageDto> ListAsync(ConceptListQuery query, CancellationToken ct = default)
    {
        LastList = query;
        return Task.FromResult(new ConceptPageDto([new(1, "Recycled windbreaker", "SKE", "Skechers", "2027-SS", "JACKET", "Jackets and coats", "Gen Z urban",
            "MALE", "Draft", "merch", 2, 1, 0, "Athleisure", null, DateTime.UtcNow)], 1));
    }

    /// <summary>Concept 5 for SKE: picture 1 read, picture 2 not read yet (until SetImageVectorAsync), picture 3 failed.</summary>
    public Task<ConceptDetailDto?> GetAsync(int conceptId, string? model, CancellationToken ct = default)
    {
        LastModel = model;
        if (conceptId == 404) return Task.FromResult<ConceptDetailDto?>(null);
        var read2 = Vectors.Any(v => v.ImageId == 2 && v.Vector is not null);
        return Task.FromResult<ConceptDetailDto?>(new ConceptDetailDto(
            new ConceptHeaderDto(conceptId, "Recycled windbreaker", "SKE", "Skechers", "SKE", "2027-SS", "GENZ", "Gen Z urban", "JACKET", "Jackets and coats",
                "MALE", "USD 14 FOB", "Packable shell", "Draft", "merch", null, "merch", DateTime.UtcNow, null, null, RowVer),
            [new("ATHLEISURE", "Athleisure"), new("RECYCLED", "Recycled materials")],
            [
                new(1, "/api/v1/concepts/images/a.jpg", null, 1, true, null, "merch", DateTime.UtcNow),
                new(2, "/api/v1/concepts/images/0123456789abcdef0123456789abcdef.png", null, 2, read2, null, "merch", DateTime.UtcNow),
                new(3, "/api/v1/concepts/images/c.jpg", null, 3, false, "The picture could not be read.", "merch", DateTime.UtcNow),
                .. (ExtraImageUrl is null ? Array.Empty<ConceptImageDto>() : [new ConceptImageDto(9, ExtraImageUrl, null, 4, true, null, "merch", DateTime.UtcNow)]),
            ],
            [new(421, "L326M062", "2026-FA", "SKE", "SKE", null, null, "Jacket", "MALE", null, null, true, true, "Zip garage", "Pick", "merch", DateTime.UtcNow)],
            []));
    }

    public Task<IReadOnlyList<StyleConceptDto>> ForStyleAsync(int styleId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StyleConceptDto>>([new(5, "Recycled windbreaker", "SKE", "2027-SS", "Draft", "merch", "Zip garage")]);

    public IReadOnlyList<string>? LastWords { get; private set; }

    /// <summary>JACKET types, brand SKE; of the words asked about, those a fabric BOM line holds: RIPSTOP and RECYCL.</summary>
    public Task<ConceptMatchContext> MatchContextAsync(int conceptId, IReadOnlyList<string> words, CancellationToken ct = default)
    {
        LastWords = words;
        return Task.FromResult(new ConceptMatchContext(["JACKET", "JACKETS"], ["SKE"], words.Where(w => w is "RIPSTOP" or "RECYCL").ToList()));
    }

    /// <summary>A real stored picture (image 9) for the tests that read its file.</summary>
    public string? ExtraImageUrl { get; set; }

    public (int ConceptId, string Season, string StyleNo, int? BaseStyleId, string? SketchUrl)? LastDevelop { get; private set; }

    public Task<(int StyleId, bool? SameBrandBase)> DevelopStyleAsync(int conceptId, string seasonCode, string styleNo, int? baseStyleId, string? sketchUrl,
        string changedBy, CancellationToken ct = default)
    {
        if (styleNo == "TAKEN") throw new StyleRuleException(409, "The customer already has a style with this number in this season.");
        LastDevelop = (conceptId, seasonCode, styleNo, baseStyleId, sketchUrl);
        return Task.FromResult((777, (bool?)true));
    }

    public Task<ConceptLookupsDto> LookupsAsync(CancellationToken ct = default)
        => Task.FromResult(new ConceptLookupsDto([new("GENZ", "Gen Z urban")], [new("ATHLEISURE", "Athleisure")], [new("JACKET", "Jackets and coats")]));

    public Task<int> SaveAsync(int? conceptId, SaveConceptRequest request, string changedBy, CancellationToken ct = default)
    {
        if (conceptId == 409) throw new StyleRuleException(409, "Someone else changed this concept after you opened it. Reload and try again.");
        Writes.Add(("save", conceptId, request, changedBy));
        return Task.FromResult(conceptId ?? 12);
    }

    public Task<byte[]> SaveBriefAsync(int conceptId, string? summary, string? briefJson, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("brief", conceptId, (summary, briefJson), changedBy));
        return Task.FromResult<byte[]>([0, 0, 0, 0, 0, 0, 0, 2]);
    }

    public Task DeleteAsync(int conceptId, byte[] rowVer, string changedBy, CancellationToken ct = default)
    {
        if (conceptId == 409) throw new StyleRuleException(409, "Styles were offered in a package for this concept, so it is kept for the record. Set it to Dropped instead.");
        Writes.Add(("delete", conceptId, rowVer, changedBy));
        return Task.CompletedTask;
    }

    public Task<int> AddImageAsync(int conceptId, string imageUrl, string? caption, PictureVector vector, string addedBy, CancellationToken ct = default)
    {
        Images.Add((conceptId, imageUrl, caption, vector));
        return Task.FromResult(Images.Count);
    }

    public List<(int ImageId, bool ForClient, string? Caption)> SavedImages { get; } = [];

    public Task SaveImageAsync(int conceptId, int conceptImageId, bool forClient, string? caption, CancellationToken ct = default)
    {
        SavedImages.Add((conceptImageId, forClient, caption));
        return Task.CompletedTask;
    }

    public Task RemoveImageAsync(int conceptId, int conceptImageId, CancellationToken ct = default)
    {
        Writes.Add(("removeImage", conceptId, conceptImageId, ""));
        return Task.CompletedTask;
    }

    public string? ToReadUrl { get; set; }

    public Task<IReadOnlyList<ConceptImageToReadDto>> ImagesToReadAsync(int conceptId, string model, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConceptImageToReadDto>>(ToReadUrl is null ? [] : [new(2, ToReadUrl)]);

    public Task SetImageVectorAsync(int conceptImageId, string model, float[]? vector, string? error, CancellationToken ct = default)
    {
        Vectors.Add((conceptImageId, model, vector, error));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConceptStyleAddResultDto>> AddStylesAsync(int conceptId, IReadOnlyList<int> styleIds, string? addedFrom, string changedBy,
        CancellationToken ct = default)
    {
        Writes.Add(("styles", conceptId, (styleIds, addedFrom), changedBy));
        return Task.FromResult<IReadOnlyList<ConceptStyleAddResultDto>>(styleIds.Select(id => new ConceptStyleAddResultDto(id, "S" + id, "2027-SS", "ADI", "Added")).ToList());
    }

    public Task SaveStyleAsync(int conceptId, int styleId, string? note, CancellationToken ct = default)
    {
        Writes.Add(("note", conceptId, (styleId, note), ""));
        return Task.CompletedTask;
    }

    public Task RemoveStyleAsync(int conceptId, int styleId, CancellationToken ct = default)
    {
        Writes.Add(("removeStyle", conceptId, styleId, ""));
        return Task.CompletedTask;
    }

    /// <summary>Picture 1: ADI 0.95 (close), the customer's own SKE 0.99 (fine), ADI 0.87 (check), TMS 0.60 (clear).</summary>
    public Task<IReadOnlyList<ConceptClosenessRow>> ClosenessAsync(int conceptId, string model, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConceptClosenessRow>>(
        [
            new(1, 421, "L326M062", "2026-FA", "SKE", "SKE", null, "Jacket", "Photo", "/api/v1/styles/images/s.jpg", true, 0.99m),
            new(1, 1, "F2325M142", "2025-FW", "ADI", "ADI", null, "Jacket", "Sketch", "/api/v1/styles/images/a.jpg", false, 0.95m),
            new(1, 2, "F2325M143", "2025-FW", "ADI", "ADI", null, "Jacket", "Sketch", "/api/v1/styles/images/b.jpg", false, 0.87m),
            new(1, 3, "F26SMU39", "2026-FW", "TMS", null, null, "Track top", "Sketch", "/api/v1/styles/images/t.jpg", false, 0.60m),
        ]);
}

public sealed class ConceptsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private const string RowVer = "AAAAAAAAAAE=";

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private FakeConceptRepository Concepts => factory.Concepts;

    private async Task<(HttpClient Client, string Name)> SignInAsync(string role)
    {
        var name = "cpt" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, name);
    }

    private static MultipartFormDataContent Upload(byte[] content, string? caption = null)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { file, "file", "a.png" } };
        if (caption is not null) form.Add(new StringContent(caption), "caption");
        return form;
    }

    [Fact]
    public async Task Readers_see_concepts_but_only_editors_change_them()
    {
        var (viewer, name) = await SignInAsync("Viewer");
        var page = await viewer.GetFromJsonAsync<ConceptPageDto>("/api/v1/concepts?mine=true&status=ready&category=jacket&take=999");
        Assert.Equal(1, page!.Total);
        Assert.Equal(new ConceptListQuery(null, null, null, "JACKET", "Ready", true, name, 0, 200), Concepts.LastList);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync("/api/v1/concepts?status=won")).StatusCode);
        Assert.Equal("GENZ", (await viewer.GetFromJsonAsync<ConceptLookupsDto>("/api/v1/concepts/lookups"))!.TargetMarkets[0].Code);
        Assert.Equal(2, (await viewer.GetFromJsonAsync<ConceptDetailDto>("/api/v1/concepts/5"))!.Tags.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/concepts/404")).StatusCode);
        Assert.Single((await viewer.GetFromJsonAsync<List<StyleConceptDto>>("/api/v1/concepts/for-style/421"))!);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/concepts", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/v1/concepts/5/images", Upload(Png))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/concepts/5/styles", new { styleIds = new[] { 1 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).Client.GetAsync("/api/v1/concepts")).StatusCode);
    }

    [Fact]
    public async Task Saving_checks_the_fields_and_keeps_codes_as_the_library_stores_them()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        var bad = await merch.PostAsJsonAsync("/api/v1/concepts", new { name = " ", gender = "men", status = "Won", trendTags = new[] { " " } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var problem = await bad.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Equal(["gender", "name", "status", "trendTags"], problem!.Errors.Keys.Select(k => k.ToLowerInvariant() == "trendtags" ? "trendTags" : k.ToLowerInvariant()).Order(StringComparer.Ordinal));

        var ok = await merch.PostAsJsonAsync("/api/v1/concepts", new
        {
            name = " Recycled windbreaker ", customerCode = " ske ", seasonCode = "2027-ss", targetMarketCode = "genz", productCategoryCode = "jacket",
            gender = "male", status = "ready", trendTags = new[] { "athleisure", "ATHLEISURE", "recycled" }, rowVer = RowVer,
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var write = Concepts.Writes.Last(w => w.Action == "save" && w.ChangedBy == name);
        var saved = (SaveConceptRequest)write.Request!;
        Assert.Null(write.ConceptId);
        Assert.Null(saved.RowVer);
        Assert.Equal(("Recycled windbreaker", "SKE", "2027-SS", "GENZ", "JACKET", "MALE", "Ready"),
            (saved.Name, saved.CustomerCode, saved.SeasonCode, saved.TargetMarketCode, saved.ProductCategoryCode, saved.Gender, saved.Status));
        Assert.Equal(["ATHLEISURE", "RECYCLED"], saved.TrendTags!);

        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PutAsJsonAsync("/api/v1/concepts/5", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await merch.PutAsJsonAsync("/api/v1/concepts/409", new { name = "x", rowVer = RowVer })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.DeleteAsync("/api/v1/concepts/5?rowVer=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await merch.DeleteAsync($"/api/v1/concepts/409?rowVer={Uri.EscapeDataString(RowVer)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.DeleteAsync($"/api/v1/concepts/5?rowVer={Uri.EscapeDataString(RowVer)}")).StatusCode);
    }

    [Fact]
    public async Task The_brief_is_drafted_from_the_saved_concept_and_kept()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        var response = await merch.PostAsync("/api/v1/concepts/5/brief", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = factory.ConceptAi.LastRequest!;
        Assert.Equal(("Recycled windbreaker", "Skechers", "Jackets and coats", "MALE", "Packable shell"),
            (request.Name, request.Client, request.Category, request.Gender, request.Description));
        Assert.Equal(["Athleisure", "Recycled materials"], request.Trends!);
        var write = Concepts.Writes.Last(w => w.Action == "brief" && w.ChangedBy == name);
        var (summary, json) = ((string?, string?))write.Request!;
        Assert.Equal("Relaxed denim.", summary);
        Assert.Contains("\"products\":[\"Denim Jogger - $14.50\"]", json);
        Assert.Equal(HttpStatusCode.NotFound, (await merch.PostAsync("/api/v1/concepts/404/brief", null)).StatusCode);
    }

    [Fact]
    public async Task Pictures_are_read_by_the_image_model_when_it_can_run()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        factory.ImageModel.IsAvailable = true;
        var uploaded = await merch.PostAsync("/api/v1/concepts/5/images", Upload(Png, " mood board "));
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var added = Concepts.Images.Last();
        Assert.StartsWith("/api/v1/concepts/images/", added.Url);
        Assert.Equal(("mood board", "fake-siglip", 768), (added.Caption, added.Vector.Model, added.Vector.Vector!.Length));
        Assert.Equal(Png.Length > 0, (await merch.GetByteArrayAsync(added.Url)).Length > 0);

        factory.ImageModel.IsAvailable = false;
        Assert.Equal(HttpStatusCode.OK, (await merch.PostAsync("/api/v1/concepts/5/images", Upload(Png))).StatusCode);
        Assert.Null(Concepts.Images.Last().Vector.Model);   // nothing stored: read later by the closeness check
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsync("/api/v1/concepts/5/images", Upload("not an image"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.DeleteAsync("/api/v1/concepts/5/images/2")).StatusCode);
    }

    [Fact]
    public async Task Closeness_flags_other_brands_only_and_reads_pictures_left_unread()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        // a picture uploaded while the model was off is read now
        var (merch, _) = await SignInAsync("Merchandiser");
        factory.ImageModel.IsAvailable = false;
        var uploaded = await (await merch.PostAsync("/api/v1/concepts/5/images", Upload(Png))).Content.ReadFromJsonAsync<UploadResult>();
        Concepts.ToReadUrl = uploaded!.ImageUrl;
        factory.ImageModel.IsAvailable = true;
        try
        {
            var result = await viewer.GetFromJsonAsync<ConceptClosenessDto>("/api/v1/concepts/5/closeness");
            Assert.Equal((0.90m, 0.85m, true, "fake-siglip"), (result!.Thresholds.Close, result.Thresholds.Watch, result.Thresholds.ModelReady, result.Thresholds.Model));
            Assert.Contains(Concepts.Vectors, v => v.ImageId == 2 && v.Model == "fake-siglip" && v.Vector!.Length == 768);
            Assert.Equal("fake-siglip", Concepts.LastModel);

            Assert.Equal(ClosenessLevels.Close, result.Level);
            var first = result.Images.Single(i => i.ConceptImageId == 1);
            Assert.Equal([ClosenessLevels.Clear, ClosenessLevels.Close, ClosenessLevels.Check, ClosenessLevels.Clear], first.Matches.Select(m => m.Level));
            Assert.Equal(("Checked", ClosenessLevels.Close), (first.Status, first.Level));
            Assert.Equal(("Failed", ClosenessLevels.NotChecked), (result.Images.Single(i => i.ConceptImageId == 3).Status, result.Images.Single(i => i.ConceptImageId == 3).Level));
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/concepts/404/closeness")).StatusCode);
        }
        finally
        {
            Concepts.ToReadUrl = null;
            factory.ImageModel.IsAvailable = false;
        }
    }

    [Fact]
    public async Task Inspiration_styles_check_the_list_and_where_they_came_from()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/concepts/5/styles", new { styleIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/concepts/5/styles", new { styleIds = new[] { 1 }, addedFrom = "Email" })).StatusCode);
        var added = await (await merch.PostAsJsonAsync("/api/v1/concepts/5/styles", new { styleIds = new[] { 1, 2 }, addedFrom = "match" }))
            .Content.ReadFromJsonAsync<List<ConceptStyleAddResultDto>>();
        Assert.Equal(2, added!.Count);
        var write = Concepts.Writes.Last(w => w.Action == "styles" && w.ChangedBy == name);
        Assert.Equal("Match", (((IReadOnlyList<int>, string?))write.Request!).Item2);

        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PutAsJsonAsync("/api/v1/concepts/5/styles/1", new { note = new string('x', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/concepts/5/styles/1", new { note = " zip garage " })).StatusCode);
        Assert.Equal((1, "zip garage"), (((int, string?))Concepts.Writes.Last(w => w.Action == "note").Request!));
        Assert.Equal(HttpStatusCode.NoContent, (await merch.DeleteAsync("/api/v1/concepts/5/styles/1")).StatusCode);
    }

    [Fact]
    public async Task Package_copies_are_checked_and_items_keep_their_concept()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        var result = await merch.GetFromJsonAsync<PackageClosenessDto>("/api/v1/style-packages/5/closeness");
        Assert.Equal([ClosenessLevels.Close, "NoPicture"], result!.Items.Select(i => i.Level));

        Assert.Equal(HttpStatusCode.OK, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items", new { styleIds = new[] { 2 }, conceptId = 7 })).StatusCode);
        Assert.Equal(7, factory.Packages.LastConceptId);
        Assert.Equal(HttpStatusCode.OK,
            (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items/copy", new { styleId = 1, seasonCode = "2027-SS", styleNo = "N1", conceptId = 8 })).StatusCode);
        Assert.Equal(8, factory.Packages.LastConceptId);
    }

    [Fact]
    public void The_concept_pick_lists_are_reference_lists()
    {
        foreach (var key in new[] { "targetMarkets", "trendTags", "productCategories" })
        {
            var d = ReferenceLists.Find(key)!;
            Assert.True(d.HasIsActive);
            Assert.Equal("URBAN-OUTDOOR", ReferenceLists.NormalizeCode(d, " urban-outdoor "));
            Assert.Empty(ReferenceLists.Validate(d, new SaveRefListItemRequest(true, "gen_z-1", "Gen Z", null, true)));
            Assert.NotEmpty(ReferenceLists.Validate(d, new SaveRefListItemRequest(true, "gen z", "Gen Z", null, true)));
        }
    }

    [Fact]
    public async Task Proven_styles_come_from_the_concept_fields_and_ai_only_explains_them()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        var calls = factory.TextAi.Calls.Count;
        var found = await viewer.GetFromJsonAsync<ConceptLibraryMatchesDto>("/api/v1/concepts/5/library-matches");
        Assert.Equal(calls, factory.TextAi.Calls.Count);   // no AI to find them
        var criteria = factory.StyleAi.LastCriteria!;
        Assert.Equal(["JACKET", "JACKETS"], criteria.ProductTypes);
        Assert.Equal(("MALE", "SKE"), (criteria.Gender, criteria.Customer));
        Assert.Equal(["RECYCL"], criteria.Keywords);   // "Recycled materials" tag; "Packable shell" has no BOM word
        Assert.Contains("PACKABLE", Concepts.LastWords!);
        Assert.Equal([false, false], found!.Matches.Select(m => m.SameBrand));   // ADI styles for an SKE concept
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/concepts/404/library-matches")).StatusCode);

        factory.TextAi.Answers["concept matches"] = """{"matches":[{"styleId":233,"fit":82,"why":"Ripstop shell","reuse":"Pattern","differs":"Little"}]}""";
        var explained = await viewer.PostAsync("/api/v1/concepts/5/library-matches/explain", null);
        Assert.Equal(HttpStatusCode.OK, explained.StatusCode);
        Assert.Equal(1, factory.TextAi.Calls.Count - calls);   // one AI call: ranking only
        Assert.Equal(233, (await explained.Content.ReadFromJsonAsync<LT.ODM.Application.StyleAi.ConceptMatchResultDto>())!.Matches.Single().Style.StyleId);
    }

    [Fact]
    public void Fabric_words_are_cut_to_a_form_every_spelling_on_a_bom_matches()
    {
        var detail = new ConceptDetailDto(
            new ConceptHeaderDto(1, "x", null, null, null, null, null, null, null, null, null, null, "Brushed fleece with taping", "Draft", "m",
                new ConceptBriefDto(null, [], ["100% Recycled Ripstop", "Water repellent coating"], [], null, null), "m", DateTime.UtcNow, null, null, []),
            [new("GORPCORE", "Gorpcore")], [], [], []);
        Assert.Equal(["RECYCL", "RIPSTOP", "WATER", "REPELLENT", "COAT", "BRUSH", "FLEECE", "TAP", "GORPCORE"], ConceptLibraryMatcher.CandidateWords(detail));
    }

    [Fact]
    public async Task Develop_as_style_copies_the_picture_into_the_library_and_reads_it()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        var store = factory.Services.GetRequiredService<LT.ODM.Application.ConceptStudio.IConceptImageStore>();
        Concepts.ExtraImageUrl = "/api/v1/concepts/images/" + await store.SaveAsync(Png, LT.ODM.Application.ConceptStudio.ImageKind.Png);
        factory.ImageModel.IsAvailable = true;
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/concepts/5/develop", new { seasonCode = "2027-SS" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await merch.PostAsJsonAsync("/api/v1/concepts/5/develop", new { seasonCode = "2027-SS", styleNo = "LT1", conceptImageId = 77 })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await merch.PostAsJsonAsync("/api/v1/concepts/5/develop", new { seasonCode = "2027-SS", styleNo = "TAKEN" })).StatusCode);

            var ok = await merch.PostAsJsonAsync("/api/v1/concepts/5/develop", new { seasonCode = " 2027-ss ", styleNo = " LT27-WB1 ", baseStyleId = 421, conceptImageId = 9 });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var result = await ok.Content.ReadFromJsonAsync<DevelopedStyleResultDto>();
            var d = Concepts.LastDevelop!.Value;
            Assert.Equal((5, "2027-SS", "LT27-WB1", (int?)421), (d.ConceptId, d.Season, d.StyleNo, d.BaseStyleId));
            Assert.StartsWith("/api/v1/styles/images/", d.SketchUrl);
            Assert.Equal((777, d.SketchUrl), (result!.StyleId, result.SketchUrl));
            Assert.Equal(Png, await merch.GetByteArrayAsync(d.SketchUrl));   // the style library serves it now
            var vector = factory.ImageSearch.SavedVectors.Last();
            Assert.Equal((777, "Sketch", d.SketchUrl, 768), (vector.StyleId, vector.Source, vector.ImageUrl, vector.Vector!.Length));

            var (viewer, _) = await SignInAsync("Viewer");
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/concepts/5/develop", new { seasonCode = "2027-SS", styleNo = "X" })).StatusCode);
        }
        finally
        {
            factory.ImageModel.IsAvailable = false;
            Concepts.ExtraImageUrl = null;
        }
    }

    [Fact]
    public async Task Only_pictures_clear_of_other_brands_go_to_the_client()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        factory.ImageModel.IsAvailable = true;
        Concepts.ExtraImageUrl = "/api/v1/concepts/images/extra.png";   // picture 9: read, nothing close
        try
        {
            var close = await merch.PutAsJsonAsync("/api/v1/concepts/5/images/1", new { forClient = true });   // ADI 0.95
            Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
            Assert.Contains("F2325M142", await close.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, (await merch.PutAsJsonAsync("/api/v1/concepts/5/images/3", new { forClient = true })).StatusCode);   // not read
            Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/concepts/5/images/9", new { forClient = true, caption = " Mood " })).StatusCode);
            Assert.Equal((9, true, "Mood"), Concepts.SavedImages.Last());
            Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/concepts/5/images/1", new { forClient = false })).StatusCode);   // taking it off is fine
        }
        finally
        {
            factory.ImageModel.IsAvailable = false;
            Concepts.ExtraImageUrl = null;
        }
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
    private sealed record UploadResult(int ConceptImageId, string ImageUrl);
}
