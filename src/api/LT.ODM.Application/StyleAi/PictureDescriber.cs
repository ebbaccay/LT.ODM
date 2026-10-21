using System.Collections.Concurrent;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.StyleAi;

// ----- Suggest from picture: a draft of a new style's fields from its sketch or photo -----
//
// The image model built into the API (Marqo-FashionSigLIP) compares the picture with short descriptions of each value.
// Measured on the library's 135 indexed pictures (2026-10-21, 124 of them sketches; "always the most common answer" in brackets):
//   garment        70% right, 77% when it leads by 0.02 (18%)      -> suggested
//   sleeve (tops)  82%, 94% when it leads by 0.02 (42%)            -> suggested
//   gender         55-60% (74%: always women's)                    -> NOT read from the picture
//   knit / woven   56% (59%); the 5 closest-looking styles 71%     -> a hint from similar-looking styles only
//   main fibre     the 5 closest-looking styles 76% (68%)          -> a hint only (mostly the library's usual fabric)
// Hood, closure, pattern and colour are the picture tags (PictureTagVocabulary).

public sealed record DescribePictureRequest(string ImageUrl, bool IsSketch, string? CustomerCode);

/// <summary>
/// A draft for a new style from its picture. Confident: the value leads the next one clearly (measured thresholds);
/// unsure values are shown but not offered as ready to use. Description: a draft in the library's usual form.
/// Hints come from the closest-looking library styles, not from the picture itself.
/// </summary>
public sealed record PictureDescriptionDto(
    PictureGuessDto? Garment, IReadOnlyList<PictureProductTypeOptionDto> ProductTypes, PictureGuessDto? Sleeve, IReadOnlyList<PictureTagDto> Tags,
    string? Description, PictureHintDto? Weave, PictureHintDto? Fabric, IReadOnlyList<PictureNeighbourDto> SimilarStyles);

/// <summary>
/// A product type for the garment read, the ones this customer uses most first. Offered to pick from, never set alone:
/// the picture cannot tell shorts 1/2 from 1/4 or a track top from a jacket (the first was the real one for 45% of 40
/// library sketches).
/// </summary>
public sealed record PictureProductTypeOptionDto(string ProductTypeCode, string Name, int CustomerStyles);

/// <summary>Value: a garment (TEE, PANTS...) or sleeve (SHORT, LONG, NONE); Margin: how far it leads the next value.</summary>
public sealed record PictureGuessDto(string Value, decimal Margin, bool Confident);

/// <summary>What most of the closest-looking styles have (Value: KNT / WVN, or a fibre text), Votes of Of.</summary>
public sealed record PictureHintDto(string Value, int Votes, int Of);

public sealed record PictureNeighbourDto(int StyleId, string StyleNo, string CustomerCode, string SeasonCode, string? ProductTypeName, decimal Similarity);

public sealed record PictureProductType(string ProductTypeCode, string Name, string? Category, int CustomerStyles, int BrandStyles, int Styles);

public sealed record PictureNeighbourStyle(
    int StyleId, string StyleNo, string CustomerCode, string SeasonCode, string? WeaveTypeCode, string? Gender, string? Description, string? ProductTypeName);

/// <summary>style.usp_PictureDescribe_Context (style.bom-suggest.procedures.sql).</summary>
public interface IPictureDescribeRepository
{
    Task<(IReadOnlyList<PictureProductType> ProductTypes, IReadOnlyList<PictureNeighbourStyle> Neighbours)> ContextAsync(
        string? customerCode, IReadOnlyList<int> styleIds, CancellationToken ct = default);
}

public static class PictureDescribeRules
{
    /// <summary>A garment or sleeve is offered as ready to use when it leads the next value by this much (see the measurements above).</summary>
    public const float ConfidentMargin = 0.02f;
    /// <summary>Closest-looking styles asked for the hints.</summary>
    public const int Neighbours = 5;

    /// <summary>Garments (fn_GarmentCategory's codes) and how they are described to the model.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Garments = new Dictionary<string, string[]>
    {
        ["TEE"] = ["a t-shirt", "a short sleeve tee"], ["POLO"] = ["a polo shirt with a collar and buttons"],
        ["TANK"] = ["a sleeveless tank top", "a singlet"], ["SHIRT"] = ["a button-up shirt", "a woven shirt with a collar"],
        ["HOODIE"] = ["a hoodie", "a hooded sweatshirt"], ["PULLOVER"] = ["a sweatshirt", "a crew neck pullover"],
        ["JACKET"] = ["a jacket", "a zip-up track jacket"], ["VEST"] = ["a sleeveless vest", "a gilet"],
        ["PANTS"] = ["long pants", "trousers", "joggers"], ["SHORTS"] = ["shorts"], ["TIGHTS"] = ["leggings", "tights"],
        ["SKIRT"] = ["a skirt"], ["DRESS"] = ["a dress"], ["BRA"] = ["a sports bra"], ["TOP"] = ["a women's top", "a crop top"],
    };

