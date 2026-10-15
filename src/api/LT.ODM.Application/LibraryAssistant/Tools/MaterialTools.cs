using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.LibraryAssistant.Tools;

// Tools on materials (the Materials screen's procedures) and the BOM check.

internal static class MaterialScope
{
    /// <summary>Materials views take one season: the first one asked about.</summary>
    public static MaterialInsightQuery Query(LibraryToolArgs a) => new(a.Customer, a.Seasons.FirstOrDefault(), null, a.ProductTypes.FirstOrDefault());

    public static string Url(LibraryToolArgs a, string view)
    {
        var p = new List<string> { "view=" + view };
        if (a.Customer is not null) p.Add("customer=" + Uri.EscapeDataString(a.Customer));
        if (a.Seasons.FirstOrDefault() is { } s) p.Add("season=" + Uri.EscapeDataString(s));
        if (view == "trims" && a.ProductTypes.FirstOrDefault() is { } t) p.Add("productType=" + Uri.EscapeDataString(t));
        return "/materials?" + string.Join('&', p);
    }

    public static string Scope(LibraryToolArgs a)
        => string.Join("; ", new[] { a.Customer, a.Seasons.FirstOrDefault() }.Where(x => x is not null)) is { Length: > 0 } s ? s : "the whole library";
}

/// <summary>A material by code (where it is used, suppliers) or materials matching words.</summary>
public sealed class MaterialLookupTool(IMaterialRepository materials) : ILibraryTool
{
    public string Name => "material_lookup";
    public string Description => "A material by its code (materialCode): which styles use it, consumption, suppliers, colours; or materials matching words "
        + "(material), most used first. Answers 'which styles use <material>', 'who supplies <material>', 'what ... materials do we use'.";
    public string Example => "Which styles use material 100% Recycled Polyester Ripstop, and from which suppliers?";
    public IReadOnlyList<string> Parameters => ["materialCode", "material", "customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var words = a.MaterialCode ?? a.Material ?? throw new StyleRuleException(400, "Which material? Give its code or words from its description.");
        var list = await materials.ListAsync(new MaterialListQuery(words, null, null, null, a.Customer, a.Seasons.FirstOrDefault(), "used", 0, 15), ct);
        var exact = list.Items.FirstOrDefault(m => string.Equals(m.MaterialCode, words, StringComparison.OrdinalIgnoreCase))
            ?? (list.Total == 1 ? list.Items[0] : null);

        if (exact is not null && await materials.GetAsync(exact.MaterialId, ct) is { } d)
        {
            var h = d.Header;
            var facts = $"{h.MaterialCode} ({h.Description}) is used on {ToolKit.N(h.Styles)} styles ({ToolKit.N(h.Lines)} BOM lines) for {h.Customers} customers "
                + $"in {h.Seasons} seasons; suppliers: {(d.Suppliers.Count == 0 ? "none recorded" : string.Join(", ", d.Suppliers.Take(3).Select(s => $"{s.SupplierName ?? "no supplier"} ({s.Styles} styles)")))}.";
            return new LibraryToolResult(facts,
                ToolKit.Table(["styleNo", "season", "customer", "productType", "consumption", "uom", "supplier"],
                    d.Uses.Take(50).Select(u => ((IReadOnlyList<string?>)[u.StyleNo, u.SeasonCode, u.CustomerCode, u.ProductTypeName, ToolKit.D(u.LcoConsumption ?? u.BrandConsumption),
                        u.UomCode, u.SupplierName ?? u.NominatedSupplierName], (string?)$"/styles/{u.StyleId}")),
                    h.Lines),
                [new LibraryLinkDto("material", h.MaterialCode, $"/materials/{h.MaterialId}")],
                new
                {
                    h.MaterialCode, h.Description, type = h.MaterialTypeName, section = h.ContentClassName, h.Styles, h.Lines, h.Customers, h.Seasons,
                    suppliers = d.Suppliers.Select(s => new { s.SupplierName, s.Styles }),
                    consumption = d.Benchmarks.Select(b => new { productType = b.ProductTypeName, uom = b.UomCode, b.Styles, median = b.Median }),
                    colours = d.Colours.Take(10).Select(c => c.MaterialColorDescription ?? c.MaterialColorCode),
                    usedOn = d.Uses.Take(10).Select(u => new { u.StyleNo, season = u.SeasonCode, customer = u.CustomerCode }),
                });
        }

        var listFacts = list.Total == 0 ? $"No materials match \"{words}\"."
            : $"{ToolKit.N(list.Total)} materials match \"{words}\"; most used: {string.Join(", ", list.Items.Take(3).Select(m => $"{m.MaterialCode} ({m.Styles} styles)"))}.";
        return new LibraryToolResult(listFacts,
            ToolKit.Table(["material", "description", "type", "styles", "supplier"],
                list.Items.Select(m => ((IReadOnlyList<string?>)[m.MaterialCode, m.Description, m.MaterialTypeName, ToolKit.N(m.Styles), m.TopSupplierName], (string?)$"/materials/{m.MaterialId}")),
                list.Total),
            [new LibraryLinkDto("materials", words, "/materials?search=" + Uri.EscapeDataString(words))],
            new { total = list.Total, materials = list.Items.Select(m => new { m.MaterialCode, m.Description, type = m.MaterialTypeName, m.Styles, supplier = m.TopSupplierName }) });
    }
}

