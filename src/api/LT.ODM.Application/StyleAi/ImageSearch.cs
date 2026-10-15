using System.Diagnostics;
using System.Text.Json;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleLibrary;
using static LT.ODM.Application.Ai.AiJson;

namespace LT.ODM.Application.StyleAi;

// ----- Image search (AI Studio > Image search) -----

public static class ImageSearchRules
{
    /// <summary>Numbers per description vector (style.ImageIndex.Embedding is vector(1024)): the Embedding job needs a 1024-dimension model.</summary>
    public const int Dimensions = 1024;
    /// <summary>Numbers per look-alike vector (style.ImageVector.Vector is vector(768)): the built-in image model must return 768.</summary>
    public const int ImageDimensions = 768;
    /// <summary>
    /// Pictures looked at per index call, at most: descriptions (slow; the time budget stops them first) and look-alike
    /// vectors (fast). Large enough that pictures whose file is missing (skipped at once) cannot fill a batch.
    /// </summary>
    public const int Batch = 50;
    public const int VectorBatch = 200;
    /// <summary>An index call stops starting new pictures after this long (a CPU server takes 1-2 minutes per description).</summary>
    public static readonly TimeSpan IndexBudget = TimeSpan.FromSeconds(45);
    public const int MaxResults = 24;
    public static readonly string[] Sources = ["Photo", "Sketch", "Colorway", "Render"];

    /// <summary>
    /// Words searched by look: SigLIP word-to-picture scores are low even for right matches (about 0.03-0.13); below this
    /// the best match is reported as not close. Set on a small library (October 2026); revisit with more pictures.
    /// </summary>
    public const decimal WeakWordMatch = 0.03m;
}

/// <summary>How a search found its matches. LookAlike: the built-in image model (pixels); Description: the vision model's text.</summary>
public static class ImageSearchMethods
{
    public const string LookAlike = "lookAlike";
    public const string Description = "description";
}

/// <summary>The look-alike image model built into the API. IsAvailable false with the Problem when it is not installed or failed to load.</summary>
public sealed record ImageModelInfo(bool IsAvailable, string Name, string? Problem);

/// <summary>
/// An image embedding model run inside the API (ONNX Runtime): pixels in, a normalised vector of
/// <see cref="ImageSearchRules.ImageDimensions"/> numbers out. Nothing leaves the server.
/// </summary>
public interface IImageEmbeddingModel
{
    ImageModelInfo Info { get; }

    /// <exception cref="UnreadableImageException">The picture cannot be decoded.</exception>
    /// <exception cref="AiServiceException">The model is not installed or failed to load.</exception>
    float[] Embed(byte[] content);

    /// <summary>The model's text part (search by words in the same space as the pictures).</summary>
    ImageModelInfo TextInfo { get; }

    /// <exception cref="AiServiceException">The text part is not installed or failed to load.</exception>
    float[] EmbedText(string text);
}

public sealed record ImageIndexSourceCountDto(string Source, int Pictures, int Indexed, int Failed, int ToIndex);

/// <summary>
/// Look-alike vectors: whether the built-in model can run (installed and AI switched on), whether its text part is there
/// too (search by words), and how far the vectors are.
/// </summary>
public sealed record LookAlikeStatusDto(
    bool IsAvailable, string Model, string? Problem, IReadOnlyList<ImageIndexSourceCountDto> Sources, int Pictures, int Indexed, int Failed, int ToIndex,
    bool SupportsWords = false);

/// <summary>
/// Descriptions (Vision + Embedding jobs) and their counts at the top level, for search by words; LookAlike for search by
/// photo and "more like this".
/// </summary>
public sealed record ImageSearchStatusDto(
    AiProviderInfo Vision, AiProviderInfo Embedding, int Dimensions, IReadOnlyList<ImageIndexSourceCountDto> Sources,
    int Pictures, int Indexed, int Failed, int ToIndex, LookAlikeStatusDto LookAlike);

/// <summary>A picture to index. KnownText: its text when only the embedding model changed (re-embedded, not re-read).</summary>
public sealed record ImageToIndexDto(
    int StyleId, string Source, int ItemId, string ImageUrl, string? KnownText, string? Garment, string? Colours, string? Details,
    string? Description, int Remaining);

