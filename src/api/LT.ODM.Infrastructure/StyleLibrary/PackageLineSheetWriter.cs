using System.Globalization;
using LT.ODM.Application.StyleLibrary;
using SkiaSharp;
using static LT.ODM.Infrastructure.StyleLibrary.Pdf;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// Style package line sheet. PDF: A4 landscape like the tech pack, a header (package, customer, season, description) and the
/// styles as cards, two per row, in pitch order: picture (photo, else sketch), style number, model, product facts, main
/// fabrics, recycled content, offered colourways, lead time and the note for the client. Excel: see the .Excel part.
/// Labels are English: the line sheet goes to brands. What goes on it: <see cref="LineSheetContent"/>.
/// </summary>
public sealed partial class PackageLineSheetWriter(IStyleImageStore images) : IPackageLineSheetWriter
{
    private const float CardHeight = 200, CardGap = 14, CardPad = 10, PictureWidth = 150;

    public async Task<byte[]> WritePdfAsync(PackageDetailDto package, string generatedBy, DateTime generatedUtc, CancellationToken ct = default)
    {
        var items = LineSheetContent.Items(package);
        var pictures = new Dictionary<string, SKImage>();
        try
        {
            foreach (var i in items)
            {
                var url = i.ImageUrl ?? i.SketchUrl;
                if (StyleImageFile(url) is not { } name || pictures.ContainsKey(url!)) continue;
                // 640 is one of ImageRules.ThumbnailWidths (the store gives nothing for any other width), as for the tech pack.
                var opened = await images.OpenThumbnailAsync(name, 640, ct);
                if (opened is not { } file) continue;
                await using var content = file.Content;
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, ct);
                if (SKImage.FromEncodedData(buffer.ToArray()) is { } image) pictures[url!] = image;
            }

            using var fonts = new Fonts();
            var layout = new Layout(fonts);
            var p = package.Package;
            Header(layout, p, items.Count);
            if (items.Count == 0) layout.Note("No styles in this package.");
            var width = (layout.Right - layout.Left - CardGap) / 2;
            for (var n = 0; n < items.Count; n++)
            {
                var column = n % 2;
                if (column == 0 && layout.Y + CardHeight > layout.Bottom) layout.NewPage();
                var item = items[n];
                Card(layout, item, pictures.GetValueOrDefault(item.ImageUrl ?? item.SketchUrl ?? ""), layout.Left + column * (width + CardGap), layout.Y, width);
                if (column == 1 || n == items.Count - 1) layout.Y += CardHeight + CardGap;
            }
            return layout.Render(
                $"{p.Name} · {p.CustomerCode}{(p.SeasonCode is null ? "" : " " + p.SeasonCode)} · Line sheet",
                $"Generated {generatedUtc:yyyy-MM-dd HH:mm} UTC by {generatedBy}",
                new SKDocumentPdfMetadata
                {
                    Title = $"Line sheet {p.Name}", Author = generatedBy, Creator = "LT ODM", Producer = "LT ODM", Creation = generatedUtc, Modified = generatedUtc,
                });
        }
        finally
        {
            foreach (var picture in pictures.Values) picture.Dispose();
        }
    }

    private static void Header(Layout l, PackageHeaderDto p, int styles)
    {
        var f = l.Fonts;
        l.Text("LINE SHEET", f.Bold, 8, Layout.Accent, l.Left, l.Y + 8);
        var nameWidth = l.Right - l.Left - 260;
        l.Text(l.Fit(p.Name, f.Bold, 20, nameWidth), f.Bold, 20, Layout.Ink, l.Left, l.Y + 30);
        l.TextRight($"{p.CustomerName} ({p.CustomerCode})", f.Bold, 11, Layout.Ink, l.Right, l.Y + 30);
        l.TextRight(string.Join(" · ", new[] { p.SeasonCode is null ? null : $"Season {p.SeasonCode}", $"{styles} styles" }.Where(v => v is not null)),
            f.Regular, 10, Layout.Muted, l.Right, l.Y + 46);
        l.Y += 40;
        if (!string.IsNullOrWhiteSpace(p.Description))
        {
            foreach (var line in Lines(l, p.Description!, f.Regular, 10, nameWidth, 3))
            {
                l.Y += 13;
                l.Text(line, f.Regular, 10, Layout.Muted, l.Left, l.Y);
            }
        }
        l.Y += 12;
        l.Rule(Layout.Accent, 1.5f);
        l.Y += 14;
    }

    private static void Card(Layout l, PackageItemDto i, SKImage? picture, float x, float y, float width)
    {
        var f = l.Fonts;
        l.Line(x, y, x + width, y, Layout.Border);
        l.Line(x, y + CardHeight, x + width, y + CardHeight, Layout.Border);
        l.Line(x, y, x, y + CardHeight, Layout.Border);
        l.Line(x + width, y, x + width, y + CardHeight, Layout.Border);
        l.Picture(picture, null, x + CardPad, y + CardPad, PictureWidth, CardHeight - 2 * CardPad);
        if (picture is null)
        {
            const string none = "No picture";
            l.Text(none, f.Regular, 9, Layout.Muted, x + CardPad + PictureWidth / 2 - l.Measure(none, f.Regular, 9) / 2, y + CardHeight / 2 + 3);
        }

        var tx = x + CardPad + PictureWidth + 12;
        var tw = x + width - CardPad - tx;
        var bottom = y + CardHeight - CardPad;
        var ty = y + CardPad;

        // Draws lines while they fit in the card; false once the card is full.
        bool Put(string text, SKTypeface face, float size, SKColor colour, float lineHeight)
        {
            if (ty + lineHeight > bottom) return false;
            ty += lineHeight;
            l.Text(l.Fit(text, face, size, tw), face, size, colour, tx, ty - 3);
            return true;
        }
        bool Label(string text) => Put(text.ToUpperInvariant(), f.Bold, 6.5f, Layout.Muted, 11);

        Put(i.StyleNo, f.Bold, 12, Layout.Ink, 15);
        if (!string.IsNullOrWhiteSpace(i.ModelName ?? i.Description)) Put((i.ModelName ?? i.Description)!, f.Regular, 9.5f, Layout.Ink, 13);
        if (LineSheetContent.Facts(i) is { Length: > 0 } facts) Put(facts, f.Regular, 8.5f, Layout.Muted, 12);

        var fabrics = i.Materials.Where(m => m.Kind == "Fabric").Take(2).ToList();
        if (fabrics.Count > 0 && Label("Fabric"))
        {
            foreach (var m in fabrics)
                foreach (var line in Lines(l, LineSheetContent.MaterialText(m) + (m.Recycled ? " (recycled)" : ""), f.Regular, 8.5f, tw, 2))
                    Put(line, f.Regular, 8.5f, Layout.Ink, 11);
        }
        if (LineSheetContent.Recycled(i) is { } recycled) Put(recycled, f.Bold, 8.5f, new SKColor(0x04, 0x78, 0x57), 12);

        var colorways = LineSheetContent.Colorways(i);
        if (colorways.Count > 0 && Label($"Colourways ({colorways.Count.ToString(CultureInfo.InvariantCulture)})"))
            foreach (var line in Lines(l, string.Join(", ", colorways.Select(LineSheetContent.ColorwayText)), f.Regular, 8.5f, tw, 2))
                Put(line, f.Regular, 8.5f, Layout.Ink, 11);

        if (i.GarmentLeadTimeDays is { } days && days > 0) Put($"Lead time {days} days", f.Regular, 8.5f, Layout.Muted, 12);

        if (!string.IsNullOrWhiteSpace(i.PitchNote))
        {
            ty += 3;
            foreach (var line in Lines(l, i.PitchNote!, f.Regular, 9, tw, 6))
                if (!Put(line, f.Regular, 9, Layout.Ink, 11.5f)) break;
        }
    }

    /// <summary>Wrapped lines, at most maxLines; the last one ends in "…" when text was left out.</summary>
    private static List<string> Lines(Layout l, string text, SKTypeface face, float size, float width, int maxLines)
    {
        var lines = l.Wrap(text, face, size, width);
        if (lines.Count <= maxLines) return lines;
        var kept = lines.Take(maxLines).ToList();
        var last = kept[^1];
        while (last.Length > 1 && l.Measure(last + "…", face, size) > width) last = last[..^1];
        kept[^1] = last.TrimEnd() + "…";
        return kept;
    }
}
