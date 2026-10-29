using System.Text.RegularExpressions;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;

namespace LT.ODM.Application.Concepts;

// ----- Proven styles in the library for a concept (Concepts page) -----

/// <summary>From concept.usp_Concept_MatchContext: the category's product types, the brand's customer codes, and the words that occur in fabric BOM lines.</summary>
public sealed record ConceptMatchContext(IReadOnlyList<string> ProductTypes, IReadOnlyList<string> BrandCustomers, IReadOnlyList<string> Words);

/// <summary>A library style scored against the concept. SameBrand: the concept customer's brand (null when the concept has no customer).</summary>
public sealed record ConceptLibraryMatchDto(ConceptCandidateDto Style, bool? SameBrand);

/// <summary>What was looked for (read from the concept's own fields, no AI) and the best-scored styles.</summary>
public sealed record ConceptLibraryMatchesDto(ConceptCriteriaDto Criteria, IReadOnlyList<ConceptLibraryMatchDto> Matches);

/// <summary>
/// Proven styles to start a concept from, without waiting for AI: the concept's category, gender and customer are already library
/// codes, and fabric words come from the brief's fabrics, the idea and the trend tags (only words found on fabric BOM lines are
/// kept), so the library is scored in SQL at once (style.usp_ConceptMatch_Candidates). Explaining the best ones with AI is a
/// separate, optional step (<see cref="ConceptMatcher.ExplainAsync"/>), one Text-job call.
/// </summary>
public sealed partial class ConceptLibraryMatcher(IConceptRepository concepts, IStyleAiRepository styleAi)
{
    public const int Shown = 12;
    public const int MaxKeywords = 6;

    private static readonly HashSet<string> KnitWords = ["JERSEY", "FLEECE", "INTERLOCK", "TRICOT", "KNIT", "RIB", "PIQUE", "TERRY", "SPACER"];
    private static readonly HashSet<string> WovenWords = ["RIPSTOP", "TWILL", "POPLIN", "TAFFETA", "WOVEN", "DOBBY", "CANVAS", "OXFORD", "DENIM"];

    /// <summary>Words that say nothing about a fabric (left out before the BOM check).</summary>
    private static readonly HashSet<string> Ignore =
    [
        "WITH", "FROM", "THAT", "THIS", "THEIR", "THESE", "INTO", "FOR", "AND", "THE", "ARE", "HAS", "HAVE", "WILL", "MADE", "USE", "USED", "USING",
        "FABRIC", "FABRICS", "MATERIAL", "MATERIALS", "BLEND", "BLENDS", "QUALITY", "STYLE", "DESIGN", "GARMENT", "PRODUCT", "PRODUCTS", "NEW", "LOOK",
        "FEEL", "HIGH", "LOW", "MORE", "LESS", "VERY", "ALSO", "EACH", "ONLY", "OUR", "YOUR", "ALL", "SEASON", "COLLECTION", "TREND", "TRENDS", "MARKET",
    ];

    /// <summary>Null when the concept does not exist.</summary>
    public async Task<(ConceptLibraryMatchesDto Result, IReadOnlyList<ConceptCandidateDto> Candidates, ConceptDetailDto Concept)?> FindAsync(
        int conceptId, int take, CancellationToken ct = default)
    {
        if (await concepts.GetAsync(conceptId, null, ct) is not { } detail) return null;
        var c = detail.Concept;
        var context = await concepts.MatchContextAsync(conceptId, CandidateWords(detail), ct);
        var keywords = context.Words.Take(MaxKeywords).ToList();
        var weave = keywords.Any(w => WovenWords.Contains(w)) ? "WVN" : keywords.Any(w => KnitWords.Contains(w)) ? "KNT" : null;
        var parts = new List<string>();
        if (c.ProductCategoryName is { } cat) parts.Add(context.ProductTypes.Count > 0 ? $"{cat} ({context.ProductTypes.Count} product types)" : cat);
        if (c.Gender is { } g) parts.Add(g);
        if (weave is not null) parts.Add(weave == "WVN" ? "woven" : "knit");
        if (keywords.Count > 0) parts.Add("fabric words " + string.Join(", ", keywords));
        var criteria = new ConceptCriteriaDto(c.CustomerCode, context.ProductTypes, c.Gender, weave, keywords, [],
            parts.Count > 0 ? "Styles with " + string.Join(" · ", parts) + "." : "Nothing to look for yet: set the product category, or draft the brief.");

        IReadOnlyList<ConceptCandidateDto> candidates = parts.Count > 0 ? await styleAi.ConceptCandidatesAsync(criteria, ConceptMatchRules.Candidates, ct) : [];
        var brand = context.BrandCustomers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = candidates.Take(Math.Clamp(take, 1, ConceptMatchRules.Candidates))
            .Select(s => new ConceptLibraryMatchDto(s, c.CustomerCode is null ? null : brand.Contains(s.CustomerCode))).ToList();
        return (new ConceptLibraryMatchesDto(criteria, matches), candidates, detail);
    }

    /// <summary>The concept as the AI ranking reads it (names, not codes).</summary>
    public static ConceptMatchRequest Request(ConceptDetailDto d)
    {
        var c = d.Concept;
        var brief = string.Join(". ", new[] { c.ProductCategoryName, c.Gender, c.Description, c.Brief?.Summary }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new ConceptMatchRequest(c.Name, c.CustomerName ?? c.CustomerCode, c.SeasonCode, c.TargetMarketName, c.TargetPrice,
            d.Tags.Select(t => t.Name).ToList(), brief.Length > 4000 ? brief[..4000] : brief, c.Brief?.Products ?? [], c.Brief?.Fabrics ?? [], c.Brief?.Notes ?? []);
    }

    /// <summary>
    /// Words that may describe the fabric, best first: the brief's fabric lines, then the idea, then the trend tag names. Upper case,
    /// letters only, at least 3 letters; RECYCLED / BRUSHED / PANELS become RECYCL / BRUSH / PANEL so they match every form on a BOM.
    /// </summary>
    public static IReadOnlyList<string> CandidateWords(ConceptDetailDto d)
    {
        var texts = (d.Concept.Brief?.Fabrics ?? []).Concat([d.Concept.Description ?? ""]).Concat(d.Tags.Select(t => t.Name));
        var words = new List<string>();
        foreach (var text in texts)
            foreach (Match m in Letters().Matches(text.ToUpperInvariant()))
            {
                var w = m.Value;
                if (w.Length < 3 || Ignore.Contains(w)) continue;
                if (w.Length > 5 && w.EndsWith("ED", StringComparison.Ordinal)) w = w[..^1].TrimEnd('E') is { Length: >= 4 } x ? x : w;
                else if (w.Length > 5 && w.EndsWith("ING", StringComparison.Ordinal)) w = w[..^3];
                else if (w.Length > 4 && w.EndsWith('S') && !w.EndsWith("SS", StringComparison.Ordinal)) w = w[..^1];
                if (!words.Contains(w)) words.Add(w);
                if (words.Count == 30) return words;
            }
        return words;
    }

    [GeneratedRegex("[A-Z]+")]
    private static partial Regex Letters();
}