/// <summary>One picture read, ready to store (Embedding null with an Error when it could not be read).</summary>
public sealed record ImageReading(
    int StyleId, string Source, int ItemId, string ImageUrl, string? Garment, string? Colours, string? Details, string? Description,
    string? SearchText, float[]? Embedding, string? Error);

public sealed record ImageVectorToIndexDto(int StyleId, string Source, int ItemId, string ImageUrl, int Remaining);

/// <summary>One look-alike vector, ready to store (Vector null with an Error when the picture could not be decoded).</summary>
public sealed record ImageVectorReading(int StyleId, string Source, int ItemId, string ImageUrl, float[]? Vector, string? Error);

/// <summary>
/// Step: lookAlike or descriptions (what this call did), none when nothing was left. Remaining covers both steps.
/// Missing: pictures whose file is not in the image store (not stored as failed; tried again next time).
/// </summary>
public sealed record IndexImagesResultDto(int Indexed, int Failed, int Remaining, string Step = "none", int Missing = 0);

/// <summary>UseAiServer: search the descriptions (in-house AI server) instead of the built-in model.</summary>
public sealed record ImageSearchTextRequest(string? Query, string? Customer, bool UseAiServer = false);

/// <summary>How the vision model read the uploaded photo (shown so users see what was searched for).</summary>
public sealed record ImageReadingDto(string? Garment, IReadOnlyList<string> Colours, IReadOnlyList<string> Details, string? FabricLook, string? Description);

/// <summary>A style and its picture closest to the search. Similarity: 1 = same direction, 0 = unrelated.</summary>
public sealed record ImageMatchDto(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? ModelName, string? ProductTypeName, bool IsActive,
    string Source, string? ColorwayCode, string ImageUrl, string? Garment, string? Colours, string? Details, string? Description, decimal Similarity);

/// <summary>
/// Mode: photo, words or similar. Method: lookAlike or description. Reading: only for photo searches by description.
/// Weak: words searched by look whose best match is far (see <see cref="ImageSearchRules.WeakWordMatch"/>); the screen
/// then says no close match was found, and word-to-picture similarities are not shown as percentages.
/// </summary>
public sealed record ImageSearchResultDto(
    string Mode, ImageReadingDto? Reading, IReadOnlyList<ImageMatchDto> Matches, string Method = ImageSearchMethods.Description, bool Weak = false);

/// <summary>
/// What can run now: look-alike vectors (built-in model, AI switched on), the same model for words (its text part
/// installed), and descriptions (Vision and Embedding jobs).
/// </summary>
public sealed record ImageSearchReadiness(bool LookAlike, bool Descriptions, bool LookAlikeWords = false)
{
    /// <summary>A photo is searched by look: the built-in model can run and the user did not ask for the AI server.</summary>
    public bool CanUseLookAlike(bool useAiServer) => !useAiServer && LookAlike;
}

/// <summary>style.usp_ImageIndex_*, style.usp_ImageSearch, style.usp_ImageVector_* and style.usp_ImageVectorSearch (style.ai.procedures.sql).</summary>
public interface IImageSearchRepository
{
    Task<IReadOnlyList<ImageIndexSourceCountDto>> StatusAsync(string visionModel, string embeddingModel, CancellationToken ct = default);
    Task<IReadOnlyList<ImageToIndexDto>> ToIndexAsync(string visionModel, string embeddingModel, int take, string indexedBy, CancellationToken ct = default);
    Task<int> SaveAsync(IReadOnlyList<ImageReading> readings, string visionModel, string embeddingModel, string provider, string indexedBy, CancellationToken ct = default);

    /// <summary>By a vector, or (vector null) by the indexed picture of fromStyleId.</summary>
    Task<IReadOnlyList<ImageMatchDto>> SearchAsync(string embeddingModel, float[]? vector, int? fromStyleId, string? customer, int take, CancellationToken ct = default);

    Task<IReadOnlyList<ImageIndexSourceCountDto>> VectorStatusAsync(string model, CancellationToken ct = default);
    Task<IReadOnlyList<ImageVectorToIndexDto>> VectorsToIndexAsync(string model, int take, string indexedBy, CancellationToken ct = default);
    Task<int> SaveVectorsAsync(IReadOnlyList<ImageVectorReading> readings, string model, string indexedBy, CancellationToken ct = default);

    /// <summary>How a vector compares with every current picture of the model (style.usp_ImageVectorStats).</summary>
    Task<ImageVectorStatsDto> VectorStatsAsync(string model, float[] vector, CancellationToken ct = default);

