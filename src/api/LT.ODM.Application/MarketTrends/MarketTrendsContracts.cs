using LT.ODM.Application.Ai;

namespace LT.ODM.Application.MarketTrends;

/// <summary>A style of the collection to score (same fields TMS sent to its hub method).</summary>
public sealed record TrendStyleInput(int ItemRecid, string? StyleName, string? StyleCode, string? Category, string? Sbu, string? Country, decimal FobPrice);

/// <summary>A collection and its styles, sent to the AI for a trend read-out.</summary>
public sealed record TrendAnalysisRequest(
    string? ConceptName,
    string? Season,
    string? TargetMarket,
    decimal TargetFob,
    IReadOnlyList<string>? ActiveTags,
    IReadOnlyList<string>? FabricDirection,
    IReadOnlyList<string>? SustainabilityNotes,
    IReadOnlyList<TrendStyleInput>? Styles);

/// <summary>
/// AI read-out per region. GrowthPct is the model's estimate of year-on-year demand growth, not market data.
/// Id: na, eu, as, sa, af, oce. Strength: Strong, Moderate, Emerging, Declining.
/// </summary>
public sealed record RegionTrendDto(string Id, string Label, string SubLabel, string Strength, int GrowthPct);

/// <summary>AI read-out per style. TrendScore 0-100; TagType: growing, emerging, declining, restocked.</summary>
public sealed record StyleTrendDto(int ItemRecid, int TrendScore, IReadOnlyList<string> TrendTags, string Insight, string TagType, int GrowthPct);

public sealed record TrendAnalysisDto(IReadOnlyList<RegionTrendDto> Regions, IReadOnlyList<StyleTrendDto> Styles, IReadOnlyList<string> Insights);

public interface ITrendAnalysisAi
{
    /// <summary>False when the Text job has no usable AI service (Settings > AI connections, else appsettings).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <exception cref="AiServiceException">The AI service failed or returned something unusable.</exception>
    Task<TrendAnalysisDto> AnalyzeAsync(TrendAnalysisRequest request, CancellationToken ct = default);
}

public static class MarketTrendRules
{
    public static readonly string[] RegionIds = ["na", "eu", "as", "sa", "af", "oce"];
    public static readonly string[] Strengths = ["Strong", "Moderate", "Emerging", "Declining"];
    public static readonly string[] TagTypes = ["growing", "emerging", "declining", "restocked"];

    public const int MaxStyles = 60;

    public static Dictionary<string, string[]> Analysis(TrendAnalysisRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        void Text(string field, string? value, int max)
        {
            if ((value?.Length ?? 0) > max) errors[field] = [$"Use at most {max} characters."];
        }
        void List(string field, IReadOnlyList<string>? values)
        {
            if (values is { Count: > 30 } || values?.Any(v => v is null || v.Length > 200) == true)
                errors[field] = ["Use at most 30 entries of up to 200 characters each."];
        }
        Text("conceptName", r.ConceptName, 500);
        Text("season", r.Season, 100);
        Text("targetMarket", r.TargetMarket, 255);
        if (r.TargetFob is < 0 or > 100_000) errors["targetFob"] = ["Enter an amount between 0 and 100,000."];
        List("activeTags", r.ActiveTags);
        List("fabricDirection", r.FabricDirection);
        List("sustainabilityNotes", r.SustainabilityNotes);

        var styles = r.Styles ?? [];
        if (styles.Count == 0) errors["styles"] = ["The collection has no styles to analyze."];
        else if (styles.Count > MaxStyles) errors["styles"] = [$"Analyze at most {MaxStyles} styles at a time."];
        else if (styles.Any(s => s.ItemRecid <= 0 || (s.StyleName?.Length ?? 0) > 255 || (s.StyleCode?.Length ?? 0) > 100
                                 || (s.Category?.Length ?? 0) > 100 || (s.Sbu?.Length ?? 0) > 255 || (s.Country?.Length ?? 0) > 100
                                 || s.FobPrice is < 0 or > 100_000))
            errors["styles"] = ["One or more styles have invalid values."];
        else if (styles.Select(s => s.ItemRecid).Distinct().Count() != styles.Count)
            errors["styles"] = ["Each style can be listed once."];
        return errors;
    }
}
