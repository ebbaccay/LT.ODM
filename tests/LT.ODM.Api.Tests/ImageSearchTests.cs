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
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>Answers every picture with Answer (or throws when Fail is set); records the pictures sent.</summary>
public sealed class FakeAiVisionClient : IAiVisionClient
{
    public bool IsConfigured { get; set; } = true;
    public string Answer { get; set; } = """
        { "garment": "men's hooded full-zip track jacket", "colours": ["navy", "white"], "details": ["white side stripes", "zip pockets"],
          "fabricLook": "smooth tricot", "description": "A slim track jacket with a hood." }
        """;
    /// <summary>Calls after this many succeed fail with an AiServiceException (null = never).</summary>
    public int? FailAfter { get; set; }
    public List<ReferenceImage> Calls { get; } = [];

    public Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new AiProviderInfo("Ollama", "fake-vision", IsConfigured, LeavesNetwork: false));

    public Task<string> DescribeJsonAsync(string purpose, string systemInstruction, string userText, ReferenceImage image, JsonObject schema, CancellationToken ct = default)
    {
        if (FailAfter is { } n && Calls.Count >= n) throw new AiServiceException("The AI service took too long to answer. Try again.");
        Calls.Add(image);
        return Task.FromResult(Answer);
    }
}

/// <summary>Returns a vector of Dimensions numbers; records the texts embedded.</summary>
public sealed class FakeAiEmbeddingClient : IAiEmbeddingClient
{
    public bool IsConfigured { get; set; } = true;
    public int Dimensions { get; set; } = ImageSearchRules.Dimensions;
    public List<string> Texts { get; } = [];

    public Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new AiProviderInfo("Ollama", "fake-embed", IsConfigured, LeavesNetwork: false));

    public Task<float[]> EmbedAsync(string purpose, string text, CancellationToken ct = default)
    {
        Texts.Add(text);
        return Task.FromResult(Enumerable.Repeat(0.01f, Dimensions).ToArray());
    }
}

/// <summary>The built-in look-alike model: off by default (tests of the description path); 768 numbers when on.</summary>
public sealed class FakeImageEmbeddingModel : IImageEmbeddingModel
{
    public bool IsAvailable { get; set; }
    public int Calls { get; set; }

    public ImageModelInfo Info => new(IsAvailable, "fake-siglip", IsAvailable ? null : "not installed");

    public float[] Embed(byte[] content)
    {
        if (ConceptStudioRules.DetectImage(content) is null) throw new UnreadableImageException();
        Calls++;
        return Enumerable.Repeat(0.036f, ImageSearchRules.ImageDimensions).ToArray();
    }

    /// <summary>The text part is installed (only matters while the model itself is available).</summary>
    public bool TextAvailable { get; set; } = true;
    public List<string> Texts { get; } = [];

    public ImageModelInfo TextInfo => new(IsAvailable && TextAvailable, "fake-siglip", IsAvailable && TextAvailable ? null : "not installed");

    public float[] EmbedText(string text)
    {
        Texts.Add(text);
        return Enumerable.Repeat(0.036f, ImageSearchRules.ImageDimensions).ToArray();
    }
}

public sealed class FakeImageSearchRepository : IImageSearchRepository
{
    public List<ImageToIndexDto> ToIndex { get; } = [];
    public List<ImageReading> Saved { get; } = [];
    public List<ImageVectorToIndexDto> VectorsToIndex { get; } = [];
    public List<ImageVectorReading> SavedVectors { get; } = [];
    /// <summary>Styles that have a look-alike vector ("more like this" by look finds matches only for these).</summary>
    public HashSet<int> StylesWithVectors { get; } = [];
    public (float[]? Vector, int? FromStyleId, string? Customer)? LastSearch { get; private set; }
    public string? LastSearchKind { get; private set; }
    /// <summary>Similarity of the one fake match (word-to-picture scores are low with SigLIP).</summary>
    public decimal Similarity { get; set; } = 0.91m;

