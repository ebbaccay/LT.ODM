using System.Globalization;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.LibraryAssistant.Tools;

// Tools on styles: search, counts, one style's details and BOM, comparing two styles, library totals.

internal static class ToolKit
{
    public static readonly string[] StyleFilters = ["customer", "seasons", "productTypes", "businessUnits", "gender", "weaveType", "material", "looks", "keywords"];

    public static LibraryTableDto Table(string[] columns, IEnumerable<(IReadOnlyList<string?> Row, string? Link)> rows, int total)
    {
        var list = rows.ToList();
        return new LibraryTableDto(columns, list.Select(r => r.Row).ToList(), list.Select(r => r.Link).ToList(), total);
    }

    /// <summary>A table with a picture per row (null where a row has none; no pictures at all = no picture column).</summary>
    public static LibraryTableDto Table(string[] columns, IEnumerable<(IReadOnlyList<string?> Row, string? Link, string? Image)> rows, int total)
    {
        var list = rows.ToList();
        var images = list.Select(r => string.IsNullOrWhiteSpace(r.Image) ? null : r.Image).ToList();
        return new LibraryTableDto(columns, list.Select(r => r.Row).ToList(), list.Select(r => r.Link).ToList(), total,
            images.Any(i => i is not null) ? images : null);
    }

    public static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    public static string? D(decimal? value) => value?.ToString("0.###", CultureInfo.InvariantCulture);
    public static decimal Pct(decimal share) => Math.Round(share * 100, 1);

    /// <summary>Finds a style by number: exact number first, then the only match of a contains-search. 404 otherwise.</summary>
    public static async Task<StyleListItemDto> FindStyleAsync(IStyleRepository styles, string? styleNo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(styleNo)) throw new StyleRuleException(400, "Which style? Give its style number.");
        var found = await styles.ListAsync(new StyleListQuery(styleNo.Trim(), null, null, null, null, null, null, 0, 20), ct);
        return found.Items.FirstOrDefault(s => string.Equals(s.StyleNo, styleNo.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? (found.Total == 1 ? found.Items[0] : null)
            ?? throw new StyleRuleException(404, found.Total == 0
                ? $"No style matches \"{styleNo}\"."
                : $"{N(found.Total)} styles match \"{styleNo}\" ({string.Join(", ", found.Items.Take(5).Select(s => s.StyleNo))}{(found.Total > 5 ? ", ..." : "")}). Give the full style number.");
    }
}

/// <summary>Styles matching filters: how many, and the newest ones.</summary>
public sealed class StyleSearchTool(IStyleRepository styles) : ILibraryTool
{
    public string Name => LibraryToolNames.StyleSearch;
    public string Description => "Finds styles by customer, season, product type, business unit, gender, weave, material on the BOM, picture look tags (hooded, full zip, striped, navy) or model name; "
        + "answers 'which styles ...', 'show me ...', 'do we have ...' and 'how many styles ...' (a total, not per group).";
    public string Example => "Which men's jackets for ADI use recycled polyester?";
    public IReadOnlyList<string> Parameters => ToolKit.StyleFilters;

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var page = await styles.ListAsync(a.ListQuery(20), ct);
        var facts = page.Total == 0 ? $"No styles found for {a.Scope()}."
            : $"{ToolKit.N(page.Total)} styles found for {a.Scope()}" + (page.Total > page.Items.Count ? $"; the newest {page.Items.Count} are listed." : ".");
        return new LibraryToolResult(facts,
            ToolKit.Table(["styleNo", "season", "customer", "productType", "model"],
                page.Items.Select(s => ((IReadOnlyList<string?>)[s.StyleNo, s.SeasonCode, s.CustomerCode, s.ProductTypeName, s.ModelName ?? s.Description],
                    (string?)$"/styles/{s.StyleId}", s.ImageUrl ?? s.SketchUrl)),
                page.Total),
            [new LibraryLinkDto("styles", ToolKit.N(page.Total), a.StylesUrl())],
            new
            {
                total = page.Total,
                filters = a.Display(),
                newest = page.Items.Take(10).Select(s => new { s.StyleNo, season = s.SeasonCode, customer = s.CustomerCode, productType = s.ProductTypeName, model = s.ModelName }),
            });
    }
}