    /// <summary>By a look-alike vector, or (vector null) by the picture of fromStyleId.</summary>
    Task<IReadOnlyList<ImageMatchDto>> SearchVectorsAsync(string model, float[]? vector, int? fromStyleId, string? customer, int take, CancellationToken ct = default);
}

/// <summary>
/// Image search on the Style Library, in-house only. Two kinds of vectors per picture (photo, sketch, colorway picture, render):
/// <list type="bullet">
/// <item><b>Look-alike</b>: the image model built into the API turns the pixels straight into a vector (well under a second
/// per picture on a CPU, no AI server). Used for search by photo and "more like this".</item>
/// <item><b>Description</b>: the Vision job writes a garment description and the Embedding job turns it into a vector (minutes
/// per picture on a CPU server). Used for search by words, and for photos when the built-in model is not installed.</item>
/// </list>
/// Indexing does the look-alike vectors first, then the descriptions. Uploaded photos are not stored.
/// </summary>
public sealed class ImageSearchService(
    IAiVisionClient vision, IAiEmbeddingClient embedding, IImageEmbeddingModel imageModel, IAiConnectionResolver connections,
    IImageSearchRepository repo, IStyleImageStore store, PictureTagger tagger)
{
    private const string LocalImagePrefix = "/api/v1/styles/images/";

    private const string Instruction = """
        You describe one garment picture from a sportswear manufacturer's style library (a product photo, a flat sketch or a
        rendering) so that look-alike garments can be found later. Describe only what is visible:
        - garment: what it is in a few words, with gender when clear (e.g. "men's hooded full-zip track jacket", "women's crew-neck
          short-sleeve T-shirt", "kids' jogger pants").
        - colours: the main colours, largest area first, in plain names (navy, white, heather grey, neon yellow).
        - details: visible design details, one per item: neckline or hood, sleeve type, closures, pockets, panels and colour blocking,
          stripes and tapes, print or logo placement, cuffs and hem, trims.
        - fabricLook: how the fabric looks (smooth woven, fleece, mesh, rib knit, quilted, denim...) when visible.
        - description: two sentences on the silhouette and its most distinctive features.
        Never name brands, and never read out logos or text: say "logo on left chest", not the brand.
        """;

    // ----- Status and indexing -----

    public async Task<ImageSearchReadiness> ReadinessAsync(CancellationToken ct = default)
    {
        var aiOn = await connections.AiEnabledAsync(ct);
        return new(imageModel.Info.IsAvailable && aiOn,
            (await vision.GetInfoAsync(ct)).IsConfigured && (await embedding.GetInfoAsync(ct)).IsConfigured,
            imageModel.TextInfo.IsAvailable && aiOn);
    }

    public async Task<ImageSearchStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var v = await vision.GetInfoAsync(ct);
        var e = await embedding.GetInfoAsync(ct);
        var all = Fill(await repo.StatusAsync(v.Model, e.Model, ct));
        var info = imageModel.Info;
        var aiOn = await connections.AiEnabledAsync(ct);
        var vectors = Fill(await repo.VectorStatusAsync(info.Name, ct));
        var lookAlike = new LookAlikeStatusDto(info.IsAvailable && aiOn, info.Name, aiOn ? info.Problem : AiBlocks.AiOff, vectors,
            vectors.Sum(c => c.Pictures), vectors.Sum(c => c.Indexed), vectors.Sum(c => c.Failed), vectors.Sum(c => c.ToIndex),
            imageModel.TextInfo.IsAvailable && aiOn);
        return new ImageSearchStatusDto(v, e, ImageSearchRules.Dimensions, all,
            all.Sum(c => c.Pictures), all.Sum(c => c.Indexed), all.Sum(c => c.Failed), all.Sum(c => c.ToIndex), lookAlike);

        static List<ImageIndexSourceCountDto> Fill(IReadOnlyList<ImageIndexSourceCountDto> counts)
            => ImageSearchRules.Sources.Select(s => counts.FirstOrDefault(c => c.Source == s) ?? new ImageIndexSourceCountDto(s, 0, 0, 0, 0)).ToList();
    }

    /// <summary>
    /// One round of indexing (about 45 s at most): look-alike vectors first while any are missing, then descriptions.
    /// Work done before a failure is kept. A picture that cannot be decoded or described is stored as failed so it is
    /// not tried again and again; a picture whose <b>file is missing</b> is not stored at all (the file may be copied
    /// later), only counted in Missing, and is tried again on the next round. Remaining counts what is left in both
    /// steps (descriptions only when their jobs are set), missing files included.
    /// </summary>
    public async Task<IndexImagesResultDto> IndexNextAsync(string indexedBy, CancellationToken ct = default)
    {
        var ready = await ReadinessAsync(ct);
        IndexImagesResultDto? vectors = null;
        var newVectors = ready.LookAlike ? await IndexVectorsAsync(indexedBy, ct) : null;
        // Picture tags (hood, closure, pattern, colours) from the vectors, milliseconds per picture: after every vector round.
        if (ready.LookAlikeWords) await tagger.TagAsync(ct);
        if (newVectors is { } v)
        {
            vectors = v;
            // Only missing files left in this step: go on with the descriptions instead of stopping here.
            if (v.Indexed + v.Failed > 0 || !ready.Descriptions)
                return v with { Remaining = v.Remaining + (ready.Descriptions ? await DescriptionsLeftAsync(ct) : 0) };
        }
        if (!ready.Descriptions) return new IndexImagesResultDto(0, 0, 0);
        var described = await IndexDescriptionsAsync(indexedBy, ct);
        return vectors is null ? described : described with { Missing = Math.Max(described.Missing, vectors.Missing), Remaining = described.Remaining + vectors.Remaining };
    }

    /// <summary>Null when every picture already has a current look-alike vector.</summary>
    private async Task<IndexImagesResultDto?> IndexVectorsAsync(string indexedBy, CancellationToken ct)
    {
        var model = imageModel.Info.Name;
        var batch = await repo.VectorsToIndexAsync(model, ImageSearchRules.VectorBatch, indexedBy, ct);
        if (batch.Count == 0) return null;

        var done = new List<ImageVectorReading>();
        var missing = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            foreach (var item in batch)
            {
                if (done.Count > 0 && clock.Elapsed > ImageSearchRules.IndexBudget) break;
                ImageVectorReading Failed(string error) => new(item.StyleId, item.Source, item.ItemId, item.ImageUrl, null, error);
                var file = await ReadFileAsync(item.ImageUrl, ct);
                if (file is null)
                {
                    missing++;
                    continue;
                }
                if (file.Kind is null)
                {
                    done.Add(Failed("The file is not a JPEG, PNG, GIF or WebP picture."));
                    continue;
                }
                try
                {
                    done.Add(new ImageVectorReading(item.StyleId, item.Source, item.ItemId, item.ImageUrl, await EmbedPictureAsync(file.Content, ct), null));
                }
                catch (UnreadableImageException)
                {
                    done.Add(Failed("The picture could not be decoded."));
                }
            }
        }
        finally
        {
            if (done.Count > 0) await repo.SaveVectorsAsync(done, model, indexedBy, CancellationToken.None);
        }
        var failed = done.Count(d => d.Error is not null);
        return new IndexImagesResultDto(done.Count - failed, failed, Math.Max(0, batch[0].Remaining - done.Count), "lookAlike", missing);
    }

    private async Task<IndexImagesResultDto> IndexDescriptionsAsync(string indexedBy, CancellationToken ct)
    {
        var v = await vision.GetInfoAsync(ct);
        var e = await embedding.GetInfoAsync(ct);
        var batch = await repo.ToIndexAsync(v.Model, e.Model, ImageSearchRules.Batch, indexedBy, ct);
        if (batch.Count == 0) return new IndexImagesResultDto(0, 0, 0);

        var done = new List<ImageReading>();
        var missing = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            foreach (var item in batch)
            {
                if (done.Count + missing > 0 && clock.Elapsed > ImageSearchRules.IndexBudget) break;
                if (await IndexOneAsync(item, ct) is { } reading) done.Add(reading);
                else missing++;
            }
        }
        finally
        {
            // Keep what was read even when a later picture fails (CPU servers are slow; nothing should be read twice).
            if (done.Count > 0) await repo.SaveAsync(done, v.Model, e.Model, v.Provider, indexedBy, CancellationToken.None);
        }
        var failed = done.Count(d => d.Error is not null);
        return new IndexImagesResultDto(done.Count - failed, failed, Math.Max(0, batch[0].Remaining - done.Count), "descriptions", missing);
    }

    private async Task<int> DescriptionsLeftAsync(CancellationToken ct)
        => (await repo.StatusAsync((await vision.GetInfoAsync(ct)).Model, (await embedding.GetInfoAsync(ct)).Model, ct)).Sum(c => c.ToIndex);

    /// <summary>The reading to store, or null when the picture's file is missing (nothing stored; tried again next round).</summary>
    private async Task<ImageReading?> IndexOneAsync(ImageToIndexDto item, CancellationToken ct)
    {
        ImageReading Failed(string error) => new(item.StyleId, item.Source, item.ItemId, item.ImageUrl, null, null, null, null, null, null, error);

        if (item.KnownText is { Length: > 0 } known)
            return new ImageReading(item.StyleId, item.Source, item.ItemId, item.ImageUrl, item.Garment, item.Colours, item.Details, item.Description,
                known, await EmbedAsync(known, ct), null);

        var file = await ReadFileAsync(item.ImageUrl, ct);
        if (file is null) return null;
        if (file.Kind is not { } kind || kind == ImageKind.Gif) return Failed("The file is not a JPEG, PNG or WebP picture.");
        var image = new ReferenceImage(file.Content, ConceptStudioRules.ContentType(ConceptStudioRules.Extension(kind)));
        var reading = await ReadAsync(image, ct);
        if (reading is null) return Failed("The vision model could not describe this picture.");
        var text = SearchText(reading);
        return new ImageReading(item.StyleId, item.Source, item.ItemId, item.ImageUrl, reading.Garment, Join(reading.Colours, ", ", 300),
            Join(reading.Details, "; ", 600), reading.Description, text, await EmbedAsync(text, ct), null);
    }

    // ----- Searching -----

    /// <summary>
    /// By look (the built-in model, about a second) when it can run and the AI server was not asked for; otherwise the
    /// Vision job reads the photo and the description vectors are searched. The photo is not stored.
    /// </summary>
    public async Task<ImageSearchResultDto> SearchByPhotoAsync(byte[] content, ImageKind kind, string? customer, bool useAiServer = false,
        CancellationToken ct = default)
    {
        if ((await ReadinessAsync(ct)).CanUseLookAlike(useAiServer))
        {
            float[] vector;
            try
            {
                vector = await EmbedPictureAsync(content, ct);
            }
            catch (UnreadableImageException ex)
            {
                throw new AiServiceException("The photo could not be read. Try another file.", ex);
            }
            var found = await repo.SearchVectorsAsync(imageModel.Info.Name, vector, null, customer, ImageSearchRules.MaxResults, ct);
            return new ImageSearchResultDto("photo", null, found, ImageSearchMethods.LookAlike);
        }

        var reading = await ReadAsync(new ReferenceImage(content, ConceptStudioRules.ContentType(ConceptStudioRules.Extension(kind))), ct)
                      ?? throw new AiServiceException("The vision model could not describe this photo. Try a clearer picture of one garment.");
        var model = (await embedding.GetInfoAsync(ct)).Model;
        var matches = await repo.SearchAsync(model, await EmbedAsync(SearchText(reading), ct), null, customer, ImageSearchRules.MaxResults, ct);
        return new ImageSearchResultDto("photo", reading, matches);
    }

    /// <summary>
    /// The built-in model's text part puts the words in the same space as the pictures (about 20 ms) when it can run and
    /// the AI server was not asked for; otherwise the Embedding job embeds the words and the descriptions are searched.
    /// </summary>
    public async Task<ImageSearchResultDto> SearchByWordsAsync(string query, string? customer, bool useAiServer = false, CancellationToken ct = default)
    {
        if (!useAiServer && (await ReadinessAsync(ct)).LookAlikeWords)
        {
            var vector = await Task.Run(() => imageModel.EmbedText(query), ct);
            var found = await repo.SearchVectorsAsync(imageModel.Info.Name, vector, null, customer, ImageSearchRules.MaxResults, ct);
            return new ImageSearchResultDto("words", null, found, ImageSearchMethods.LookAlike,
                Weak: found.Count == 0 || found[0].Similarity < ImageSearchRules.WeakWordMatch);
        }

        var model = (await embedding.GetInfoAsync(ct)).Model;
        var matches = await repo.SearchAsync(model, await EmbedAsync(query, ct), null, customer, ImageSearchRules.MaxResults, ct);
        return new ImageSearchResultDto("words", null, matches);
    }

    /// <summary>
    /// Styles that look like this one, from stored vectors (no AI call): look-alike vectors first, description vectors
    /// when the style has no look-alike vector yet.
    /// </summary>
    public async Task<ImageSearchResultDto> SimilarAsync(int styleId, string? customer, CancellationToken ct = default)
    {
        var byLook = await repo.SearchVectorsAsync(imageModel.Info.Name, null, styleId, customer, ImageSearchRules.MaxResults, ct);
        if (byLook.Count > 0) return new ImageSearchResultDto("similar", null, byLook, ImageSearchMethods.LookAlike);
        var model = (await embedding.GetInfoAsync(ct)).Model;
        return new ImageSearchResultDto("similar", null, await repo.SearchAsync(model, null, styleId, customer, ImageSearchRules.MaxResults, ct));
    }

    // ----- Helpers -----

    /// <summary>The built-in model on a worker thread (CPU work, about 0.2 s).</summary>
    private Task<float[]> EmbedPictureAsync(byte[] content, CancellationToken ct) => Task.Run(() => imageModel.Embed(content), ct);

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var vector = await embedding.EmbedAsync("image search", text, ct);
        if (vector.Length != ImageSearchRules.Dimensions)
            throw new AiServiceException(
                $"The embedding model returns {vector.Length} numbers per text; image search needs a model with {ImageSearchRules.Dimensions} " +
                "(e.g. qwen3-embedding:0.6b or bge-m3). Change the Embedding job in Settings > AI connections.");
        return vector;
    }

    /// <summary>The vision model's reading of one picture, or null when it describes nothing usable.</summary>
    internal async Task<ImageReadingDto?> ReadAsync(ReferenceImage image, CancellationToken ct)
    {
        var schema = Obj(new()
        {
            ["garment"] = Str(),
            ["colours"] = Arr(Str()),
            ["details"] = Arr(Str()),
            ["fabricLook"] = Str(),
            ["description"] = Str(),
        }, "garment", "colours", "details", "description");
        using var doc = Parse(await vision.DescribeJsonAsync("image search", Instruction, "Describe this garment picture.", image, schema, ct));
        return ToReading(doc.RootElement);
    }

    internal static ImageReadingDto? ToReading(JsonElement e)
    {
        var garment = Text(e, "garment", 100);
        var description = Text(e, "description", 1000);
        if (garment.Length == 0 && description.Length == 0) return null;
        var fabric = Text(e, "fabricLook", 100);
        return new ImageReadingDto(garment.Length == 0 ? null : garment, Texts(e, "colours", 8, 40).ToList(), Texts(e, "details", 12, 120).ToList(),
            fabric.Length == 0 ? null : fabric, description.Length == 0 ? null : description);
    }

    /// <summary>The text that is embedded: the same layout for library pictures and uploaded photos, so their vectors compare.</summary>
    internal static string SearchText(ImageReadingDto r)
    {
        var parts = new List<string>();
        if (r.Garment is { } g) parts.Add(g + ".");
        if (r.Colours.Count > 0) parts.Add("Colours: " + string.Join(", ", r.Colours) + ".");
        if (r.Details.Count > 0) parts.Add("Details: " + string.Join("; ", r.Details) + ".");
        if (r.FabricLook is { } f) parts.Add("Fabric: " + f + ".");
        if (r.Description is { } d) parts.Add(d);
        return Clip(string.Join(" ", parts), 2000);
    }

    /// <summary>A stored picture file; Kind null when the content is not a picture.</summary>
    private sealed record PictureFile(byte[] Content, ImageKind? Kind);

    /// <summary>The file behind a picture address, or null when there is no such file in the image store.</summary>
    private async Task<PictureFile?> ReadFileAsync(string url, CancellationToken ct)
    {
        if (!url.StartsWith(LocalImagePrefix, StringComparison.Ordinal) || store.Open(url[LocalImagePrefix.Length..]) is not { } file) return null;
        await using var stream = file.Content;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        return new PictureFile(bytes, ConceptStudioRules.DetectImage(bytes));
    }

    private static string? Join(IReadOnlyList<string> values, string separator, int max)
        => values.Count == 0 ? null : Clip(string.Join(separator, values), max);
}
