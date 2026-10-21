using System.Globalization;
using System.Text.RegularExpressions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;

namespace LT.ODM.Application.StyleLibrary;

// ----- Contracts -----

/// <summary>
/// A suggested BOM for a style: the reference styles it was built from, what was read from the style's description,
/// and one line per material to add (the first option is the suggestion, the rest are alternatives).
/// </summary>
public sealed record BomSuggestionDto(
    int StyleId, FabricReadingDto? Fabric, bool UsedPicture, string? PictureNote, IReadOnlyList<BomReferenceDto> References,
    IReadOnlyList<SuggestedBomLineDto> Lines, IReadOnlyList<string> Notes);

/// <summary>What the style's description says about its fabric (null parts were not found).</summary>
public sealed record FabricReadingDto(string? Fibres, bool? Recycled, decimal? WeightGsm, string? Weave);

/// <summary>
/// A reference style. Reasons: family, productType, garment, weave, gender, customer, brand, fabric, look.
/// OtherBrand: know-how only; its labels, packaging and artwork are never suggested.
/// </summary>
public sealed record BomReferenceDto(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? ModelName, bool OtherBrand, int Score,
    IReadOnlyList<string> Reasons, decimal? LookSimilarity);

/// <summary>
/// One line to add. Basis: references (shared by the reference styles) or description (a fabric matching the
/// description). Recommended lines start ticked. AlreadyOnBom: the style has the material already (never ticked).
/// </summary>
public sealed record SuggestedBomLineDto(
    string Key, string? ContentClassCode, string? ContentClassName, string? MaterialTypeCode, string? MaterialTypeName, int Confidence,
    bool Recommended, string Basis, bool AlreadyOnBom, IReadOnlyList<BomOptionDto> Options);

/// <summary>
/// A material for a line: usual part, suppliers and consumption on the reference styles. UsedBy: how many reference
/// styles use it (of OfReferences). FabricMatch: 0-100 against the description's fabric (fabrics only).
/// </summary>
public sealed record BomOptionDto(
    int MaterialId, string MaterialCode, string? Description, int? PartNo, string? NominatedSupplierCode, string? NominatedSupplierName,
    string? SupplierCode, string? SupplierName, decimal? LcoConsumption, decimal? BrandConsumption, string? UomCode, int UsedBy, int OfReferences,
    int? FabricMatch, bool OtherBrand);

/// <summary>Lines picked on the suggestion, added to the style as ordinary BOM lines (for all its colorways).</summary>
public sealed record ApplyBomSuggestionRequest(IReadOnlyList<SaveBomLineRequest> Lines);

// ----- Reads (style.bom-suggest.procedures.sql) -----

public sealed record BomSuggestTarget(
    int StyleId, string CustomerCode, string? BrandCode, string? Category, string? Description, string? ModelName, string? ProductTypeCode,
    string? ProductTypeName, string? WeaveTypeCode, string? Gender, string? SketchUrl, string? ImageUrl, int BomLines);

public sealed record BomSuggestCandidate(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? Description, string? ModelName, string? ProductTypeName,
    string? WeaveTypeCode, string? Gender, bool IsActive, bool SameCustomer, bool SameBrand, bool Family, bool SameProductType, bool SameGarment,
    bool SameWeave, bool SameGender, int HeaderScore);

public sealed record BomSuggestSourceLine(
    int StyleId, int BomLineId, int LineSeq, int? PartNo, int MaterialId, string MaterialCode, string? MaterialDescription, string? MaterialTypeCode,
    string? MaterialTypeName, string? ContentClassCode, string? ContentClassName, int? ContentClassSort, string? NominatedSupplierCode,
    string? NominatedSupplierName, string? SupplierCode, string? SupplierName, decimal? LcoConsumption, decimal? BrandConsumption, string? UomCode);

