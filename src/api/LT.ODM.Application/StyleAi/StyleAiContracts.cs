using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.StyleAi;

// AI Studio (/api/v1/ai/...): smart search, change summary, BOM check and style renders on the Style Library.
// The rules (comparison, BOM check, render facts) work without AI; the AI writes filters, explanations and pictures.

/// <summary>Who may use AI Studio: the Style Library readers; renders are made by its editors.</summary>
public static class StyleAiRoles
{
    public const string Readers = StyleRoles.Readers;
    public const string Editors = StyleRoles.Editors;
}

// ----- Smart search -----

public sealed record StyleSearchRequest(string? Query);

/// <summary>Filters the AI read from the request; every code is one of the library's codes.</summary>
public sealed record StyleSearchFiltersDto(
    string? Search, string? Material, string? Customer, IReadOnlyList<string> Seasons, string? BusinessUnit,
    IReadOnlyList<string> ProductTypes, string? WeaveType, string? Gender);

public sealed record StyleSearchResultDto(StyleSearchFiltersDto Filters, string Explanation, PagedResult<StyleListItemDto> Results);

// ----- Change summary -----

public sealed record StyleRefDto(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? ModelName, string? ImageUrl, string? SketchUrl,
    int Colorways, int BomLines);

public sealed record FieldChangeDto(string Field, string? Before, string? After);

/// <summary>Change: Added, Removed, Changed (same code) or Recoded (same name, new code).</summary>
public sealed record ColorwayChangeDto(string Change, string ColorwayCode, string? ColorwayName, string? FromCode, IReadOnlyList<FieldChangeDto> Fields);

/// <summary>
/// Change: Added, Removed, Changed (same material) or Swapped (same section and part, another material).
/// MaterialCode is the line's material in the newer style (the older one for Removed); FromMaterial* is the replaced one.
/// </summary>
public sealed record BomLineChangeDto(
    string Change, string? ContentClassCode, string? ContentClassName, int? PartNo, string MaterialCode, string? MaterialDescription,
    string? FromMaterialCode, string? FromMaterialDescription, IReadOnlyList<FieldChangeDto> Fields);

/// <summary>From = the earlier style (by default the one this style was reused from); To = this style.</summary>
public sealed record StyleCompareDto(
    StyleRefDto From, StyleRefDto To, string? Relation, IReadOnlyList<FieldChangeDto> Header, IReadOnlyList<ColorwayChangeDto> Colorways,
    IReadOnlyList<BomLineChangeDto> BomLines, int UnchangedColorways, int UnchangedLines);

public sealed record StyleCompareRequest(int StyleId, int? FromStyleId);

/// <summary>Area: header, colorways, materials, consumption, suppliers or other.</summary>
public sealed record CompareHighlightDto(string Area, string Text);

public sealed record CompareSummaryDto(string Headline, string Summary, IReadOnlyList<CompareHighlightDto> Highlights, IReadOnlyList<string> Checks);

// ----- BOM check -----

public sealed record BomCheckQuery(int? StyleId, string? Customer, string? Season);

public static class BomCheckRules
{
    public static readonly string[] All = ["NoConsumption", "LcoBrandGap", "PeerOutlier", "MainFabricOutlier", "FamilyDrift", "LcoMissing", "NoSupplier"];
    public static readonly string[] Severities = ["High", "Medium", "Low"];
    public const int MaxRows = 500;
    /// <summary>Findings sent to the AI for an explanation (most severe first).</summary>
    public const int MaxExplained = 80;
}

public sealed record BomCheckRuleCountDto(string RuleCode, string Severity, int Lines, int Styles);

public sealed record BomCheckFindingDto(
    int BomLineId, int StyleId, string StyleNo, string SeasonCode, string CustomerCode, int LineSeq, int? PartNo, string MaterialCode,
    string? MaterialDescription, string? ContentClassCode, string? UomCode, decimal? LcoConsumption, decimal? BrandConsumption,
    string RuleCode, string Severity, decimal? Value, decimal? RefValue, int? PeerCount, int? RefStyleId, string? RefStyleNo);

