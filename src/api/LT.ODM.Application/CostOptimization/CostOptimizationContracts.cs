using LT.ODM.Application.Ai;

namespace LT.ODM.Application.CostOptimization;

/// <summary>A style's current costing, sent to the AI for savings ideas (same fields TMS sent to its hub method).</summary>
public sealed record CostSuggestionRequest(
    string? StyleName,
    decimal CurrentFob,
    decimal TargetFob,
    decimal FabricCost,
    string? FabricDesc,
    decimal TrimCost,
    string? TrimDesc,
    decimal LaborCost,
    decimal OverheadCost,
    decimal MarginCost,
    string? BomSummary,
    string? CmtSummary);

/// <summary>One savings idea. Category is Fabric, Trim, Labor or Overhead; Saving = FromPrice - ToPrice.</summary>
public sealed record CostSuggestionDto(string Category, string Title, string Detail, decimal FromPrice, decimal ToPrice, decimal Saving);

public interface ICostSuggestionAi
{
    /// <summary>False when the Text job has no usable AI service (Settings > AI connections, else appsettings).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <exception cref="AiServiceException">The AI service failed or returned something unusable.</exception>
    Task<IReadOnlyList<CostSuggestionDto>> SuggestAsync(CostSuggestionRequest request, CancellationToken ct = default);
}

public static class CostOptimizationRules
{
    public static readonly string[] Categories = ["Fabric", "Trim", "Labor", "Overhead"];

    private const decimal MaxAmount = 100_000m;

    public static Dictionary<string, string[]> Suggestions(CostSuggestionRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        void Amount(string field, decimal value)
        {
            if (value is < 0 or > MaxAmount) errors[field] = ["Enter an amount between 0 and 100,000."];
        }
        void Text(string field, string? value, int max)
        {
            if ((value?.Length ?? 0) > max) errors[field] = [$"Use at most {max} characters."];
        }
        Amount("currentFob", r.CurrentFob);
        Amount("targetFob", r.TargetFob);
        Amount("fabricCost", r.FabricCost);
        Amount("trimCost", r.TrimCost);
        Amount("laborCost", r.LaborCost);
        Amount("overheadCost", r.OverheadCost);
        Amount("marginCost", r.MarginCost);
        Text("styleName", r.StyleName, 255);
        Text("fabricDesc", r.FabricDesc, 500);
        Text("trimDesc", r.TrimDesc, 500);
        Text("bomSummary", r.BomSummary, 4000);
        Text("cmtSummary", r.CmtSummary, 2000);
        if (r.CurrentFob <= 0) errors["currentFob"] = ["The style has no current FOB to optimize."];
        return errors;
    }
}