public sealed record BomSuggestFabric(
    int MaterialId, string MaterialCode, string? Description, string? Composition, decimal? RecycledPct, decimal? WeightGsm, string? Construction,
    int Styles, int CustomerStyles, int BrandStyles, string? MaterialTypeCode, string? UomCode, string? NominatedSupplierCode,
    string? NominatedSupplierName, string? SupplierCode, string? SupplierName, decimal? MedianConsumption);

public interface IBomSuggestionRepository
{
    /// <summary>The style itself and up to <paramref name="take"/> candidate reference styles; null when the style does not exist.</summary>
    Task<(BomSuggestTarget Target, IReadOnlyList<BomSuggestCandidate> Candidates)?> CandidatesAsync(int styleId, int take, CancellationToken ct = default);

    Task<IReadOnlyList<BomSuggestSourceLine>> LinesAsync(IReadOnlyList<int> styleIds, CancellationToken ct = default);

    /// <summary>Every fabric in the library (may be cached for a few minutes: it changes only with the library).</summary>
    Task<IReadOnlyList<BomSuggestFabric>> FabricsAsync(string customerCode, CancellationToken ct = default);
}

// ----- Fabric descriptions -----

/// <summary>Fibres (percent, recycled), weight and weave read from a fabric or style description.</summary>
public sealed record FabricSpec(IReadOnlyList<(string Fibre, decimal Percent, bool Recycled)> Fibres, decimal? WeightGsm, string? Weave)
{
    public bool IsEmpty => Fibres.Count == 0 && WeightGsm is null && Weave is null;
    public bool? Recycled => Fibres.Count == 0 ? null : Fibres.Any(f => f.Recycled);

    public string? FibresText => Fibres.Count == 0 ? null
        : string.Join(" · ", Fibres.Select(f => $"{f.Fibre}{(f.Recycled ? " (recycled)" : "")} {f.Percent.ToString("0.#", CultureInfo.InvariantCulture)}%"));
}

/// <summary>
/// Reads library descriptions such as "MALE; 100%POLYESTER(100%RECYCLED); Solid; 101.0 G/SQM; T-SHIRT",
/// "Men's 53% Cotton 47% Polyester WOVEN QUILTED JACKET", "100%Recycle Polyester,Single jersey,FD 60D/60F" or
/// "87%POLYESTER(100%RECYCLED)/13%ELASTANE". Rules only; unknown fibre words are skipped.
/// </summary>
public static partial class FabricSpecReader
{
    private static readonly (string Fibre, string[] Words)[] FibreWords =
    [
        ("Polyester", ["POLYESTER", "POLY", "PES", "PET", "PE"]),
        ("Cotton", ["COTTON", "CO", "CTN"]),
        ("Elastane", ["ELASTANE", "SPANDEX", "LYCRA", "EA", "EL"]),
        ("Nylon", ["NYLON", "POLYAMIDE", "PA"]),
        ("Viscose", ["VISCOSE", "RAYON", "CV"]),
        ("Modal", ["MODAL"]),
        ("Lyocell", ["LYOCELL", "TENCEL"]),
        ("Wool", ["WOOL", "MERINO"]),
        ("Acrylic", ["ACRYLIC"]),
        ("Linen", ["LINEN"]),
        ("Polypropylene", ["POLYPROPYLENE", "PP"]),
    ];

    private static readonly string[] KnitWords = ["KNIT", "JERSEY", "FLEECE", "INTERLOCK", "PIQUE", "RIB", "TERRY", "MESH", "JACQUARD KNIT", "FRENCH TERRY"];
    private static readonly string[] WovenWords = ["WOVEN", "TWILL", "POPLIN", "RIPSTOP", "TAFFETA", "DENIM", "CANVAS", "OXFORD", "DOBBY", "CHAMBRAY", "PLAIN WEAVE"];

    // "53% Cotton", "100%POLYESTER", "100%Recycle Polyester": a percent, then the words up to the next separator or number
    [GeneratedRegex(@"(\d{1,3}(?:\.\d+)?)\s*%\s*([A-Z][A-Z \-]{0,40})", RegexOptions.IgnoreCase)]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"(\d{2,3}(?:\.\d+)?)\s*(?:G/SQM|GSM|G/M2|G/M²|G/SQ\.?M|GRAMS?\b)", RegexOptions.IgnoreCase)]
    private static partial Regex GsmRegex();