    public static readonly IReadOnlyDictionary<string, string[]> Sleeves = new Dictionary<string, string[]>
    {
        ["SHORT"] = ["a short sleeve top"], ["LONG"] = ["a long sleeve top"], ["NONE"] = ["a sleeveless top"],
    };

    /// <summary>Garments that have sleeves to read (bottoms, dresses, bras and sleeveless garments do not).</summary>
    public static readonly string[] SleevedTops = ["TEE", "POLO", "SHIRT", "HOODIE", "PULLOVER", "JACKET", "TOP"];
    public static readonly string[] Bottoms = ["PANTS", "SHORTS", "TIGHTS", "SKIRT"];

    /// <summary>Garment words for the draft description.</summary>
    public static readonly IReadOnlyDictionary<string, string> GarmentWords = new Dictionary<string, string>
    {
        ["TEE"] = "T-SHIRT", ["POLO"] = "POLO SHIRT", ["TANK"] = "TANK TOP", ["SHIRT"] = "SHIRT", ["HOODIE"] = "HOODIE", ["PULLOVER"] = "SWEATSHIRT",
        ["JACKET"] = "JACKET", ["VEST"] = "VEST", ["PANTS"] = "PANTS", ["SHORTS"] = "SHORTS", ["TIGHTS"] = "TIGHTS", ["SKIRT"] = "SKIRT",
        ["DRESS"] = "DRESS", ["BRA"] = "SPORTS BRA", ["TOP"] = "TOP",
    };

    /// <summary>Product types offered for a garment.</summary>
    public const int ProductTypeOptions = 3;

    /// <summary>Sketches read better described as sketches (garment 70% vs 66% right).</summary>
    public static string Phrase(string text, bool isSketch) => isSketch ? "a technical flat sketch of " + text : text;
}