/// <summary>Findings holds at most BomCheckRules.MaxRows rows; TotalFindings counts them all.</summary>
public sealed record BomCheckDto(
    int StylesChecked, int LinesChecked, int TotalFindings, IReadOnlyList<BomCheckRuleCountDto> Rules, IReadOnlyList<BomCheckFindingDto> Findings);

public sealed record BomCheckPriorityDto(int BomLineId, string Why, string Action);

public sealed record BomCheckRuleNoteDto(string RuleCode, string Note);

public sealed record BomCheckExplanationDto(string Summary, IReadOnlyList<BomCheckPriorityDto> Priorities, IReadOnlyList<BomCheckRuleNoteDto> RuleNotes);

// ----- Renders -----

/// <summary>What the render prompt is built from (shown so users can see exactly which data goes to the image model).</summary>
public sealed record RenderFactsDto(
    string? Garment, string? Gender, string? Construction, string? ModelName, string? ColorwayCode, string? ColorwayName,
    IReadOnlyList<string> Fabrics, IReadOnlyList<string> Colours, IReadOnlyList<string> Details);

public sealed record RenderColorwayDto(int ColorwayId, string ColorwayCode, string? ColorwayName, string Status);

public sealed record StyleRenderDto(
    int RenderId, int StyleId, int? ColorwayId, string? ColorwayCode, string ImageUrl, string Prompt, string Provider, string Model,
    bool UsedSketch, string CreatedBy, DateTime CreatedUtc);

/// <summary>Everything the render screen needs for one style: its colorways, the facts and default prompt, and earlier renders.</summary>
public sealed record RenderBriefDto(
    StyleRefDto Style, IReadOnlyList<RenderColorwayDto> Colorways, int? ColorwayId, bool HasSketch, bool SketchUsable,
    RenderFactsDto Facts, string Prompt, IReadOnlyList<StyleRenderDto> Renders);

public sealed record RenderPromptRequest(int? ColorwayId);

/// <summary>
/// A prompt for outside design tools (<see cref="DesignPrompt"/>). Mode is the one used (image falls back to text when
/// the style has no sketch or photo); ReferenceKind / ReferenceUrl name the image to attach in image mode.
/// </summary>
public sealed record DesignPromptDto(
    string Mode, int? ColorwayId, string? ReferenceKind, string? ReferenceUrl, bool HasReference, RenderFactsDto Facts, string Prompt);

public sealed record RenderPromptDto(string Prompt);

public sealed record RenderRequest(int? ColorwayId, string? Prompt, bool UseSketch);

public static class RenderRules
{
    public const int MaxPrompt = 2000;
}

/// <summary>AI Studio data that is not in the Styles procedures (style.ai.procedures.sql).</summary>
public interface IStyleAiRepository
{
    Task<BomCheckDto> RunBomCheckAsync(BomCheckQuery query, int maxRows, CancellationToken ct = default);
    Task<IReadOnlyList<StyleRenderDto>> ListRendersAsync(int styleId, CancellationToken ct = default);

    Task<StyleRenderDto> AddRenderAsync(
        int styleId, int? colorwayId, string imageUrl, string prompt, string provider, string model, bool usedSketch, string createdBy,
        CancellationToken ct = default);

    /// <summary>Throws StyleRuleException (404) when the render is not on the style.</summary>
    Task DeleteRenderAsync(int styleId, int renderId, CancellationToken ct = default);

    /// <summary>Styles with a BOM scored against a concept's criteria, best first (style.usp_ConceptMatch_Candidates).</summary>
    Task<IReadOnlyList<ConceptCandidateDto>> ConceptCandidatesAsync(ConceptCriteriaDto criteria, int take, CancellationToken ct = default);
}
