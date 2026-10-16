using System.Collections.Concurrent;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleAi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>
/// Reads a garment's shape from its sketch with the image model built into the API (Marqo-FashionSigLIP): the sketch's
/// vector is compared with short descriptions of each feature value, as the picture tags do, and a value is kept only
/// when it leads the next one by a clear margin. Which features apply depends on the garment type, also read from the
/// sketch (the product type helps when the model is unsure: product types in the library are not always right).
/// Front and back views: counted from the pixels, as separate drawings of similar size (the model cannot tell).
/// Margins were set on 81 library sketches (October 2026): sleeves, neckline, shorts / full length and front-and-back
/// came out right on nearly all; dress length is the weakest.
/// </summary>
public sealed class SketchShapeReader(IImageEmbeddingModel model, IAiConnectionResolver connections, ILogger<SketchShapeReader> logger)
    : ISketchShapeReader
{
    /// <summary>Warms the reader up in the background once the API has started (the first render page then opens quickly).</summary>
    public sealed class WarmUp(SketchShapeReader reader, ILogger<WarmUp> log) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();   // never delay the API's start
            try { await reader.WarmUpAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "The sketch shape reader could not be warmed up; the first render page loads it instead.");
            }
        }
    }

    private sealed record Value(string Code, string Words, string[] Prompts);
    private sealed record Feature(string Name, string[] AppliesTo, float MinMargin, Value[] Values);

    private static readonly Value[] Types =
    [
        new("top", "", ["a sketch of a top", "a sketch of a jacket", "a t-shirt", "a sweatshirt"]),
        new("bottom", "", ["a sketch of trousers", "a sketch of shorts", "pants"]),
        new("dress", "", ["a sketch of a dress", "a dress"]),
        new("skirt", "", ["a sketch of a skirt", "a skirt"]),
    ];
    private const float MinTypeMargin = 0.01f;

    /// <summary>
    /// The print, read on every garment type. All values compete, but only those FLUX can use and the model read
    /// reliably add words (Words), each when it leads by its own margin. Set on 81 library sketches (2026-10-16):
    /// floral, animal (zebra, snakeskin, cow) and stripes came out right; camouflage, tie-dye, geometric and graphic were
    /// mostly wrong (a cheetah print read as camouflage, colour-blocked panels too), and a "colour-blocked" value fired
    /// on every sketch showing two colorways, so those only compete. Plain garments get no print words.
    /// </summary>
    private static readonly (string Code, string? Words, float MinMargin, string[] Prompts)[] Prints =
    [
        ("solid", null, 0, ["a plain solid colour garment", "a garment in one solid colour with no print"]),
        ("floral", "a floral print", 0.015f, ["a garment with a floral print", "a shirt with a flower pattern"]),
        ("stripes", "stripes", 0.02f, ["a garment with stripes", "a striped shirt", "a pinstripe garment"]),
        ("checks", "a check pattern", 0.015f, ["a garment with a check pattern", "a plaid shirt"]),
        ("animal", "an animal print", 0.012f,
            ["a garment with a zebra print", "a garment with a leopard print", "a garment with a snakeskin print", "a garment with a cow print", "a garment with a cheetah print"]),
        ("camo", null, 0, ["a garment with a military camouflage print in green and brown"]),
        ("tiedye", null, 0, ["a tie-dye garment"]),
        ("geometric", null, 0, ["a garment with a geometric print"]),
        ("graphic", null, 0, ["a garment with a large graphic print on the chest", "a t-shirt with a big logo graphic"]),
    ];

    private static readonly Feature[] Features =
    [
        new("sleeves", ["top", "dress"], 0.015f,
        [
            new("long", "long sleeves", ["a sketch of a garment with long sleeves", "a long-sleeve top"]),
            new("short", "short sleeves", ["a sketch of a garment with short sleeves", "a short-sleeve t-shirt"]),
            new("none", "no sleeves", ["a sketch of a sleeveless garment", "a sleeveless tank top", "a sleeveless dress with straps"]),
        ]),
        new("neck", ["top", "dress"], 0.015f,
        [
            new("high", "a high neck", ["a garment with a high mock neck", "a turtleneck", "a jacket with a high stand-up collar"]),
            new("hood", "a hood", ["a hooded garment", "a hoodie"]),
            new("crew", "a crew neck", ["a crew neck garment", "a round neckline"]),
            new("vneck", "a V-neck", ["a v-neck garment"]),
            new("collar", "a shirt collar", ["a garment with a shirt collar", "a polo shirt collar"]),
        ]),
        new("legs", ["bottom"], 0.012f,
        [
            new("shorts", "short legs above the knee", ["a sketch of shorts", "short pants above the knee"]),
            new("full", "full-length legs", ["a sketch of full-length trousers", "long pants down to the ankle"]),
        ]),
        new("length", ["dress", "skirt"], 0.012f,
        [
            new("mini", "mini length", ["a mini dress above the knee"]),
            new("knee", "knee length", ["a knee-length dress"]),
            new("midi", "midi length", ["a midi dress below the knee"]),
            new("maxi", "maxi length", ["a maxi dress down to the ankle"]),
        ]),
    ];

    /// <summary>Sketch files never change (a new upload gets a new name), so a reading is kept for the API's lifetime.</summary>
    private static readonly ConcurrentDictionary<string, SketchShapeDto> Cache = new();
    private const int MaxCached = 2000;
    private Dictionary<string, float[]>? _prompts;
    private readonly Lock _promptLock = new();

    public async Task<SketchShapeDto> ReadAsync(string cacheKey, Func<CancellationToken, Task<byte[]?>> sketch, string? productTypeName,
        CancellationToken ct = default)
    {
        if (Cache.TryGetValue(cacheKey, out var known)) return known;
        if (!model.Info.IsAvailable || !model.TextInfo.IsAvailable || !await connections.AiEnabledAsync(ct)) return SketchShapeDto.None;
        if (await sketch(ct) is not { } bytes) return SketchShapeDto.None;
        try
        {
            var shape = Read(model.Embed(bytes), bytes, productTypeName);
            if (Cache.Count < MaxCached) Cache[cacheKey] = shape;
            return shape;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A sketch that cannot be read only loses the shape words; the render itself goes on.
            logger.LogWarning(ex, "Sketch {Sketch} could not be read for its shape.", cacheKey);
            return SketchShapeDto.None;
        }
    }

    /// <summary>
    /// Loads the image model and embeds the feature descriptions ahead of the first render page (3.4 s cold on a laptop,
    /// against 0.6 s per sketch once warm). Does nothing when the model is not installed or AI is off.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        if (!model.Info.IsAvailable || !model.TextInfo.IsAvailable || !await connections.AiEnabledAsync(ct)) return;
        Prompts();
    }

    private SketchShapeDto Read(float[] picture, byte[] bytes, string? productTypeName)
    {
        var prompts = Prompts();
        var (type, typeMargin) = Best(picture, Types, "type", prompts);
        if (typeMargin < MinTypeMargin) type = TypeFromProductType(productTypeName) ?? type;

        var words = new List<string>();
        foreach (var f in Features.Where(f => f.AppliesTo.Contains(type)))
        {
            var (code, margin) = Best(picture, f.Values, f.Name, prompts);
            if (margin >= f.MinMargin) words.Add(f.Values.First(v => v.Code == code).Words);
        }
        var prints = Prints.Select(v => (v.Words, v.MinMargin, Score: Dot(picture, prompts[$"print:{v.Code}"]))).OrderByDescending(v => v.Score).ToList();
        if (prints[0].Words is { } print && prints[0].Score - prints[1].Score >= prints[0].MinMargin) words.Add(print);
        return new SketchShapeDto(words, CountDrawings(bytes) == 2);
    }

    private static (string Code, float Margin) Best(float[] picture, Value[] values, string group, Dictionary<string, float[]> prompts)
    {
        var scored = values.Select(v => (v.Code, Score: Dot(picture, prompts[$"{group}:{v.Code}"]))).OrderByDescending(s => s.Score).ToList();
        return (scored[0].Code, scored[0].Score - scored[1].Score);
    }

    public static string? TypeFromProductType(string? productTypeName)
    {
        if (string.IsNullOrWhiteSpace(productTypeName)) return null;
        var t = productTypeName.ToUpperInvariant();
        if (t.Contains("DRESS")) return "dress";
        if (t.Contains("SKIRT")) return "skirt";
        return PictureTagVocabulary.IsBottom(productTypeName) ? "bottom" : "top";
    }

    /// <summary>Each value's descriptions, embedded once and averaged (the text part of the same model).</summary>
    private Dictionary<string, float[]> Prompts()
    {
        lock (_promptLock)
        {
            if (_prompts is not null) return _prompts;
            var all = Types.Select(v => ($"type:{v.Code}", v.Prompts))
                .Concat(Features.SelectMany(f => f.Values.Select(v => ($"{f.Name}:{v.Code}", v.Prompts))))
                .Concat(Prints.Select(v => ($"print:{v.Code}", v.Prompts)));
            return _prompts = all.ToDictionary(x => x.Item1, x => Average(x.Prompts.Select(model.EmbedText)));
        }
    }

    /// <summary>Separate garment drawings on the sketch (2 = front and back drawn apart); see <see cref="Files.SketchDrawings"/>.</summary>
    public static int CountDrawings(byte[] bytes)
    {
        using var source = SKBitmap.Decode(bytes);
        return source is null ? 0 : Files.SketchDrawings.Find(source).Count;
    }

    private static float[] Average(IEnumerable<float[]> vectors)
    {
        var list = vectors.ToList();
        var sum = new float[list[0].Length];
        foreach (var v in list)
            for (var i = 0; i < sum.Length; i++) sum[i] += v[i];
        var norm = MathF.Sqrt(sum.Sum(x => x * x));
        for (var i = 0; i < sum.Length; i++) sum[i] /= norm;
        return sum;
    }

    private static float Dot(float[] a, float[] b)
    {
        float s = 0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