    public static FabricSpec Read(string? text, string? weaveCode = null)
    {
        var fibres = new List<(string Fibre, decimal Percent, bool Recycled)>();
        decimal? gsm = null;
        string? weave = weaveCode switch { "KNT" => "Knit", "WVN" => "Woven", _ => null };
        if (string.IsNullOrWhiteSpace(text)) return new FabricSpec(fibres, gsm, weave);
        var upper = text.ToUpperInvariant();

        foreach (Match m in PercentRegex().Matches(upper))
        {
            var percent = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var words = m.Groups[2].Value.Trim();
            var recycledWord = words.StartsWith("RECYCLE", StringComparison.Ordinal);
            // "(100%RECYCLED)" after a fibre: the fibre before it is recycled
            if (recycledWord && Fibre(words) is null)
            {
                if (fibres.Count > 0) fibres[^1] = fibres[^1] with { Recycled = true };
                continue;
            }
            if (Fibre(words) is not { } fibre || percent is <= 0 or > 100) continue;
            var existing = fibres.FindIndex(f => f.Fibre == fibre);
            if (existing >= 0) fibres[existing] = fibres[existing] with { Percent = Math.Min(100, fibres[existing].Percent + percent) };
            else fibres.Add((fibre, percent, recycledWord));
        }
        // "100% RECYCLE PES" or "RECYCLED POLYESTER" without a percent after it
        if (fibres.Count > 0 && upper.Contains("RECYCL", StringComparison.Ordinal) && !fibres.Any(f => f.Recycled))
            fibres[0] = fibres[0] with { Recycled = true };

        if (GsmRegex().Match(upper) is { Success: true } g) gsm = decimal.Parse(g.Groups[1].Value, CultureInfo.InvariantCulture);
        weave ??= KnitWords.Any(w => Contains(upper, w)) ? "Knit" : WovenWords.Any(w => Contains(upper, w)) ? "Woven" : null;
        return new FabricSpec(fibres, gsm, weave);
    }

    /// <summary>
    /// How well a fabric fits the wanted spec, 0-100, from the parts both have: fibres (share in common), recycled,
    /// weight (within 10% / 25%) and weave. Null when the wanted spec has nothing to compare.
    /// </summary>
    public static int? Match(FabricSpec wanted, FabricSpec fabric)
    {
        if (wanted.IsEmpty) return null;
        double score = 0, max = 0;
        if (wanted.Fibres.Count > 0)
        {
            max += 50;
            if (fabric.Fibres.Count > 0)
                score += 50 * (double)wanted.Fibres.Sum(w => Math.Min(w.Percent, fabric.Fibres.FirstOrDefault(f => f.Fibre == w.Fibre).Percent)) / 100;
            max += 15;
            if (fabric.Fibres.Count > 0 && wanted.Recycled == fabric.Recycled) score += 15;
        }
        if (wanted.WeightGsm is { } wg)
        {
            max += 20;
            if (fabric.WeightGsm is { } fg)
            {
                var diff = Math.Abs((double)(fg - wg)) / (double)wg;
                score += diff <= 0.10 ? 20 : diff <= 0.25 ? 10 : 0;
            }
        }
        if (wanted.Weave is not null)
        {
            max += 15;
            if (fabric.Weave == wanted.Weave) score += 15;
            else if (fabric.Weave is not null) score -= 15;
        }
        return max == 0 ? null : (int)Math.Round(Math.Clamp(score / max, 0, 1) * 100);
    }

