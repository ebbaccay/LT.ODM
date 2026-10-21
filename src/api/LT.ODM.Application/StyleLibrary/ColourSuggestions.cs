namespace LT.ODM.Application.StyleLibrary;

// ----- Contracts -----

/// <summary>Suggested material colours for a style's BOM lines, one per line x colorway the line is used in.</summary>
public sealed record ColourSuggestionDto(int StyleId, int Cells, int Empty, IReadOnlyList<SuggestedColourDto> Suggestions);

/// <summary>
/// A colour for one BOM line in one colorway. Basis: colorway (other styles' colorways of the same name used this colour
/// with this material) or material (the material has always come in this colour). Lines / Of: how many of the lines
/// seen had this colour. Recommended ones start ticked. Only empty cells get suggestions; filled ones are never changed.
/// </summary>
public sealed record SuggestedColourDto(
    int BomLineId, int ColorwayId, string? MaterialColorCode, string? MaterialColorDescription, string Basis, int Confidence, int Lines, int Of,
    bool SameCustomer, bool Recommended);

/// <summary>Colours picked on the suggestion. Only empty cells are filled.</summary>
public sealed record ApplyColoursRequest(IReadOnlyList<ColourCellRequest> Cells);

public sealed record ColourCellRequest(int BomLineId, int ColorwayId, string? MaterialColorCode, string? MaterialColorDescription);

// ----- Reads (style.usp_ColourSuggest_Sources) -----

public sealed record ColourCell(
    int BomLineId, int ColorwayId, string Base, string ColorwayCode, string? ColorwayName, string? NameKey, string? MaterialColorCode,
    string? MaterialColorDescription);

public sealed record ColourByName(string Base, string NameKey, string? MaterialColorCode, string? MaterialColorDescription, int Lines, int CustomerLines);

public sealed record ColourByMaterial(string Base, string? MaterialColorCode, string? MaterialColorDescription, int Lines, int CustomerLines);

public interface IColourSuggestionRepository
{
    /// <summary>Null when the style does not exist.</summary>
    Task<(IReadOnlyList<ColourCell> Cells, IReadOnlyList<ColourByName> ByName, IReadOnlyList<ColourByMaterial> ByMaterial)?> SourcesAsync(
        int styleId, CancellationToken ct = default);

    /// <summary>Fills the given cells that are still empty (lines of this style only); returns how many were filled.</summary>
    Task<int> ApplyAsync(int styleId, IReadOnlyList<ColourCellRequest> cells, string changedBy, CancellationToken ct = default);
}

public static class ColourSuggestionRules
{
    /// <summary>A colorway-name suggestion needs at least this many lines seen.</summary>
    public const int MinLines = 2;
    /// <summary>A material counts as one-colour when this share of its lines (at least <see cref="MinSingleLines"/>) had the same colour.</summary>
    public const double SingleColourShare = 0.9;
    public const int MinSingleLines = 3;
    /// <summary>
    /// Back-test (2026-10-21, 80 styles without earlier versions, their 10,263 filled cells hidden): suggested for 80%, right
    /// 93%. From confidence 80: 99% right (55% of cells), ticked; 40-79: about 80%, shown unticked; below 40: about 30%, not shown.
    /// </summary>
    public const int TickConfidence = 80;
    public const int MinConfidence = 40;
}

/// <summary>
/// Material colours per colorway from the library, by rules (no AI): for each empty line x colorway, the colour other
/// styles used for the same material (variants count as one) in a colorway of the same name, this customer's first;
/// else the colour the material has always come in. Confidence: the share of the lines seen that had the colour,
/// lowered when few lines were seen.
/// </summary>
public sealed class ColourSuggestionService(IColourSuggestionRepository repo)
{
    public async Task<ColourSuggestionDto?> SuggestAsync(int styleId, CancellationToken ct = default)
    {
        if (await repo.SourcesAsync(styleId, ct) is not { } sources) return null;
        var (cells, byName, byMaterial) = sources;
        var names = byName.ToLookup(x => (x.Base, x.NameKey));
        var materials = byMaterial.ToLookup(x => x.Base);
        var empty = cells.Where(c => c.MaterialColorCode is null && c.MaterialColorDescription is null).ToList();
        var suggestions = new List<SuggestedColourDto>();

        foreach (var cell in empty)
        {
            if (cell.NameKey is { Length: > 0 } key && Best(names[(cell.Base, key)].Select(x => (x.MaterialColorCode, x.MaterialColorDescription, x.Lines, x.CustomerLines)).ToList())
                    is { Total: >= ColourSuggestionRules.MinLines } byColorway)
            {
                suggestions.Add(Suggestion(cell, byColorway, "colorway"));
                continue;
            }
            var single = Best(materials[cell.Base].Select(x => (x.MaterialColorCode, x.MaterialColorDescription, x.Lines, x.CustomerLines)).ToList());
            if (single is { } s && s.Total >= ColourSuggestionRules.MinSingleLines && (double)s.Lines / s.Total >= ColourSuggestionRules.SingleColourShare)
                suggestions.Add(Suggestion(cell, s, "material"));
        }
        return new ColourSuggestionDto(styleId, cells.Count, empty.Count, suggestions.Where(x => x.Confidence >= ColourSuggestionRules.MinConfidence).ToList());
    }

    private sealed record Pick(string? Code, string? Description, int Lines, int Total, bool SameCustomer);

    /// <summary>The colour this customer used most (else everyone's most used); null when nothing was seen.</summary>
    private static Pick? Best(List<(string? Code, string? Description, int Lines, int CustomerLines)> colours)
    {
        if (colours.Count == 0) return null;
        var customerTotal = colours.Sum(c => c.CustomerLines);
        if (customerTotal > 0)
        {
            var best = colours.OrderByDescending(c => c.CustomerLines).ThenByDescending(c => c.Lines).ThenBy(c => c.Code, StringComparer.Ordinal).First();
            return new Pick(best.Code, best.Description, best.CustomerLines, customerTotal, true);
        }
        var top = colours.OrderByDescending(c => c.Lines).ThenBy(c => c.Code, StringComparer.Ordinal).First();
        return new Pick(top.Code, top.Description, top.Lines, colours.Sum(c => c.Lines), false);
    }

    private static SuggestedColourDto Suggestion(ColourCell cell, Pick p, string basis)
    {
        // share of the lines seen, scaled down below 5 lines (2 of 2 is weaker evidence than 20 of 20)
        var confidence = (int)Math.Round(100.0 * p.Lines / p.Total * Math.Min(1, p.Total / 5.0));
        return new SuggestedColourDto(cell.BomLineId, cell.ColorwayId, p.Code, p.Description, basis, confidence, p.Lines, p.Total, p.SameCustomer,
            confidence >= ColourSuggestionRules.TickConfidence);
    }
}