    public Task<IReadOnlyList<ImageIndexSourceCountDto>> VectorStatusAsync(string model, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ImageIndexSourceCountDto>>([new ImageIndexSourceCountDto("Photo", 5, 5, 0, 0), new ImageIndexSourceCountDto("Sketch", 3, 1, 0, 2)]);

    public Task<IReadOnlyList<ImageVectorToIndexDto>> VectorsToIndexAsync(string model, int take, string indexedBy, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ImageVectorToIndexDto>>(VectorsToIndex.Take(take).ToList());

    public Task<int> SaveVectorsAsync(IReadOnlyList<ImageVectorReading> readings, string model, string indexedBy, CancellationToken ct = default)
    {
        SavedVectors.AddRange(readings);
        return Task.FromResult(readings.Count);
    }

    public Task<IReadOnlyList<ImageMatchDto>> SearchVectorsAsync(string model, float[]? vector, int? fromStyleId, string? customer, int take, CancellationToken ct = default)
    {
        if (vector is null && fromStyleId is { } id && !StylesWithVectors.Contains(id)) return Task.FromResult<IReadOnlyList<ImageMatchDto>>([]);
        LastSearchKind = "lookAlike";
        return Matches(vector, fromStyleId, customer);
    }

    public Task<IReadOnlyList<ImageIndexSourceCountDto>> StatusAsync(string visionModel, string embeddingModel, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ImageIndexSourceCountDto>>([new ImageIndexSourceCountDto("Photo", 5, 2, 1, 2), new ImageIndexSourceCountDto("Sketch", 3, 0, 0, 3)]);

    public Task<IReadOnlyList<ImageToIndexDto>> ToIndexAsync(string visionModel, string embeddingModel, int take, string indexedBy, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ImageToIndexDto>>(ToIndex.Take(take).ToList());

    public Task<int> SaveAsync(IReadOnlyList<ImageReading> readings, string visionModel, string embeddingModel, string provider, string indexedBy, CancellationToken ct = default)
    {
        Saved.AddRange(readings);
        return Task.FromResult(readings.Count);
    }

    public Task<IReadOnlyList<ImageMatchDto>> SearchAsync(string embeddingModel, float[]? vector, int? fromStyleId, string? customer, int take, CancellationToken ct = default)
    {
        LastSearchKind = "description";
        return Matches(vector, fromStyleId, customer);
    }

    private Task<IReadOnlyList<ImageMatchDto>> Matches(float[]? vector, int? fromStyleId, string? customer)
    {
        LastSearch = (vector, fromStyleId, customer);
        return Task.FromResult<IReadOnlyList<ImageMatchDto>>(
        [
            new ImageMatchDto(9, "S2708MR0230", "2027-SS", "ADI", "TIRO HD TT", "JACKET", true, "Photo", null, "/api/v1/styles/images/" + new string('a', 32) + ".jpg",
                "men's hooded track jacket", "navy, white", "side stripes", "A track jacket.", Similarity),
        ]);
    }
}

public sealed class ImageSearchTests : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private readonly ApiFactory factory;

    public ImageSearchTests(ApiFactory factory)
    {
        this.factory = factory;
        // Each test starts from working jobs and an empty queue.
        factory.Vision.IsConfigured = true;
        factory.Vision.FailAfter = null;
        factory.Vision.Calls.Clear();
        factory.Embedding.IsConfigured = true;
        factory.Embedding.Dimensions = ImageSearchRules.Dimensions;
        factory.Embedding.Texts.Clear();
        factory.ImageSearch.ToIndex.Clear();
        factory.ImageSearch.Saved.Clear();
        factory.ImageSearch.VectorsToIndex.Clear();
        factory.ImageSearch.SavedVectors.Clear();
        factory.ImageSearch.StylesWithVectors.Clear();
        factory.ImageModel.IsAvailable = false;
        factory.ImageModel.TextAvailable = true;
        factory.ImageModel.Calls = 0;
        factory.ImageModel.Texts.Clear();
        factory.ImageSearch.Similarity = 0.91m;
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "is" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private async Task<string> StoredPictureUrlAsync()
    {
        var store = factory.Services.GetRequiredService<IStyleImageStore>();
        return "/api/v1/styles/images/" + await store.SaveAsync(FakeAiImageClient.Png, ImageKind.Png);
    }

    private static MultipartFormDataContent Photo(byte[] bytes, string? customer = null)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "file", "photo.png" } };
        if (customer is not null) form.Add(new StringContent(customer), "customer");
        return form;
    }

    [Fact]
    public async Task Readers_search_editors_index_factories_do_neither()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).GetAsync("/api/v1/ai/image-search/status")).StatusCode);

        var viewer = await SignInAsync("Viewer");
        var status = await viewer.GetFromJsonAsync<ImageSearchStatusDto>("/api/v1/ai/image-search/status");
        Assert.Equal((8, 2, 1, 5, 1024), (status!.Pictures, status.Indexed, status.Failed, status.ToIndex, status.Dimensions));
        Assert.Equal(ImageSearchRules.Sources, status.Sources.Select(s => s.Source));   // sources without pictures are listed too
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/v1/ai/image-search/index", null)).StatusCode);

        var words = await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "navy track jacket with white stripes", customer = "ADI" });
        Assert.Equal(HttpStatusCode.OK, words.StatusCode);
        var result = await words.Content.ReadFromJsonAsync<ImageSearchResultDto>();
        Assert.Equal(("words", "S2708MR0230"), (result!.Mode, Assert.Single(result.Matches).StyleNo));
        Assert.Equal("navy track jacket with white stripes", Assert.Single(factory.Embedding.Texts));   // the words as typed
        Assert.Equal("ADI", factory.ImageSearch.LastSearch!.Value.Customer);

        // More like this compares stored vectors: no AI call.
        var similar = await viewer.GetFromJsonAsync<ImageSearchResultDto>("/api/v1/ai/image-search/similar/7");
        Assert.Equal(("similar", 7), (similar!.Mode, factory.ImageSearch.LastSearch!.Value.FromStyleId));
        Assert.Null(factory.ImageSearch.LastSearch.Value.Vector);
        Assert.Single(factory.Embedding.Texts);
    }

    [Fact]
    public async Task Photo_search_reads_the_photo_and_embeds_the_same_layout_as_the_index()
    {
        var viewer = await SignInAsync("Viewer");
        var response = await viewer.PostAsync("/api/v1/ai/image-search/photo", Photo(FakeAiImageClient.Png, "ADI"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImageSearchResultDto>();
        Assert.Equal(("photo", "men's hooded full-zip track jacket"), (result!.Mode, result.Reading!.Garment));
        Assert.Equal(["navy", "white"], result.Reading.Colours);
        Assert.Equal(
            "men's hooded full-zip track jacket. Colours: navy, white. Details: white side stripes; zip pockets. Fabric: smooth tricot. A slim track jacket with a hood.",
            Assert.Single(factory.Embedding.Texts));

        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsync("/api/v1/ai/image-search/photo", Photo("not a picture"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "x" })).StatusCode);

        // A model that describes nothing: a readable 502, nothing searched.
        factory.Vision.Answer = """{ "garment": "", "colours": [], "details": [], "description": "" }""";
        try
        {
            Assert.Equal(HttpStatusCode.BadGateway, (await viewer.PostAsync("/api/v1/ai/image-search/photo", Photo(FakeAiImageClient.Png))).StatusCode);
        }
        finally
        {
            factory.Vision.Answer = new FakeAiVisionClient().Answer;
        }
    }

    [Fact]
    public async Task Search_answers_503_when_a_job_cannot_run_and_502_for_a_wrong_size_model()
    {
        var viewer = await SignInAsync("Viewer");
        factory.Vision.IsConfigured = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await viewer.PostAsync("/api/v1/ai/image-search/photo", Photo(FakeAiImageClient.Png))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "red tee" })).StatusCode);   // words need no vision

        factory.Embedding.IsConfigured = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "red tee" })).StatusCode);

        factory.Embedding.IsConfigured = true;
        factory.Embedding.Dimensions = 768;
        var wrong = await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "red tee" });
        Assert.Equal(HttpStatusCode.BadGateway, wrong.StatusCode);
        Assert.Contains("1024", await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Indexing_reads_new_pictures_re_embeds_known_text_and_marks_missing_files()
    {
        var url = await StoredPictureUrlAsync();
        var missing = "/api/v1/styles/images/" + new string('b', 32) + ".jpg";
        factory.ImageSearch.ToIndex.AddRange(
        [
            new ImageToIndexDto(1, "Photo", 0, url, null, null, null, null, null, 3),
            new ImageToIndexDto(2, "Colorway", 44, url, "known text", "tee", "red", "crew neck", "A tee.", 3),
            new ImageToIndexDto(3, "Sketch", 0, missing, null, null, null, null, null, 3),
        ]);

        var editor = await SignInAsync("Merchandiser");
        var response = await editor.PostAsync("/api/v1/ai/image-search/index", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The missing file is not a failure: nothing is stored for it, it is counted and stays to do.
        Assert.Equal(new IndexImagesResultDto(2, 0, 1, "descriptions", 1), await response.Content.ReadFromJsonAsync<IndexImagesResultDto>());

        Assert.Single(factory.Vision.Calls);                                     // only the new picture was read
        Assert.Equal(["known text"], factory.Embedding.Texts.Skip(1));          // the known one was only re-embedded
        var saved = factory.ImageSearch.Saved;
        Assert.Equal(2, saved.Count);
        Assert.Equal(("men's hooded full-zip track jacket", 1024), (saved[0].Garment, saved[0].Embedding!.Length));
        Assert.Equal(("tee", "known text"), (saved[1].Garment, saved[1].SearchText));
    }

    [Fact]
    public async Task Missing_files_never_block_indexing_and_non_pictures_fail()
    {
        factory.ImageModel.IsAvailable = true;
        var url = await StoredPictureUrlAsync();
        var missing = "/api/v1/styles/images/" + new string('d', 32) + ".jpg";
        var editor = await SignInAsync("Admin");

        // Only missing files left for the look-alike step: the round goes on with the descriptions.
        factory.ImageSearch.VectorsToIndex.Add(new ImageVectorToIndexDto(3, "Sketch", 0, missing, 1));
        factory.ImageSearch.ToIndex.Add(new ImageToIndexDto(1, "Photo", 0, url, null, null, null, null, null, 1));
        var round = await (await editor.PostAsync("/api/v1/ai/image-search/index", null)).Content.ReadFromJsonAsync<IndexImagesResultDto>();
        Assert.Equal(new IndexImagesResultDto(1, 0, 1, "descriptions", 1), round);
        Assert.Empty(factory.ImageSearch.SavedVectors);

        // A file that is there but is not a picture is a real failure (stored, not retried).
        var bogus = new string('e', 32) + ".jpg";
        Directory.CreateDirectory(Path.Combine(factory.FilesRoot, "style-library"));
        await File.WriteAllTextAsync(Path.Combine(factory.FilesRoot, "style-library", bogus), "not a picture");
        factory.ImageSearch.VectorsToIndex.Clear();
        factory.ImageSearch.VectorsToIndex.Add(new ImageVectorToIndexDto(4, "Photo", 0, "/api/v1/styles/images/" + bogus, 1));
        var bad = await (await editor.PostAsync("/api/v1/ai/image-search/index", null)).Content.ReadFromJsonAsync<IndexImagesResultDto>();
        Assert.Equal((0, 1, 0), (bad!.Indexed, bad.Failed, bad.Missing));
        Assert.Equal("The file is not a JPEG, PNG, GIF or WebP picture.", Assert.Single(factory.ImageSearch.SavedVectors).Error);
    }

    // ----- Look-alike vectors (built-in model) -----

    [Fact]
    public async Task Photo_search_uses_the_built_in_model_without_the_ai_server()
    {
        factory.ImageModel.IsAvailable = true;
        factory.Vision.IsConfigured = false;                 // no AI server at all
        factory.Embedding.IsConfigured = false;
        var viewer = await SignInAsync("Viewer");

        var status = await viewer.GetFromJsonAsync<ImageSearchStatusDto>("/api/v1/ai/image-search/status");
        Assert.Equal((true, "fake-siglip", 6, 8), (status!.LookAlike.IsAvailable, status.LookAlike.Model, status.LookAlike.Indexed, status.LookAlike.Pictures));

        var response = await viewer.PostAsync("/api/v1/ai/image-search/photo", Photo(FakeAiImageClient.Png, "ADI"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImageSearchResultDto>();
        Assert.Equal(("photo", ImageSearchMethods.LookAlike, "lookAlike"), (result!.Mode, result.Method, factory.ImageSearch.LastSearchKind));
        Assert.Null(result.Reading);                         // nothing was described
        Assert.Equal(768, factory.ImageSearch.LastSearch!.Value.Vector!.Length);
        Assert.Empty(factory.Vision.Calls);
        Assert.Empty(factory.Embedding.Texts);

        // Words by look need no AI server either; asking for the AI server without one is 503.
        var words = await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "red tee" });
        Assert.Equal(HttpStatusCode.OK, words.StatusCode);
        Assert.Equal(ImageSearchMethods.LookAlike, (await words.Content.ReadFromJsonAsync<ImageSearchResultDto>())!.Method);
        Assert.Equal(["red tee"], factory.ImageModel.Texts);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "red tee", useAiServer = true })).StatusCode);

        // Without the model's text part, words go to the descriptions (here: not set up).
        factory.ImageModel.TextAvailable = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "red tee" })).StatusCode);
    }

    [Fact]
    public async Task The_ai_server_tickbox_sends_searches_to_the_descriptions()
    {
        factory.ImageModel.IsAvailable = true;
        var viewer = await SignInAsync("Viewer");

        var words = await (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "navy track jacket", useAiServer = true }))
            .Content.ReadFromJsonAsync<ImageSearchResultDto>();
        Assert.Equal((ImageSearchMethods.Description, "description"), (words!.Method, factory.ImageSearch.LastSearchKind));
        Assert.Equal(["navy track jacket"], factory.Embedding.Texts);
        Assert.Empty(factory.ImageModel.Texts);

        var form = Photo(FakeAiImageClient.Png);
        form.Add(new StringContent("true"), "useAiServer");
        var photo = await (await viewer.PostAsync("/api/v1/ai/image-search/photo", form)).Content.ReadFromJsonAsync<ImageSearchResultDto>();
        Assert.Equal(ImageSearchMethods.Description, photo!.Method);
        Assert.NotNull(photo.Reading);
        Assert.Single(factory.Vision.Calls);
        Assert.Equal(0, factory.ImageModel.Calls);
    }

    [Fact]
    public async Task Words_by_look_flag_a_far_best_match_as_weak()
    {
        factory.ImageModel.IsAvailable = true;
        var viewer = await SignInAsync("Viewer");
        factory.ImageSearch.Similarity = 0.11m;              // a good word-to-picture match by SigLIP standards
        var close = await (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "denim shorts" })).Content.ReadFromJsonAsync<ImageSearchResultDto>();
        Assert.False(close!.Weak);

        factory.ImageSearch.Similarity = -0.05m;             // "red dress" in a library without dresses
        var far = await (await viewer.PostAsJsonAsync("/api/v1/ai/image-search/words", new { query = "red dress" })).Content.ReadFromJsonAsync<ImageSearchResultDto>();
        Assert.True(far!.Weak);
        Assert.Single(far.Matches);                          // still shown, ordered, with the warning
    }

    [Fact]
    public async Task More_like_this_prefers_look_alike_vectors_and_falls_back_to_descriptions()
    {
        var viewer = await SignInAsync("Viewer");
        factory.ImageSearch.StylesWithVectors.Add(7);
        var byLook = await viewer.GetFromJsonAsync<ImageSearchResultDto>("/api/v1/ai/image-search/similar/7");
        Assert.Equal((ImageSearchMethods.LookAlike, "lookAlike"), (byLook!.Method, factory.ImageSearch.LastSearchKind));

        var byText = await viewer.GetFromJsonAsync<ImageSearchResultDto>("/api/v1/ai/image-search/similar/8");   // no look-alike vector yet
        Assert.Equal((ImageSearchMethods.Description, "description"), (byText!.Method, factory.ImageSearch.LastSearchKind));
    }

    [Fact]
    public async Task Indexing_does_look_alike_vectors_first_then_descriptions()
    {
        factory.ImageModel.IsAvailable = true;
        var url = await StoredPictureUrlAsync();
        var missing = "/api/v1/styles/images/" + new string('c', 32) + ".jpg";
        factory.ImageSearch.VectorsToIndex.AddRange(
        [
            new ImageVectorToIndexDto(1, "Photo", 0, url, 2),
            new ImageVectorToIndexDto(3, "Sketch", 0, missing, 2),
        ]);
        factory.ImageSearch.ToIndex.Add(new ImageToIndexDto(1, "Photo", 0, url, null, null, null, null, null, 1));
        var editor = await SignInAsync("Merchandiser");

        var first = await (await editor.PostAsync("/api/v1/ai/image-search/index", null)).Content.ReadFromJsonAsync<IndexImagesResultDto>();
        // Remaining: the missing file (still to do) plus the descriptions still to do (5 in the fake status).
        Assert.Equal(new IndexImagesResultDto(1, 0, 6, "lookAlike", 1), first);
        Assert.Empty(factory.Vision.Calls);                 // vectors need no AI server
        Assert.Equal(768, Assert.Single(factory.ImageSearch.SavedVectors).Vector!.Length);   // nothing stored for the missing file

        factory.ImageSearch.VectorsToIndex.Clear();         // all vectors done: the next round reads descriptions
        var second = await (await editor.PostAsync("/api/v1/ai/image-search/index", null)).Content.ReadFromJsonAsync<IndexImagesResultDto>();
        Assert.Equal(("descriptions", 1), (second!.Step, second.Indexed));
        Assert.Single(factory.Vision.Calls);

        // Built-in model only (no AI server): indexing still runs the vectors.
        factory.Vision.IsConfigured = false;
        factory.ImageSearch.VectorsToIndex.Add(new ImageVectorToIndexDto(1, "Photo", 0, url, 1));
        var third = await editor.PostAsync("/api/v1/ai/image-search/index", null);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(new IndexImagesResultDto(1, 0, 0, "lookAlike"), await third.Content.ReadFromJsonAsync<IndexImagesResultDto>());
    }

    [Fact]
    public async Task Indexing_keeps_pictures_read_before_the_ai_failed()
    {
        var url = await StoredPictureUrlAsync();
        factory.ImageSearch.ToIndex.AddRange(
        [
            new ImageToIndexDto(1, "Photo", 0, url, null, null, null, null, null, 2),
            new ImageToIndexDto(2, "Photo", 0, url, null, null, null, null, null, 2),
        ]);
        factory.Vision.FailAfter = 1;

        var response = await (await SignInAsync("Admin")).PostAsync("/api/v1/ai/image-search/index", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, Assert.Single(factory.ImageSearch.Saved).StyleId);      // the first picture is not lost
    }
}
