using System.Collections.Concurrent;
using System.Diagnostics;

namespace LT.ODM.Application.StyleAi;

// ----- Picture tags: what a style picture looks like, read without an AI server -----
//
// The image model built into the API (Marqo-FashionSigLIP) puts pictures and words in the same space. A picture's
// look-alike vector is compared with short descriptions of each tag value ("a hooded top", "a navy blue garment"); the
// closest value of an attribute is kept when it leads the next one by a clear margin. Tags are made during
// Image search > Index pictures, from the vectors already stored (milliseconds per picture), and searched as
// "attribute:value" (hood:hooded) by Styles, Smart search and the Library assistant.

/// <summary>One tag value: its code, the words the phrase list reads as it, and the descriptions it is compared with.</summary>
public sealed record PictureTagValue(string Value, string[] Words, string[] Prompts);

/// <summary>
/// An attribute (hood, closure, pattern, colour) and its values. MinMargin: how far the best value must lead the next one.
/// Tops: only for tops (not trousers, shorts, skirts...). Multiple: values within MinMargin of the best are kept too (colours).
/// </summary>
public sealed record PictureTagAttribute(string Name, PictureTagValue[] Values, float MinMargin, float MinScore, bool TopsOnly, bool Multiple = false);

public static class PictureTagVocabulary
{
    /// <summary>
    /// Change it whenever the attributes, prompts or thresholds change: every picture is then tagged again on the next
    /// indexing round. Thresholds were set on 8 pictures (October 2026) and should be checked once more pictures are indexed.
    /// </summary>
    public const string Version = "v1";

    public static readonly PictureTagAttribute[] Attributes =
    [
        new("hood",
        [
            new("hooded", ["hooded", "hood"], ["a hooded top", "a jacket with a hood", "a hoodie"]),
            new("noHood", [], ["a top without a hood", "a crew neck top", "a collarless top"]),
        ], MinMargin: 0.02f, MinScore: 0f, TopsOnly: true),
        new("closure",
        [
            new("fullZip", ["full zip", "full-zip", "zip-up", "zip up"], ["a full zip jacket", "a garment with a full-length front zipper"]),
            new("halfZip", ["half zip", "half-zip", "quarter zip", "quarter-zip"], ["a half zip top", "a quarter zip pullover"]),
            new("buttons", [], ["a garment with a button front", "a top fastened with buttons"]),
            new("noZip", [], ["a pullover top with no zipper", "a t-shirt"]),
        ], MinMargin: 0.02f, MinScore: 0f, TopsOnly: true),
        new("pattern",
        [
            new("solid", ["solid", "plain"], ["a plain solid colour garment", "a garment in one solid colour"]),
            new("striped", ["striped", "stripes", "stripe"], ["a striped garment", "a garment with stripes"]),
            new("print", ["printed", "graphic", "print"], ["a garment with a large graphic print", "a printed garment"]),
            new("colourBlock", ["colour-block", "color-block", "colour-blocked", "color-blocked", "colourblock", "colorblock"],
                ["a colour-blocked garment", "a garment with contrasting colour panels"]),
        ], MinMargin: 0.012f, MinScore: 0f, TopsOnly: false),
        new("colour",
        [
            new("black", ["black"], ["a black garment"]),
            new("white", ["white"], ["a white garment"]),
            new("grey", ["grey", "gray"], ["a grey garment"]),
            new("navy", ["navy"], ["a navy blue garment"]),
            new("blue", ["blue"], ["a bright blue garment"]),
            new("red", ["red"], ["a red garment"]),
            new("green", ["green", "olive"], ["a green garment"]),
            new("yellow", ["yellow"], ["a yellow garment"]),
            new("pink", ["pink"], ["a pink garment"]),
            new("beige", ["beige", "khaki", "tan"], ["a beige garment"]),
        ], MinMargin: 0.008f, MinScore: 0.02f, TopsOnly: false, Multiple: true),
    ];

    /// <summary>Every tag code (attribute:value).</summary>
    public static IEnumerable<string> Codes => Attributes.SelectMany(a => a.Values.Select(v => $"{a.Name}:{v.Value}"));

    /// <summary>Words (one or two) the phrase list reads as a tag, longest first.</summary>
    public static readonly IReadOnlyList<(string[] Words, string Tag)> Phrases = Attributes
        .SelectMany(a => a.Values.SelectMany(v => v.Words.Select(w => (Words: w.Split(' '), Tag: $"{a.Name}:{v.Value}"))))
        .OrderByDescending(p => p.Words.Length).ToList();

    private static readonly string[] BottomWords = ["PANT", "SHORT", "TIGHT", "SKIRT", "JOGGER", "LEGGING", "CAPRI", "BRIEF"];