    private static string? Fibre(string words)
    {
        var tokens = words.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var (fibre, names) in FibreWords)
            if (tokens.Any(t => names.Contains(t) || (t.Length >= 5 && names[0].StartsWith(t, StringComparison.Ordinal)))) return fibre;
        return null;
    }

    private static bool Contains(string text, string word) => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}", RegexOptions.CultureInvariant);
}

// ----- The suggestion -----

public static class BomSuggestionRules
{
    /// <summary>Candidate reference styles read from the database (best header match first).</summary>
    public const int Candidates = 300;
    /// <summary>Reference styles the BOM is built from.</summary>
    public const int References = 12;
    /// <summary>A reference needs at least this score (0-100), unless fewer than <see cref="MinReferences"/> would be left.</summary>
    public const int MinScore = 35;
    public const int MinReferences = 3;
    /// <summary>A section / material type is suggested when reference styles with at least this share of the weight have it.</summary>
    public const double SlotShare = 0.3;
    /// <summary>
    /// A line starts ticked from this confidence. Back-tested on 60 library styles (2026-10-21): from 60 up, 45-70% of
    /// lines were on the style's real BOM; below it 19-33%. Lower lines are listed unticked.
    /// </summary>
    public const int TickConfidence = 60;
    public const int MaxAlternatives = 4;
    /// <summary>Labels, packaging and artwork: only from the same customer or brand (they carry the brand).</summary>
    public static readonly string[] BrandOnlySections = ["LNP", "ART"];

    /// <summary>
    /// A material's base code: the code before a source or region suffix ("80020552_VN", "80020552_BOWKER" and
    /// "62754771-COO:CAMBODIA_1" are variants of 80020552 / 62754771). Variants pool their use; the suggestion offers the
    /// variant the reference styles use most. Codes whose base would be shorter than 4 characters stay whole.
    /// </summary>
    public static string BaseCode(string materialCode)
    {
        var cut = materialCode.IndexOfAny(['_', ' ', '-', '+', '(']);
        return cut >= 4 ? materialCode[..cut].ToUpperInvariant() : materialCode.Trim().ToUpperInvariant();
    }
}

/// <summary>
/// Builds a BOM for a style from the library, by rules (about a second, no AI service):
/// <list type="number">
/// <item>Reference styles: the header match from the database (family, product type or garment, weave, gender,
/// customer), plus how close their description's fabric is (fibres, recycled, weight), plus how alike their pictures
/// look when the style has a sketch or photo and the built-in image model can run.</item>
/// <item>For each section and material type most reference styles have, the materials they use most (weighted by how
/// close each reference is), with the usual part, suppliers and consumption; the rest are alternatives.</item>
/// <item>Fabric lines are ranked also by how well each fabric matches the description, and library fabrics that match
/// it well are offered even when no reference style uses them.</item>
/// </list>
/// Brand guardrail: labels, packaging and artwork come only from the same customer or brand. Other brands' fabrics,
/// trims and accessories are know-how: offered, marked, never ticked.
/// </summary>
public sealed class BomSuggestionService(IBomSuggestionRepository repo, IStyleImageStore images, ImageSearchService imageSearch)
{
    private const string ImagePrefix = "/api/v1/styles/images/";

