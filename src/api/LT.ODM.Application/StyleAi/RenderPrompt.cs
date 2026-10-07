using System.Text;
using System.Text.RegularExpressions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.StyleAi;

/// <summary>
/// Turns a style's data into a description an image model can draw: garment, construction, main fabrics, the chosen
/// colorway's fabric colours and the visible trims (zipper, drawcord, heat-transfer graphic...). Labels, packaging,
/// thread, interlining and padding are left out (not visible). Customer names and brand words are never included:
/// the picture is a product visual, not a branded image.
/// </summary>
public static partial class RenderPrompt
{
    /// <summary>Material types that show on the finished garment, in plain words.</summary>
    private static readonly Dictionary<string, string> VisibleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ZIP"] = "zipper", ["ZPLR"] = "zipper pullers", ["TAPE"] = "woven tape trim", ["TAPEPCS"] = "woven tape trim",
        ["HT TR"] = "heat-transfer graphic", ["HTR"] = "heat-transfer graphic", ["ELTC"] = "elastic", ["STOP"] = "cord stoppers",
        ["TIECORD"] = "drawcord", ["CORD"] = "drawcord", ["HKBR"] = "hook-and-bar closure", ["EYLT"] = "metal eyelets",
        ["SBUT"] = "snap buttons", ["BUTN"] = "buttons", ["BADG"] = "badge", ["BRING"] = "buckle", ["PRT"] = "printed artwork",
    };

    private static readonly string[] BrandWords = ["adidas", "skechers", "alo", "parley", "primegreen", "originals"];

    public static RenderFactsDto Facts(StyleDetailDto d, int? colorwayId)
    {
        var s = d.Style;
        var colorway = colorwayId is { } id ? d.Colorways.FirstOrDefault(c => c.ColorwayId == id) : null;

        string? ColourOf(BomLineDto line)
        {
            if (colorway is null) return null;
            var c = line.Colorways.FirstOrDefault(x => x.ColorwayId == colorway.ColorwayId)?.MaterialColorDescription;
            return string.IsNullOrWhiteSpace(c) || c.Contains("NO COLOR", StringComparison.OrdinalIgnoreCase) ? null : Lower(c);
        }

        static decimal Eff(BomLineDto l) => l.BrandConsumption is > 0 ? l.BrandConsumption.Value : l.LcoConsumption ?? 0;

        // Biggest fabric first: the first one is the shell.
        var fabricLines = d.BomLines.Where(l => Same(l.ContentClassCode, "FAB")).OrderByDescending(Eff).ThenBy(l => l.LineSeq).ToList();
        var fabrics = fabricLines.Select(l => ShortFabric(l.MaterialDescription ?? l.MaterialCode)).Where(f => f.Length > 0).Distinct().Take(3).ToList();
        var colours = fabricLines.Select(ColourOf).OfType<string>().Distinct().Take(4).ToList();
        if (colours.Count == 0 && colorway?.ColorwayName is { } name && !string.IsNullOrWhiteSpace(name)) colours.Add(Lower(name));

        var details = d.BomLines
            .Where(l => Same(l.ContentClassCode, "ACC") || Same(l.ContentClassCode, "ART"))
            .OrderBy(l => l.LineSeq)
            .Select(l =>
            {
                var word = l.MaterialTypeCode is { } t && VisibleTypes.TryGetValue(t, out var w) ? w
                    : Same(l.ContentClassCode, "ART") ? "printed artwork" : null;
                if (word is null) return null;
                var colour = ColourOf(l);
                return colour is null ? word : $"{word} ({colour})";
            })
            .OfType<string>()
            .DistinctBy(x => x.Split(" (")[0])
            .Take(8)
            .ToList();

        return new RenderFactsDto(
            Lower(s.ProductTypeName ?? s.ProductTypeCode), Gender(s.Gender), Construction(s.WeaveTypeCode), Clean(s.ModelName),
            colorway?.ColorwayCode, colorway?.ColorwayName is { } cn ? Lower(cn) : null, fabrics, colours, details);
    }

    /// <summary>The default prompt (the user can edit it, or have the text AI rewrite it).</summary>
    public static string Build(RenderFactsDto f, bool withSketch)
    {
        var sb = new StringBuilder();
        var who = f.Gender is null ? "" : f.Gender + " ";
        sb.Append($"Photorealistic e-commerce product photo of a {who}{f.Garment ?? "garment"}");
        // The fabric names say how it is made; the style's weave code is only a fallback (some styles are coded woven with fleece fabrics).
        if (f.Construction is not null && f.Fabrics.Count == 0) sb.Append($", {f.Construction} construction");
        sb.Append(". ");
        if (f.Fabrics.Count > 0) sb.Append($"Main fabric: {f.Fabrics[0]}. ");
        if (f.Fabrics.Count > 1) sb.Append($"Also uses {string.Join("; ", f.Fabrics.Skip(1))}. ");
        if (f.Colours.Count > 0) sb.Append($"Colours: {string.Join(", ", f.Colours)}. ");
        if (f.Details.Count > 0) sb.Append($"Visible details: {string.Join(", ", f.Details)}. ");
        if (withSketch) sb.Append("Follow the attached flat sketch exactly for the silhouette, seams, panels and trim placement. ");
        sb.Append("Ghost-mannequin front view, centred, on a plain light-grey studio background, soft even lighting, ");
        sb.Append("true-to-life fabric texture and drape. No person, no brand names, no logos, no readable text or labels.");
        return Strip(sb.ToString());
    }

    /// <summary>Brand words out (customer and programme names), so the picture never imitates a brand.</summary>
    public static string Strip(string text)
    {
        foreach (var w in BrandWords)
            text = Regex.Replace(text, $@"\b{Regex.Escape(w)}\b", "", RegexOptions.IgnoreCase);
        return SpacesRegex().Replace(text.Replace("\"", "").Replace("''", ""), " ").Replace(" ,", ",").Replace(" .", ".").Trim();
    }

    /// <summary>"70% COTTON 30% RECYCLED POLYESTER,SOLID FLEECE,32s/1 cotton + ..." -> "70% cotton 30% recycled polyester, solid fleece".</summary>
    internal static string ShortFabric(string text)
    {
        var parts = text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !YarnRegex().IsMatch(p))
            .Take(2);
        return Strip(Lower(string.Join(", ", parts)));
    }

    private static string? Gender(string? g) => g?.ToUpperInvariant() switch
    {
        "MALE" => "men's", "FEMALE" => "women's", "UNISEX" => "unisex", "KIDS" => "kids'", _ => null,
    };

    private static string? Construction(string? weave) => weave?.ToUpperInvariant() switch
    {
        "KNT" => "knit", "WVN" => "woven", _ => null,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : Strip(value.Trim());

    private static string Lower(string? value) => SpacesRegex().Replace((value ?? "").Trim().ToLowerInvariant(), " ");

    private static bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    /// <summary>Yarn specs ("32s/1 cotton", "75D/36F", "TEX#24") mean nothing to an image model.</summary>
    [GeneratedRegex(@"\d+\s*[sSdD]\s*/\s*\d+|\bTEX#|\d+F\b", RegexOptions.IgnoreCase)]
    private static partial Regex YarnRegex();
}