/// <summary>Styles counted per season, customer, product type, business unit, gender or weave.</summary>
public sealed class StyleCountsTool(IStyleRepository styles) : ILibraryTool
{
    public string Name => "style_counts";
    public string Description => "Counts styles per season, customer, product type, business unit, gender or weave (groupBy), for the styles matching "
        + "the filters; answers 'how many ... per/by ...', 'which season/customer has the most ...', 'breakdown of ...'.";
    public string Example => "How many jackets per season?";
    public IReadOnlyList<string> Parameters => [.. ToolKit.StyleFilters, "groupBy"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var by = a.GroupBy ?? "season";
        var groups = await styles.CountAsync(a.ListQuery(0), by, ct);
        var total = groups.Sum(g => g.Styles);
        var top = groups.OrderByDescending(g => g.Styles).Take(3).Select(g => $"{g.Name ?? g.Key ?? "not set"} ({ToolKit.N(g.Styles)})");
        var facts = total == 0 ? $"No styles found for {a.Scope()}."
            : $"{ToolKit.N(total)} styles for {a.Scope()}, by {by}: {groups.Count} groups; most: {string.Join(", ", top)}.";
        return new LibraryToolResult(facts,
            ToolKit.Table(["group", "name", "styles"], groups.Select(g => ((IReadOnlyList<string?>)[g.Key ?? "—", g.Name, ToolKit.N(g.Styles)], (string?)null)), groups.Count),
            [new LibraryLinkDto("styles", ToolKit.N(total), a.StylesUrl())],
            new { groupBy = by, total, filters = a.Display(), groups = groups.Take(25).Select(g => new { key = g.Key ?? "not set", name = g.Name, styles = g.Styles }) });
    }
}

/// <summary>One style: header, colorways and its BOM.</summary>
public sealed class StyleDetailTool(IStyleRepository styles) : ILibraryTool
{
    public string Name => "style_detail";
    public string Description => "One style by its style number: customer, season, model, product type, gender, weave, colorways and its BOM "
        + "(fabrics, trims, suppliers, consumption); answers 'what fabric/trims/supplier does <style> use', 'tell me about <style>'.";
    public string Example => "What fabric and trims does S2808MR0000 use?";
    public IReadOnlyList<string> Parameters => ["styleNo"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var found = await ToolKit.FindStyleAsync(styles, a.StyleNo, ct);
        var d = await styles.GetAsync(found.StyleId, ct) ?? throw new StyleRuleException(404, "The style was not found.");
        var s = d.Style;
        var fabrics = d.BomLines.Where(b => b.ContentClassCode == "FAB").Select(b => b.MaterialDescription ?? b.MaterialCode).Distinct().Take(3).ToList();
        var facts = $"{s.StyleNo} ({s.CustomerCode}, {s.SeasonCode}): {s.ModelName ?? s.Description ?? "no model name"}, {s.ProductTypeName ?? "no product type"}"
            + $"{(s.Gender is null ? "" : ", " + s.Gender)}{(s.WeaveTypeName is null ? "" : ", " + s.WeaveTypeName)}; {d.Colorways.Count} colorways, {d.BomLines.Count} BOM lines"
            + (fabrics.Count > 0 ? $"; fabric: {string.Join("; ", fabrics)}." : ".");
        return new LibraryToolResult(facts,
            ToolKit.Table(["section", "material", "description", "supplier", "consumption", "uom"],
                d.BomLines.Select(b => ((IReadOnlyList<string?>)[b.ContentClassCode, b.MaterialCode, b.MaterialDescription, b.SupplierName ?? b.NominatedSupplierName,
                    ToolKit.D(b.LcoConsumption ?? b.BrandConsumption), b.UomCode], b.MaterialId > 0 ? (string?)$"/materials/{b.MaterialId}" : null, b.ImageUrl)),
                d.BomLines.Count),
            [new LibraryLinkDto("style", s.StyleNo, $"/styles/{s.StyleId}")],
            new
            {
                s.StyleNo, customer = s.CustomerCode, season = s.SeasonCode, model = s.ModelName, s.Description, productType = s.ProductTypeName, s.Gender,
                weave = s.WeaveTypeName, businessUnit = s.BusinessUnitName, active = s.IsActive,
                colorways = d.Colorways.Select(c => $"{c.ColorwayCode} {c.ColorwayName}".Trim()),
                bom = d.BomLines.Take(25).Select(b => new
                {
                    section = b.ContentClassCode, material = b.MaterialCode, description = b.MaterialDescription, type = b.MaterialTypeName,
                    supplier = b.SupplierName ?? b.NominatedSupplierName, consumption = b.LcoConsumption ?? b.BrandConsumption, uom = b.UomCode,
                }),
                reusedFrom = d.Family.FirstOrDefault(f => f.IsCurrent)?.SourceStyleId is { } src ? d.Family.FirstOrDefault(f => f.StyleId == src)?.StyleNo : null,
            });
    }
}