    public async Task<BomSuggestionDto?> SuggestAsync(int styleId, CancellationToken ct = default)
    {
        if (await repo.CandidatesAsync(styleId, BomSuggestionRules.Candidates, ct) is not { } found) return null;
        var (target, candidates) = found;
        var notes = new List<string>();
        var wanted = FabricSpecReader.Read(target.Description, target.WeaveTypeCode);

        // Pictures: how alike each candidate looks (best match per style), when the style has a picture and the model can run.
        var (look, pictureNote) = await LookAsync(target, ct);

        var scored = candidates.Select(c =>
        {
            var fabric = FabricSpecReader.Match(wanted, FabricSpecReader.Read(c.Description, c.WeaveTypeCode));
            var lookScore = look.TryGetValue(c.StyleId, out var s) ? Math.Clamp(((double)s - 0.6) / 0.3, 0, 1) * 20 : 0;
            var score = Math.Min(100, c.HeaderScore + (fabric ?? 0) * 0.15 + lookScore);
            return (Candidate: c, Score: score, Fabric: fabric, Look: look.TryGetValue(c.StyleId, out var l) ? l : (decimal?)null);
        }).OrderByDescending(x => x.Score).ThenByDescending(x => x.Candidate.SameBrand).ToList();
        var references = scored.Where(x => x.Score >= BomSuggestionRules.MinScore).Take(BomSuggestionRules.References).ToList();
        if (references.Count < BomSuggestionRules.MinReferences) references = scored.Take(BomSuggestionRules.MinReferences).ToList();
        if (references.Count == 0)
        {
            notes.Add("noReferences");
            return new BomSuggestionDto(styleId, Reading(wanted), look.Count > 0, pictureNote, [], [], notes);
        }

        var referenceDtos = references.Select(r => new BomReferenceDto(r.Candidate.StyleId, r.Candidate.StyleNo, r.Candidate.SeasonCode,
            r.Candidate.CustomerCode, r.Candidate.ModelName, !r.Candidate.SameBrand, (int)Math.Round(r.Score), Reasons(r.Candidate, r.Fabric, r.Look), r.Look)).ToList();
        var weights = references.ToDictionary(r => r.Candidate.StyleId, r => r.Score);
        var sameBrand = references.Where(r => r.Candidate.SameBrand).Select(r => r.Candidate.StyleId).ToHashSet();
        if (sameBrand.Count == 0) notes.Add("noSameBrandReferences");

        var lines = await repo.LinesAsync(weights.Keys.ToList(), ct);
        var existing = (await repo.LinesAsync([styleId], ct)).Select(l => BomSuggestionRules.BaseCode(l.MaterialCode)).ToHashSet();
        var fabrics = wanted.Fibres.Count > 0 || wanted.WeightGsm is not null ? await repo.FabricsAsync(target.CustomerCode, ct) : [];

        var suggested = Build(lines, weights, sameBrand, existing, wanted, fabrics, target, notes);
        return new BomSuggestionDto(styleId, Reading(wanted), look.Count > 0, pictureNote, referenceDtos, suggested, notes);
    }

    private static FabricReadingDto? Reading(FabricSpec spec)
        => spec.IsEmpty ? null : new FabricReadingDto(spec.FibresText, spec.Recycled, spec.WeightGsm, spec.Weave);

    private static List<string> Reasons(BomSuggestCandidate c, int? fabric, decimal? look)
    {
        var reasons = new List<string>();
        if (c.Family) reasons.Add("family");
        if (c.SameProductType) reasons.Add("productType");
        else if (c.SameGarment) reasons.Add("garment");
        if (c.SameWeave) reasons.Add("weave");
        if (c.SameGender) reasons.Add("gender");
        if (c.SameCustomer) reasons.Add("customer");
        else if (c.SameBrand) reasons.Add("brand");
        if (fabric >= 70) reasons.Add("fabric");
        if (look >= 0.8m) reasons.Add("look");
        return reasons;
    }