public sealed class SupplierShareTool(IMaterialRepository materials) : ILibraryTool
{
    public string Name => "supplier_share";
    public string Description => "Suppliers ranked by share of styles, with how many materials only one supplier provides; answers 'who are our main suppliers', "
        + "'how dependent are we on <supplier>', 'single-source materials'.";
    public string Example => "Who are our main suppliers for ADI in 2027-SS?";
    public IReadOnlyList<string> Parameters => ["customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var r = await materials.SuppliersAsync(MaterialScope.Query(a), ct);
        var t = r.Totals;
        var facts = $"For {MaterialScope.Scope(a)}: {ToolKit.N(t.Suppliers)} suppliers on {ToolKit.N(t.Styles)} styles; {ToolKit.N(t.SingleSource)} materials have one supplier, "
            + $"{ToolKit.N(t.MultiSource)} several; {ToolKit.N(t.LinesWithoutSupplier)} of {ToolKit.N(t.Lines)} BOM lines have no supplier. Top: "
            + string.Join(", ", r.Suppliers.Take(3).Select(s => $"{s.SupplierName ?? s.SupplierCode} ({ToolKit.Pct(s.Share)}% of styles)")) + ".";
        return new LibraryToolResult(facts,
            ToolKit.Table(["supplier", "styles", "share", "materials", "onlySource"],
                r.Suppliers.Take(30).Select(s => ((IReadOnlyList<string?>)[s.SupplierName ?? s.SupplierCode, ToolKit.N(s.Styles), $"{ToolKit.Pct(s.Share)}%", ToolKit.N(s.Materials), ToolKit.N(s.OnlySource)], (string?)null)),
                r.Suppliers.Count),
            [new LibraryLinkDto("materialsView", "suppliers", MaterialScope.Url(a, "suppliers"))],
            new { totals = t, suppliers = r.Suppliers.Take(10).Select(s => new { s.SupplierName, s.Styles, sharePercent = ToolKit.Pct(s.Share), s.Materials, s.OnlySource }) });
    }
}