/// <summary>What changed between two styles (the same rules as Change summary).</summary>
public sealed class CompareStylesTool(IStyleRepository styles, StyleAiAssistant assistant) : ILibraryTool
{
    public string Name => "compare_styles";
    public string Description => "What changed between two styles (header, colorways, BOM lines); without fromStyleNo, compares with the style it was "
        + "reused from. Answers 'what changed in <style>', 'difference between <style> and <style>'.";
    public string Example => "What changed between F2308MR1600_SS27 and the style it came from?";
    public IReadOnlyList<string> Parameters => ["styleNo", "fromStyleNo"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var to = await ToolKit.FindStyleAsync(styles, a.StyleNo, ct);
        int? fromId = a.FromStyleNo is null ? null : (await ToolKit.FindStyleAsync(styles, a.FromStyleNo, ct)).StyleId;
        var c = await assistant.CompareAsync(to.StyleId, fromId, ct);
        var facts = $"{c.To.StyleNo} compared with {c.From.StyleNo}: {c.Header.Count} header changes, {c.Colorways.Count} colorway changes, "
            + $"{c.BomLines.Count} BOM line changes ({c.UnchangedLines} lines unchanged).";
        return new LibraryToolResult(facts,
            ToolKit.Table(["change", "section", "material", "description", "fromMaterial"],
                c.BomLines.Select(b => ((IReadOnlyList<string?>)[b.Change, b.ContentClassCode, b.MaterialCode, b.MaterialDescription, b.FromMaterialCode], (string?)null)),
                c.BomLines.Count),
            [new LibraryLinkDto("compare", $"{c.From.StyleNo} → {c.To.StyleNo}", $"/ai/compare?styleId={c.To.StyleId}&fromStyleId={c.From.StyleId}")],
            new
            {
                from = c.From.StyleNo, to = c.To.StyleNo, relation = c.Relation,
                header = c.Header.Select(h => new { h.Field, h.Before, h.After }),
                colorways = c.Colorways.Take(10).Select(k => new { k.Change, k.ColorwayCode, k.ColorwayName }),
                bomLines = c.BomLines.Take(20).Select(b => new { b.Change, section = b.ContentClassCode, b.MaterialCode, b.MaterialDescription, b.FromMaterialCode, b.FromMaterialDescription }),
                unchangedLines = c.UnchangedLines,
            });
    }
}

/// <summary>What is in the library: totals and styles per season and customer.</summary>
public sealed class LibraryTotalsTool(IStyleRepository styles) : ILibraryTool
{
    public string Name => "library_totals";
    public string Description => "Totals of the whole library: styles, colorways, BOM lines, materials, suppliers, customers, seasons, styles with "
        + "pictures or BOMs, and styles per season and per customer. Answers 'how big is the library', 'how many materials/suppliers do we have'.";
    public string Example => "How many styles, materials and suppliers are in the library?";
    public IReadOnlyList<string> Parameters => [];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var d = await styles.GetDashboardAsync(ct);
        var t = d.Totals;
        var facts = $"The library has {ToolKit.N(t.Styles)} styles ({ToolKit.N(t.StylesWithBom)} with a BOM, {ToolKit.N(t.StylesWithImage)} with a picture), "
            + $"{ToolKit.N(t.Colorways)} colorways, {ToolKit.N(t.BomLines)} BOM lines, {ToolKit.N(t.Materials)} materials, {ToolKit.N(t.Suppliers)} suppliers, "
            + $"{ToolKit.N(t.Customers)} customers and {ToolKit.N(t.Seasons)} seasons.";
        return new LibraryToolResult(facts,
            ToolKit.Table(["season", "styles", "colorways", "bomLines"],
                d.Seasons.Select(s => ((IReadOnlyList<string?>)[s.SeasonCode, ToolKit.N(s.Styles), ToolKit.N(s.Colorways), ToolKit.N(s.BomLines)], (string?)$"/styles?season={s.SeasonCode}")),
                d.Seasons.Count),
            [new LibraryLinkDto("home", null, "/")],
            new
            {
                totals = t,
                seasons = d.Seasons.Select(s => new { s.SeasonCode, s.Styles }),
                customers = d.Customers.Select(c => new { c.Code, c.Name, c.Styles }),
            });
    }
}