/// <summary>
/// Suggest from picture: garment (and the customer's product type for it), sleeve length, picture tags and a draft
/// description, plus knit / woven and fabric hints from the closest-looking library styles. Runs inside the API in well
/// under a second; nothing is sent anywhere.
/// </summary>
public sealed class PictureDescriber(
    IImageEmbeddingModel model, ImageSearchService imageSearch, IImageSearchRepository vectors, PictureTagger tagger, IPictureDescribeRepository repo,
    IStyleImageStore images)
{
    private const string ImagePrefix = "/api/v1/styles/images/";
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, float[]>> PhraseCache = new();

    /// <exception cref="AiServiceException">The model (or its text part) cannot run, or the picture cannot be read.</exception>
    public async Task<PictureDescriptionDto> DescribeAsync(DescribePictureRequest request, CancellationToken ct = default)
    {
        var ready = await imageSearch.ReadinessAsync(ct);
        if (!ready.LookAlike || !ready.LookAlikeWords)
            throw new AiServiceException("The built-in image model (with its text part) is not installed, or AI features are off. Ask an administrator.");
        if (!request.ImageUrl.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase) || images.Open(request.ImageUrl[ImagePrefix.Length..].Split('?')[0]) is not { } file)
            throw new AiServiceException("The picture was not found. Upload it again.");

        byte[] content;
        await using (var stream = file.Content)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }
        float[] picture;
        try
        {
            picture = await Task.Run(() => model.Embed(content), ct);
        }
        catch (ConceptStudio.UnreadableImageException ex)
        {
            throw new AiServiceException("The picture could not be read. Try another file.", ex);
        }

        return await DescribeVectorAsync(picture, request.IsSketch, request.CustomerCode, ct);
    }

    /// <summary>The draft for a picture already turned into a look-alike vector (normalised) by the built-in model.</summary>
    public async Task<PictureDescriptionDto> DescribeVectorAsync(float[] picture, bool isSketch, string? customerCode, CancellationToken ct = default)
    {
        var garment = Pick(picture, Phrases("garment", PictureDescribeRules.Garments, isSketch));
        var sleeve = garment is not null && PictureDescribeRules.SleevedTops.Contains(garment.Value)
            ? Pick(picture, Phrases("sleeve", PictureDescribeRules.Sleeves, isSketch)) : null;
        var tags = PictureTagVocabulary.Tags(picture, garment is not null && PictureDescribeRules.Bottoms.Contains(garment.Value), tagger.Prompts());

        var near = (await vectors.SearchVectorsAsync(model.Info.Name, picture, null, null, PictureDescribeRules.Neighbours, ct)).ToList();
        var (productTypes, neighbours) = await repo.ContextAsync(customerCode, near.Select(n => n.StyleId).ToList(), ct);

        var options = garment is { Confident: true } ? ProductTypes(productTypes, garment.Value, sleeve) : [];
        var weave = Majority(neighbours.Select(n => n.WeaveTypeCode));
        var fabric = Majority(neighbours.Select(n => FabricSpecReader.Read(n.Description) is { Fibres.Count: > 0 } f
            ? new FabricSpec([f.Fibres.OrderByDescending(x => x.Percent).First()], null, null).FibresText : null));

        return new PictureDescriptionDto(garment, options, sleeve, tags, Draft(garment, sleeve, tags), weave, fabric,
            near.Select(n => new PictureNeighbourDto(n.StyleId, n.StyleNo, n.CustomerCode, n.SeasonCode, n.ProductTypeName, n.Similarity)).ToList());
    }

    /// <summary>Each value's phrases averaged into one normalised text vector, computed once per model.</summary>
    private IReadOnlyDictionary<string, float[]> Phrases(string set, IReadOnlyDictionary<string, string[]> values, bool isSketch)
        => PhraseCache.GetOrAdd($"{model.Info.Name}|{set}|{isSketch}", _ => values.ToDictionary(kv => kv.Key,
            kv => PictureTagger.Average(kv.Value.Select(p => model.EmbedText(PictureDescribeRules.Phrase(p, isSketch))).ToList())));

    private static PictureGuessDto? Pick(float[] picture, IReadOnlyDictionary<string, float[]> phrases)
    {
        var scored = phrases.Select(kv => (kv.Key, Score: Dot(picture, kv.Value))).OrderByDescending(x => x.Score).ToList();
        if (scored.Count < 2) return null;
        var margin = scored[0].Score - scored[1].Score;
        return new PictureGuessDto(scored[0].Key, Math.Round((decimal)margin, 4), margin >= PictureDescribeRules.ConfidentMargin);
    }

    /// <summary>
    /// Product types for the garment: of those named like it, the ones this customer uses most (then its brand, then
    /// everyone), a name with the sleeve read first ("T-SHIRT (LONG SLEEVE)" for a long sleeve tee). Types nobody uses are left out.
    /// </summary>
    private static List<PictureProductTypeOptionDto> ProductTypes(IReadOnlyList<PictureProductType> types, string garment, PictureGuessDto? sleeve)
    {
        var sleeveWord = sleeve is { Confident: true } ? sleeve.Value switch { "SHORT" => "SHORT SLEEVE", "LONG" => "LONG SLEEVE", _ => null } : null;
        return types.Where(t => t.Category == garment && t.Styles > 0)
            .OrderByDescending(t => sleeveWord is not null && t.Name.Contains(sleeveWord, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.CustomerStyles).ThenByDescending(t => t.BrandStyles).ThenByDescending(t => t.Styles).ThenBy(t => t.ProductTypeCode, StringComparer.Ordinal)
            .Take(PictureDescribeRules.ProductTypeOptions)
            .Select(t => new PictureProductTypeOptionDto(t.ProductTypeCode, t.Name, t.CustomerStyles)).ToList();
    }

    private static PictureHintDto? Majority(IEnumerable<string?> values)
    {
        var list = values.ToList();
        var best = list.Where(v => v is not null).GroupBy(v => v!).OrderByDescending(g => g.Count()).FirstOrDefault();
        return best is null ? null : new PictureHintDto(best.Key, best.Count(), list.Count);
    }

    /// <summary>A draft in the library's usual form: "T-SHIRT; SHORT SLEEVE; SOLID; NAVY". Only confident parts.</summary>
    private static string? Draft(PictureGuessDto? garment, PictureGuessDto? sleeve, IReadOnlyList<PictureTagDto> tags)
    {
        var parts = new List<string>();
        if (garment is { Confident: true }) parts.Add(PictureDescribeRules.GarmentWords.GetValueOrDefault(garment.Value, garment.Value));
        if (sleeve is { Confident: true }) parts.Add(sleeve.Value switch { "SHORT" => "SHORT SLEEVE", "LONG" => "LONG SLEEVE", _ => "SLEEVELESS" });
        string? Tag(string attribute) => tags.FirstOrDefault(t => t.Attribute == attribute)?.Value;
        if (Tag("hood") == "hooded") parts.Add("HOODED");
        parts.Add(Tag("closure") switch { "fullZip" => "FULL ZIP", "halfZip" => "HALF ZIP", "buttons" => "BUTTON FRONT", _ => "" });
        parts.Add(Tag("pattern") switch { "solid" => "SOLID", "striped" => "STRIPED", "print" => "PRINTED", "colourBlock" => "COLOUR BLOCK", _ => "" });
        var colours = tags.Where(t => t.Attribute == "colour").Select(t => t.Value.ToUpperInvariant()).ToList();
        if (colours.Count > 0) parts.Add(string.Join("/", colours));
        var text = string.Join("; ", parts.Where(p => p.Length > 0));
        return text.Length == 0 ? null : text;
    }

    private static float Dot(float[] a, float[] b)
    {
        var s = 0f;
        for (var i = 0; i < a.Length && i < b.Length; i++) s += a[i] * b[i];
        return s;
    }
}
