using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.StyleAi;

/// <summary>
/// A ready-to-paste prompt for outside design tools (StyTrix, Style3D AI, ...), built only from the style's data:
/// garment, construction, main fabrics with weight, colorway colours and visible trims (the same facts as the AI Studio
/// render, <see cref="RenderPrompt.Facts"/>). No AI call is made and nothing leaves LT ODM; the user copies the text.
///   text  = text-to-design: describes the whole garment.
///   image = image-to-design: the user attaches the style's sketch (or photo) and the prompt says how to use it.
/// Customer, model and brand names are never included, so the result is an unbranded design.
/// </summary>
public static partial class DesignPrompt
{
    public const string TextMode = "text";
    public const string ImageMode = "image";

    /// <summary>At most this many colorways are listed when no single colorway is chosen.</summary>
    private const int MaxColorways = 6;

    public static DesignPromptDto Build(StyleDetailDto d, string? mode, int? colorwayId)
    {
        var s = d.Style;
        var colorway = colorwayId is { } id ? d.Colorways.FirstOrDefault(c => c.ColorwayId == id) : null;
        var facts = RenderPrompt.Facts(d, colorway?.ColorwayId);   // fabrics already in plain words (RenderPrompt.Readable)

        // Image mode needs something to attach: the sketch first (clean lines), else the photo; otherwise fall back to text.
        var (referenceKind, referenceUrl) = !string.IsNullOrWhiteSpace(s.SketchUrl) ? ("sketch", s.SketchUrl)
            : !string.IsNullOrWhiteSpace(s.ImageUrl) ? ("photo", s.ImageUrl)
            : ((string?)null, (string?)null);
        var useImage = string.Equals(mode, ImageMode, StringComparison.OrdinalIgnoreCase) && referenceKind is not null;

        var garment = $"{(facts.Gender is null ? "" : facts.Gender + " ")}{(string.IsNullOrWhiteSpace(facts.Garment) ? "garment" : facts.Garment)}";
        var weight = Weight(s.Description);
        var recycled = d.BomLines.Any(l => string.Equals(l.ContentClassCode, "FAB", StringComparison.OrdinalIgnoreCase)
                                          && (l.MaterialDescription ?? "").Contains("recycl", StringComparison.OrdinalIgnoreCase))
                       || (s.Description ?? "").Contains("recycl", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        if (useImage)
        {
            sb.Append(referenceKind == "sketch"
                ? "Use the attached flat sketch as the base design. Keep its silhouette, proportions, seams, panel lines and trim placement exactly. "
                : "Use the attached product photo as the base design. Keep its silhouette, proportions, seams, panel lines and trim placement, and remove any logos, labels or brand marks. ");
            sb.Append($"Turn it into a photorealistic {garment}");
        }
        else
            sb.Append($"Design a {garment}");
        if (facts.Construction is not null && facts.Fabrics.Count == 0) sb.Append($" in {facts.Construction} construction");
        sb.Append(". ");

        if (facts.Fabrics.Count > 0)
            sb.Append($"Main fabric: {facts.Fabrics[0]}{(weight is null ? "" : $", about {weight} g/m²")}. ");
        else if (weight is not null)
            sb.Append($"Fabric weight about {weight} g/m². ");
        if (facts.Fabrics.Count > 1) sb.Append($"Also uses {string.Join("; ", facts.Fabrics.Skip(1))}. ");

        if (facts.Colours.Count > 0)
            sb.Append($"Colours: {string.Join(", ", facts.Colours)}. ");
        else if (colorway is null && ColorwayNames(d) is { Count: > 0 } names)
            sb.Append($"Colourways: {string.Join("; ", names)}. ");

        if (facts.Details.Count > 0) sb.Append($"Visible details: {string.Join(", ", facts.Details)}. ");
        if (recycled) sb.Append("Made with recycled fibres. ");

        sb.Append(useImage
            ? "Front view on a ghost mannequin, plain light-grey studio background, soft even lighting, true-to-life fabric texture and drape. "
            : "Show the front and back as clean technical flats, plus one photorealistic front view on a plain light-grey background. ");
        sb.Append("Original, unbranded design: no logos, brand names, readable text or labels.");

        return new DesignPromptDto(useImage ? ImageMode : TextMode, colorway?.ColorwayId, useImage ? referenceKind : null,
            useImage ? referenceUrl : null, referenceKind is not null, facts, RenderPrompt.Strip(sb.ToString()));
    }

    /// <summary>"…; 101.0 G/SQM; …" -> "101" (the weight the import puts in the style description).</summary>
    public static string? Weight(string? description)
    {
        var m = WeightRegex().Match(description ?? "");
        return m.Success && decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var gsm) && gsm > 0
            ? gsm.ToString("0.#", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>In-range colorway names in lower case ("black/white"), without duplicates.</summary>
    private static List<string> ColorwayNames(StyleDetailDto d)
        => d.Colorways
            .Where(c => !string.Equals(c.Status, "DROPPED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.ColorwayName))
            .Select(c => c.ColorwayName!.Trim().ToLowerInvariant())
            .Distinct()
            .Take(MaxColorways)
            .ToList();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:G/SQM|G/M2|GSM|G/M²)", RegexOptions.IgnoreCase)]
    private static partial Regex WeightRegex();
}