public sealed class RecycledShareTool(IMaterialRepository materials) : ILibraryTool
{
    public string Name => "recycled_share";
    public string Description => "How many styles use recycled fabric (all fabrics or some), per season and per product type; answers 'how sustainable', "
        + "'share of recycled', 'which product types use recycled fabric'.";
    public string Example => "How much recycled fabric do we use per season?";
    public IReadOnlyList<string> Parameters => ["customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var r = await materials.RecycledAsync(MaterialScope.Query(a), ct);
        int styles = r.Seasons.Sum(s => s.Styles), all = r.Seasons.Sum(s => s.AllRecycled), some = r.Seasons.Sum(s => s.SomeRecycled);
        var facts = $"For {MaterialScope.Scope(a)}: of {ToolKit.N(styles)} styles, {ToolKit.N(all)} use only recycled fabric and {ToolKit.N(some)} some recycled fabric.";
        return new LibraryToolResult(facts,
            ToolKit.Table(["season", "styles", "allRecycled", "someRecycled", "recycledLines", "fabricLines"],
                r.Seasons.Select(s => ((IReadOnlyList<string?>)[s.Key, ToolKit.N(s.Styles), ToolKit.N(s.AllRecycled), ToolKit.N(s.SomeRecycled), ToolKit.N(s.RecycledLines), ToolKit.N(s.FabricLines)], (string?)null)),
                r.Seasons.Count),
            [new LibraryLinkDto("materialsView", "recycled", MaterialScope.Url(a, "recycled"))],
            new
            {
                totalStyles = styles, onlyRecycled = all, someRecycled = some,
                seasons = r.Seasons.Select(s => new { season = s.Key, s.Styles, s.AllRecycled, s.SomeRecycled }),
                productTypes = r.ProductTypes.Take(10).Select(p => new { productType = p.Name ?? p.Key, p.Styles, p.AllRecycled, p.SomeRecycled }),
            });
    }
}

public sealed class StandardTrimsTool(IMaterialRepository materials) : ILibraryTool
{
    public string Name => "standard_trims";
    public string Description => "The trims and materials most styles of a product type share, and how many different materials do one job (e.g. how many "
        + "different zippers); answers 'standard trims for <product type>', 'can we reduce the number of zippers'.";
    public string Example => "What are the standard trims on jackets?";
    public IReadOnlyList<string> Parameters => ["productTypes", "customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var r = await materials.StandardTrimsAsync(MaterialScope.Query(a), ct);
        var pt = a.ProductTypes.FirstOrDefault() ?? "all product types";
        var facts = $"For {pt} ({MaterialScope.Scope(a)}, {ToolKit.N(r.StylesWithBom)} styles with a BOM): most shared: "
            + string.Join(", ", r.Materials.Take(3).Select(m => $"{m.Description ?? m.MaterialCode} ({ToolKit.Pct(m.Share)}% of styles)")) + ".";
        return new LibraryToolResult(facts,
            ToolKit.Table(["material", "description", "type", "styles", "share"],
                r.Materials.Take(30).Select(m => ((IReadOnlyList<string?>)[m.MaterialCode, m.Description, m.MaterialTypeName, ToolKit.N(m.Styles), $"{ToolKit.Pct(m.Share)}%"], (string?)$"/materials/{m.MaterialId}")),
                r.Materials.Count),
            [new LibraryLinkDto("materialsView", "trims", MaterialScope.Url(a, "trims"))],
            new
            {
                productType = pt, stylesWithBom = r.StylesWithBom,
                types = r.Types.Take(8).Select(t => new { type = t.MaterialTypeName, t.Materials, usedOnOneStyleOnly = t.OneOffs, t.Styles }),
                materials = r.Materials.Take(10).Select(m => new { m.MaterialCode, m.Description, type = m.MaterialTypeName, m.Styles, sharePercent = ToolKit.Pct(m.Share) }),
            });
    }
}

public sealed class DuplicateMaterialsTool(IMaterialRepository materials) : ILibraryTool
{
    public string Name => "duplicate_materials";
    public string Description => "Materials that are probably the same (same description or same base code) under different codes; answers 'duplicate "
        + "materials', 'which materials could be merged'.";
    public string Example => "Which materials look like duplicates?";
    public IReadOnlyList<string> Parameters => ["customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var r = await materials.DuplicatesAsync(MaterialScope.Query(a), ct);
        var facts = $"For {MaterialScope.Scope(a)}: {ToolKit.N(r.TotalGroups)} groups of probably-same materials ({ToolKit.N(r.TotalMaterials)} codes).";
        return new LibraryToolResult(facts,
            ToolKit.Table(["material", "description", "codes", "styles"],
                r.Groups.Take(30).Select(g => ((IReadOnlyList<string?>)[g.Members[0].MaterialCode, g.Members[0].Description, string.Join(", ", g.Members.Select(m => m.MaterialCode)), ToolKit.N(g.Styles)],
                    (string?)$"/materials/{g.Members[0].MaterialId}")),
                r.TotalGroups),
            [new LibraryLinkDto("materialsView", "duplicates", MaterialScope.Url(a, "duplicates"))],
            new { r.TotalGroups, r.TotalMaterials, groups = r.Groups.Take(8).Select(g => new { g.Styles, codes = g.Members.Select(m => new { m.MaterialCode, m.Description }) }) });
    }
}