    /// <summary>Look-alike similarity per style for the style's photo (else sketch); empty when not possible, with why.</summary>
    private async Task<(Dictionary<int, decimal> Look, string? Note)> LookAsync(BomSuggestTarget target, CancellationToken ct)
    {
        var url = new[] { target.ImageUrl, target.SketchUrl }.FirstOrDefault(u => u?.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase) == true);
        if (url is null) return ([], "noPicture");
        try
        {
            if (!(await imageSearch.ReadinessAsync(ct)).CanUseLookAlike(false)) return ([], "pictureModelOff");
            var name = url[ImagePrefix.Length..].Split('?')[0];
            if (images.Open(name) is not { } file) return ([], "pictureMissing");
            byte[] content;
            await using (var stream = file.Content)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                content = buffer.ToArray();
            }
            var found = await imageSearch.SearchByPhotoAsync(content, ImageKind.Jpeg, null, useAiServer: false, ct);
            return (found.Matches.Where(m => m.StyleId != target.StyleId).GroupBy(m => m.StyleId).ToDictionary(g => g.Key, g => g.Max(m => m.Similarity)), null);
        }
        catch (Exception ex) when (ex is AiServiceException or UnreadableImageException)
        {
            return ([], "pictureUnreadable");
        }
    }

    private sealed record Use(BomSuggestSourceLine Line, double Weight, bool SameBrand);

    private static List<SuggestedBomLineDto> Build(
        IReadOnlyList<BomSuggestSourceLine> lines, Dictionary<int, double> weights, HashSet<int> sameBrand, HashSet<string> existing, FabricSpec wanted,
        IReadOnlyList<BomSuggestFabric> fabrics, BomSuggestTarget target, List<string> notes)
    {
        var result = new List<SuggestedBomLineDto>();
        var totalWeight = weights.Values.Sum();
        var brandWeight = weights.Where(w => sameBrand.Contains(w.Key)).Sum(w => w.Value);
        var references = weights.Count;

        // Sections in their usual order; a slot is a section + material type.
        var slots = lines
            .GroupBy(l => (l.ContentClassCode, l.MaterialTypeCode))
            .Select(g => (Key: g.Key, Lines: g.ToList(), Sort: g.Min(l => l.ContentClassSort ?? 99)))
            .OrderBy(s => s.Sort).ThenBy(s => s.Key.ContentClassCode).ToList();
        var fabricSlotDone = false;

        foreach (var slot in slots)
        {
            var section = slot.Key.ContentClassCode;
            var brandOnly = section is not null && BomSuggestionRules.BrandOnlySections.Contains(section);
            var uses = slot.Lines.Where(l => !brandOnly || sameBrand.Contains(l.StyleId))
                .Select(l => new Use(l, weights[l.StyleId], sameBrand.Contains(l.StyleId))).ToList();
            var relevantWeight = brandOnly ? brandWeight : totalWeight;
            if (uses.Count == 0 || relevantWeight <= 0) continue;

            // How many of the reference weight has this slot, and how many lines of it a style usually has.
            var perStyle = uses.GroupBy(u => u.Line.StyleId).Select(g => (Weight: g.First().Weight, Count: g.Select(u => BomSuggestionRules.BaseCode(u.Line.MaterialCode)).Distinct().Count())).ToList();
            var share = perStyle.Sum(p => p.Weight) / relevantWeight;
            if (share < BomSuggestionRules.SlotShare) continue;
            var count = Math.Clamp((int)Math.Round(Median(perStyle.Select(p => (double)p.Count))), 1, 6);

            var isFabric = section == "FAB";
            var options = uses.GroupBy(u => BomSuggestionRules.BaseCode(u.Line.MaterialCode)).Select(g => Option(g.ToList(), relevantWeight, references, wanted, isFabric)).ToList();
            // Fabrics: also library fabrics that match the description well, ranked with the references' own.
            if (isFabric && !fabricSlotDone && fabrics.Count > 0 && WeaveFits(slot.Key.MaterialTypeCode, wanted))
            {
                fabricSlotDone = true;
                var have = options.Select(o => BomSuggestionRules.BaseCode(o.Option.MaterialCode)).ToHashSet();
                options.AddRange(fabrics.Where(f => !have.Contains(BomSuggestionRules.BaseCode(f.MaterialCode)))
                    .Select(f => (Fabric: f, Match: FabricSpecReader.Match(wanted, FabricSpec(f))))
                    .Where(x => x.Match >= 70)
                    .OrderByDescending(x => x.Match).ThenByDescending(x => x.Fabric.BrandStyles).ThenByDescending(x => x.Fabric.Styles).Take(5)
                    .Select(x => (Option: FromLibrary(x.Fabric, x.Match, references), Support: 0.0, x.Match)));
            }
            var ranked = options
                .OrderByDescending(o => Rank(o.Support, o.Match, o.Option.OtherBrand))
                .ThenByDescending(o => o.Option.UsedBy).ThenBy(o => o.Option.MaterialCode, StringComparer.Ordinal).ToList();

            for (var i = 0; i < count && i < ranked.Count; i++)
            {
                var (option, support, match) = ranked[i];
                var alternatives = ranked.Where((_, j) => j != i && j >= count).Take(BomSuggestionRules.MaxAlternatives).Select(o => o.Option);
                var onBom = existing.Contains(BomSuggestionRules.BaseCode(option.MaterialCode));
                var basis = support == 0 && match is not null ? "description" : "references";
                var confidence = (int)Math.Round(100 * (isFabric && match is { } m ? 0.5 * support + 0.5 * m / 100.0 : support));
                var tick = !onBom && !option.OtherBrand && (confidence >= BomSuggestionRules.TickConfidence || (basis == "description" && match >= 85));
                var first = slot.Lines[0];
                result.Add(new SuggestedBomLineDto($"{section}|{slot.Key.MaterialTypeCode}|{i}", section, first.ContentClassName, slot.Key.MaterialTypeCode,
                    first.MaterialTypeName, confidence, tick, basis, onBom, [option, .. alternatives]));
            }
        }

        // No reference style has a fabric line: offer the best matching library fabrics on their own.
        if (!result.Any(r => r.ContentClassCode == "FAB") && fabrics.Count > 0)
        {
            var best = fabrics.Select(f => (Fabric: f, Match: FabricSpecReader.Match(wanted, FabricSpec(f)))).Where(x => x.Match >= 70)
                .OrderByDescending(x => x.Match).ThenByDescending(x => x.Fabric.BrandStyles).ThenByDescending(x => x.Fabric.Styles).Take(5).ToList();
            if (best.Count > 0)
                result.Insert(0, new SuggestedBomLineDto("FAB|description|0", "FAB", "Fabric", best[0].Fabric.MaterialTypeCode, null, best[0].Match ?? 0,
                    best[0].Match >= 85 && best[0].Fabric.BrandStyles > 0, "description", existing.Contains(BomSuggestionRules.BaseCode(best[0].Fabric.MaterialCode)),
                    best.Select(x => FromLibrary(x.Fabric, x.Match, references)).ToList()));
        }
        if (target.Description is not null && wanted.IsEmpty) notes.Add("descriptionWithoutFabric");
        return result;
    }

    /// <summary>Support counts most; a fabric's fit to the description counts as much when there is one; other brands come last among equals.</summary>
    private static double Rank(double support, int? match, bool otherBrand)
        => (match is { } m ? 0.5 * support + 0.5 * m / 100.0 : support) - (otherBrand ? 0.05 : 0);

    private static bool WeaveFits(string? materialType, FabricSpec wanted)
        => wanted.Weave is null || materialType is null
           || (wanted.Weave == "Knit" ? !materialType.StartsWith("WOV", StringComparison.OrdinalIgnoreCase) : !materialType.StartsWith("KNIT", StringComparison.OrdinalIgnoreCase));

    private static FabricSpec FabricSpec(BomSuggestFabric f)
    {
        var spec = FabricSpecReader.Read(f.Composition ?? f.Description,
            f.MaterialTypeCode?.StartsWith("KNIT", StringComparison.OrdinalIgnoreCase) == true ? "KNT"
            : f.MaterialTypeCode?.StartsWith("WOV", StringComparison.OrdinalIgnoreCase) == true ? "WVN" : null);
        return f.WeightGsm is { } gsm ? spec with { WeightGsm = gsm } : spec;
    }

    /// <summary>
    /// One material (all variants of a base code): support from every style using any variant; code, description, part,
    /// suppliers and consumption from the variant used most.
    /// </summary>
    private static (BomOptionDto Option, double Support, int? Match) Option(List<Use> all, double relevantWeight, int references, FabricSpec wanted, bool isFabric)
    {
        var styles = all.GroupBy(u => u.Line.StyleId).Select(g => g.First()).ToList();
        var support = styles.Sum(u => u.Weight) / relevantWeight;
        var uses = all.GroupBy(u => u.Line.MaterialId).OrderByDescending(g => g.Select(u => u.Line.StyleId).Distinct().Sum(id => all.First(u => u.Line.StyleId == id).Weight))
            .ThenBy(g => g.First().Line.MaterialCode, StringComparer.Ordinal).First().ToList();
        var first = uses[0].Line;
        var description = MostCommon(uses, l => l.MaterialDescription);
        int? match = isFabric ? FabricSpecReader.Match(wanted, FabricSpecReader.Read(description)) : null;
        // A supplier is its code and name together (the most used pair, not the most used code with another line's name).
        var (nominatedCode, nominatedName) = MostCommonPair(uses, l => (l.NominatedSupplierCode, l.NominatedSupplierName));
        var (sapCode, sapName) = MostCommonPair(uses, l => (l.SupplierCode, l.SupplierName));
        return (new BomOptionDto(first.MaterialId, first.MaterialCode, description, MostCommonValue(uses, l => l.PartNo),
            nominatedCode, nominatedName, sapCode, sapName,
            MedianOf(uses.Select(u => u.Line.LcoConsumption)), MedianOf(uses.Select(u => u.Line.BrandConsumption)), MostCommon(uses, l => l.UomCode),
            styles.Count, references, match, !styles.Any(u => u.SameBrand)), support, match);
    }

    private static BomOptionDto FromLibrary(BomSuggestFabric f, int? match, int references)
        => new(f.MaterialId, f.MaterialCode, f.Description, null, f.NominatedSupplierCode, f.NominatedSupplierName, f.SupplierCode, f.SupplierName,
            f.MedianConsumption, null, f.UomCode, 0, references, match, f.BrandStyles == 0);

    /// <summary>The value most reference styles have (weighted), ignoring missing values.</summary>
    private static string? MostCommon(List<Use> uses, Func<BomSuggestSourceLine, string?> pick)
        => uses.Select(u => (Value: pick(u.Line), u.Weight)).Where(x => x.Value is not null)
            .GroupBy(x => x.Value).OrderByDescending(g => g.Sum(x => x.Weight)).Select(g => g.Key).FirstOrDefault();

    private static int? MostCommonValue(List<Use> uses, Func<BomSuggestSourceLine, int?> pick)
        => uses.Select(u => (Value: pick(u.Line), u.Weight)).Where(x => x.Value is not null)
            .GroupBy(x => x.Value).OrderByDescending(g => g.Sum(x => x.Weight)).Select(g => g.Key).FirstOrDefault();

    private static (string?, string?) MostCommonPair(List<Use> uses, Func<BomSuggestSourceLine, (string?, string?)> pick)
        => uses.Select(u => (Value: pick(u.Line), u.Weight)).Where(x => x.Value.Item1 is not null || x.Value.Item2 is not null)
            .GroupBy(x => x.Value).OrderByDescending(g => g.Sum(x => x.Weight)).Select(g => g.Key).FirstOrDefault();

    private static decimal? MedianOf(IEnumerable<decimal?> values)
    {
        var list = values.Where(v => v is > 0).Select(v => v!.Value).Order().ToList();
        if (list.Count == 0) return null;
        return list.Count % 2 == 1 ? list[list.Count / 2] : Math.Round((list[list.Count / 2 - 1] + list[list.Count / 2]) / 2, 4);
    }

    private static double Median(IEnumerable<double> values)
    {
        var list = values.Order().ToList();
        return list.Count == 0 ? 0 : list.Count % 2 == 1 ? list[list.Count / 2] : (list[list.Count / 2 - 1] + list[list.Count / 2]) / 2;
    }
}