    /// <summary>Trousers, shorts, tights, skirts...: no hood or closure tags. The bracketed detail is ignored ("T-SHIRT (SHORT SLEEVE)" is a top).</summary>
    public static bool IsBottom(string? productTypeName)
    {
        if (productTypeName is null) return false;
        var name = productTypeName.IndexOf('(') is var i and >= 0 ? productTypeName[..i] : productTypeName;
        return BottomWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The confident tags of one picture. prompts: each value's text vector (normalised); picture: the look-alike vector
    /// (normalised), so closeness is the dot product.
    /// </summary>
    public static IReadOnlyList<PictureTagDto> Tags(float[] picture, bool bottom, IReadOnlyDictionary<string, float[]> prompts)
    {
        var tags = new List<PictureTagDto>();
        foreach (var a in Attributes)
        {
            if (a.TopsOnly && bottom) continue;
            var scored = a.Values.Select(v => (v.Value, Score: Dot(picture, prompts[$"{a.Name}:{v.Value}"]))).OrderByDescending(s => s.Score).ToList();
            var (best, second) = (scored[0], scored[1]);
            if (best.Score < a.MinScore) continue;
            if (best.Score - second.Score >= a.MinMargin)
                tags.Add(new PictureTagDto(a.Name, best.Value, (decimal)best.Score, (decimal)(best.Score - second.Score)));
            else if (a.Multiple && second.Score >= a.MinScore && (scored.Count < 3 || second.Score - scored[2].Score >= a.MinMargin))
            {
                // Two close values (navy or blue): both, when together they lead the rest.
                var lead = (decimal)(second.Score - (scored.Count < 3 ? 0 : scored[2].Score));
                tags.Add(new PictureTagDto(a.Name, best.Value, (decimal)best.Score, lead));
                tags.Add(new PictureTagDto(a.Name, second.Value, (decimal)second.Score, lead));
            }
        }
        return tags;
    }

    private static float Dot(float[] a, float[] b)
    {
        var s = 0f;
        for (var i = 0; i < a.Length && i < b.Length; i++) s += a[i] * b[i];
        return s;
    }
}

public sealed record PictureTagDto(string Attribute, string Value, decimal Score, decimal Margin);

public sealed record PictureToTagDto(int ImageVectorId, float[] Vector, string? ProductTypeName);

public sealed record PictureTagsDto(int ImageVectorId, IReadOnlyList<PictureTagDto> Tags);

/// <summary>Styles per current tag.</summary>
public sealed record PictureTagCountDto(string Attribute, string Value, int Styles);

/// <summary>style.usp_ImageTag_* (db/procedures/style.tags.procedures.sql).</summary>
public interface IPictureTagRepository
{
    Task<IReadOnlyList<PictureToTagDto>> ToTagAsync(string model, string version, int take, CancellationToken ct = default);
    Task<int> SaveAsync(IReadOnlyList<PictureTagsDto> items, string version, CancellationToken ct = default);
    Task<IReadOnlyList<PictureTagCountDto>> SummaryAsync(CancellationToken ct = default);
}

/// <summary>Tags the pictures whose look-alike vectors are not tagged with the current vocabulary (needs the model's text part).</summary>
public sealed class PictureTagger(IImageEmbeddingModel model, IPictureTagRepository repo)
{
    private const int Batch = 1000;
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, float[]>> PromptCache = new();

    /// <summary>Pictures tagged in this call (0 when the text part is not installed or nothing is left).</summary>
    public async Task<int> TagAsync(CancellationToken ct = default)
    {
        if (!model.Info.IsAvailable || !model.TextInfo.IsAvailable) return 0;
        var name = model.Info.Name;
        var prompts = await Task.Run(() => Prompts(name), ct);
        var tagged = 0;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < ImageSearchRules.IndexBudget)
        {
            var batch = await repo.ToTagAsync(name, PictureTagVocabulary.Version, Batch, ct);
            if (batch.Count == 0) break;
            var items = batch.Select(p => new PictureTagsDto(p.ImageVectorId,
                PictureTagVocabulary.Tags(p.Vector, PictureTagVocabulary.IsBottom(p.ProductTypeName), prompts))).ToList();
            tagged += await repo.SaveAsync(items, PictureTagVocabulary.Version, ct);
            if (batch.Count < Batch) break;
        }
        return tagged;
    }

    /// <summary>The tag values' text vectors for the installed model (also used by Suggest from picture).</summary>
    public IReadOnlyDictionary<string, float[]> Prompts() => Prompts(model.Info.Name);

    /// <summary>Each value's descriptions averaged into one normalised text vector (computed once per model and vocabulary).</summary>
    private IReadOnlyDictionary<string, float[]> Prompts(string modelName)
        => PromptCache.GetOrAdd(modelName + "|" + PictureTagVocabulary.Version, _ =>
            PictureTagVocabulary.Attributes.SelectMany(a => a.Values.Select(v => (Key: $"{a.Name}:{v.Value}", Vector: Average(v.Prompts.Select(model.EmbedText).ToList()))))
                .ToDictionary(x => x.Key, x => x.Vector));

    internal static float[] Average(List<float[]> vectors)
    {
        var sum = new float[vectors[0].Length];
        foreach (var v in vectors)
            for (var i = 0; i < sum.Length; i++) sum[i] += v[i];
        var norm = MathF.Sqrt(sum.Sum(x => x * x));
        for (var i = 0; i < sum.Length; i++) sum[i] /= norm;
        return sum;
    }
}