public sealed class ColourUsageTool(IMaterialRepository materials) : ILibraryTool
{
    public string Name => "colour_usage";
    public string Description => "The material colours used most, on how many colorways and styles; answers 'most used colours', 'how many styles use black'.";
    public string Example => "Which colours do we use most in 2027-SS?";
    public IReadOnlyList<string> Parameters => ["customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var r = await materials.ColoursAsync(MaterialScope.Query(a), ct);
        var facts = $"For {MaterialScope.Scope(a)}: {ToolKit.N(r.Count)} colours; most used: "
            + string.Join(", ", r.Take(3).Select(c => $"{c.Colour} ({ToolKit.N(c.Styles)} styles)")) + ".";
        return new LibraryToolResult(facts,
            ToolKit.Table(["colour", "styles", "colorways", "materials", "codes"],
                r.Take(30).Select(c => ((IReadOnlyList<string?>)[c.Colour, ToolKit.N(c.Styles), ToolKit.N(c.Colorways), ToolKit.N(c.Materials), ToolKit.N(c.Codes)], (string?)null)),
                r.Count),
            [new LibraryLinkDto("materialsView", "colours", MaterialScope.Url(a, "colours"))],
            new { colours = r.Take(12).Select(c => new { c.Colour, c.Styles, c.Colorways, c.Materials }) });
    }
}

/// <summary>The BOM check rules on one style or a customer / season.</summary>
public sealed class BomCheckTool(StyleAiAssistant assistant, Abstractions.IStyleRepository styles) : ILibraryTool
{
    public string Name => "bom_check";
    public string Description => "Checks BOMs for problems (missing consumption, LCO vs brand gaps, unusual consumption vs similar styles, no supplier) "
        + "for one style (styleNo) or a customer / season; answers 'any BOM problems', 'which BOM lines look wrong'.";
    public string Example => "Are there BOM problems in SKE 2027-SS?";
    public IReadOnlyList<string> Parameters => ["styleNo", "customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        int? styleId = a.StyleNo is null ? null : (await ToolKit.FindStyleAsync(styles, a.StyleNo, ct)).StyleId;
        var season = a.Seasons.FirstOrDefault();
        var r = await assistant.BomCheckAsync(new BomCheckQuery(styleId, styleId is null ? a.Customer : null, styleId is null ? season : null), ct);
        var scope = a.StyleNo ?? MaterialScope.Scope(a);
        var facts = $"BOM check of {scope}: {ToolKit.N(r.StylesChecked)} styles, {ToolKit.N(r.LinesChecked)} lines, {ToolKit.N(r.TotalFindings)} findings"
            + (r.Rules.Count == 0 ? "." : ": " + string.Join(", ", r.Rules.Select(x => $"{x.RuleCode} {x.Lines} ({x.Severity})")) + ".");
        var url = styleId is { } id ? $"/ai/bom-check?styleId={id}"
            : "/ai/bom-check?" + string.Join('&', new[] { a.Customer is null ? null : "customer=" + a.Customer, season is null ? null : "season=" + season }.Where(x => x is not null).DefaultIfEmpty("all=1"));
        return new LibraryToolResult(facts,
            ToolKit.Table(["styleNo", "material", "description", "rule", "severity"],
                r.Findings.Take(30).Select(f => ((IReadOnlyList<string?>)[f.StyleNo, f.MaterialCode, f.MaterialDescription, f.RuleCode, f.Severity], (string?)$"/styles/{f.StyleId}")),
                r.TotalFindings),
            [new LibraryLinkDto("bomCheck", scope, url)],
            new
            {
                r.StylesChecked, r.LinesChecked, r.TotalFindings,
                rules = r.Rules.Select(x => new { x.RuleCode, x.Severity, x.Lines, x.Styles }),
                examples = r.Findings.Take(10).Select(f => new { f.StyleNo, f.MaterialCode, f.MaterialDescription, f.RuleCode, f.Value, f.RefValue }),
            });
    }
}
