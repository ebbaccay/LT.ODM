using System.Globalization;
using LT.ODM.Application.StyleLibrary;
using SkiaSharp;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// Tech pack PDF drawn with SkiaSharp (already used for images; no PDF library to license). A4 landscape, because the
/// BOM and the colour matrix are wide. Pages are laid out first (a list of drawing steps per page) and drawn after, so
/// the footer can say "page x of y". Text stays text in the PDF (searchable, copyable); fonts are embedded as subsets.
/// Labels are English: the tech pack goes to brands and suppliers.
/// </summary>
public sealed class TechPackPdfWriter(IStyleImageStore images) : ITechPackWriter
{
    private const string ImagePrefix = "/api/v1/styles/images/";
    /// <summary>Colorways per colour matrix table; more colorways repeat the table with the next ones.</summary>
    private const int MatrixColorways = 4;

    public async Task<byte[]> WriteAsync(StyleDetailDto d, string generatedBy, DateTime generatedUtc, CancellationToken ct = default)
    {
        // Pictures first (async), drawing after (Skia is synchronous). Sketch and photo large, colorway pictures small.
        var pictures = new Dictionary<string, SKImage>();
        try
        {
            async Task Load(string? url, int width)
            {
                var name = FileName(url);
                if (name is null || pictures.ContainsKey(url!)) return;
                var opened = await images.OpenThumbnailAsync(name, width, ct);
                if (opened is not { } file) return;
                await using var content = file.Content;
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, ct);
                if (SKImage.FromEncodedData(buffer.ToArray()) is { } image) pictures[url!] = image;
            }

            await Load(d.Style.SketchUrl, 640);
            await Load(d.Style.ImageUrl, 640);
            foreach (var c in d.Colorways) await Load(c.ImageUrl, 160);

            using var fonts = new Fonts();
            var layout = new Layout(fonts);
            Header(layout, d, pictures);
            Colorways(layout, d, pictures);
            Bom(layout, d);
            ColourMatrix(layout, d);
            return layout.Render(
                $"{d.Style.StyleNo} · {d.Style.CustomerCode} {d.Style.SeasonCode} · Tech pack",
                $"Generated {generatedUtc:yyyy-MM-dd HH:mm} UTC by {generatedBy}",
                new SKDocumentPdfMetadata
                {
                    Title = $"Tech pack {d.Style.StyleNo}", Author = generatedBy, Creator = "LT ODM", Producer = "LT ODM",
                    Creation = generatedUtc, Modified = generatedUtc,
                });
        }
        finally
        {
            foreach (var p in pictures.Values) p.Dispose();
        }
    }

    /// <summary>The stored file name of a style image address, or null for any other address (skipped).</summary>
    private static string? FileName(string? url)
    {
        if (url is null || !url.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var name = url[ImagePrefix.Length..];
        var q = name.IndexOf('?');
        return q >= 0 ? name[..q] : name;
    }

    // ----- Sections -----

    private static void Header(Layout l, StyleDetailDto d, IReadOnlyDictionary<string, SKImage> pictures)
    {
        var s = d.Style;
        var f = l.Fonts;
        // Title band
        l.Text("TECH PACK", f.Bold, 8, Layout.Accent, l.Left, l.Y + 8);
        l.Text(s.StyleNo, f.Bold, 20, Layout.Ink, l.Left, l.Y + 30);
        if (!string.IsNullOrWhiteSpace(s.ModelName)) l.Text(s.ModelName!, f.Regular, 11, Layout.Muted, l.Left, l.Y + 46);
        l.TextRight($"{s.CustomerName} ({s.CustomerCode})", f.Bold, 11, Layout.Ink, l.Right, l.Y + 30);
        l.TextRight($"Season {s.SeasonCode}" + (s.IsActive ? "" : " · inactive"), f.Regular, 10, Layout.Muted, l.Right, l.Y + 46);
        l.Y += 56;
        l.Rule(Layout.Accent, 1.5f);
        l.Y += 14;

        // Pictures left, facts right
        const float pic = 190, gap = 12;
        var top = l.Y;
        l.Picture(pictures.GetValueOrDefault(s.SketchUrl ?? ""), "Sketch", l.Left, top, pic, pic);
        l.Picture(pictures.GetValueOrDefault(s.ImageUrl ?? ""), "Photo", l.Left + pic + gap, top, pic, pic);

        var x = l.Left + 2 * (pic + gap) + 8;
        var width = l.Right - x;
        var facts = new (string Label, string? Value)[]
        {
            ("Customer", $"{s.CustomerName} ({s.CustomerCode})"), ("Season", s.SeasonCode),
            ("Model ID", s.ModelCode), ("Model name", s.ModelName),
            ("Product type", s.ProductTypeName ?? s.ProductTypeCode), ("Weave", s.WeaveTypeName ?? s.WeaveTypeCode),
            ("Gender", s.Gender), ("Business unit", s.BusinessUnitName ?? s.BusinessUnitCode),
            ("Garment lead time", s.GarmentLeadTimeDays is { } days ? $"{days} days" : null), ("Base style", s.BaseStyleNo == s.StyleNo ? null : s.BaseStyleNo),
            ("Colorways", d.Colorways.Count.ToString(CultureInfo.InvariantCulture)), ("BOM lines", d.BomLines.Count.ToString(CultureInfo.InvariantCulture)),
        };
        var col = (width - 16) / 2;
        var y = top;
        for (var i = 0; i < facts.Length; i += 2)
        {
            var rowHeight = 0f;
            for (var j = 0; j < 2 && i + j < facts.Length; j++)
            {
                var (label, value) = facts[i + j];
                var cx = x + j * (col + 16);
                l.Text(label, f.Regular, 8, Layout.Muted, cx, y + 8);
                var lines = l.Wrap(value ?? "—", f.Regular, 10, col);
                for (var k = 0; k < lines.Count; k++) l.Text(lines[k], f.Regular, 10, Layout.Ink, cx, y + 21 + k * 12.5f);
                rowHeight = Math.Max(rowHeight, 18 + lines.Count * 12.5f);
            }
            y += rowHeight + 6;
        }
        if (!string.IsNullOrWhiteSpace(s.Description))
        {
            l.Text("Description", f.Regular, 8, Layout.Muted, x, y + 8);
            var lines = l.Wrap(s.Description!, f.Regular, 10, width);
            for (var k = 0; k < lines.Count; k++) l.Text(lines[k], f.Regular, 10, Layout.Ink, x, y + 21 + k * 12.5f);
            y += 18 + lines.Count * 12.5f;
        }
        l.Y = Math.Max(top + pic, y) + 18;
    }

    private static void Colorways(Layout l, StyleDetailDto d, IReadOnlyDictionary<string, SKImage> pictures)
    {
        l.Heading("Colorways", d.Colorways.Count);
        if (d.Colorways.Count == 0)
        {
            l.Note("No colorways.");
            return;
        }
        var lineCount = d.Colorways.ToDictionary(c => c.ColorwayId, c => d.BomLines.Count(b => b.Colorways.Any(x => x.ColorwayId == c.ColorwayId)));
        // A picture column only when some colorway has a picture (otherwise rows of empty frames).
        var withPictures = d.Colorways.Any(c => pictures.ContainsKey(c.ImageUrl ?? ""));
        var columns = new List<Column> { new("Colorway", 130), new("Name", 0), new("Status", 90), new("BOM lines", 70, Right: true) };
        if (withPictures) columns.Insert(0, new Column("Picture", 56));
        var table = new Table(l, columns);
        foreach (var c in d.Colorways)
        {
            var cells = new List<Cell>
            {
                new(c.ColorwayCode, Bold: true), new(c.ColorwayName ?? "—"),
                new(c.Status == "DROPPED" ? "Dropped" : "In range", Muted: c.Status == "DROPPED"),
                new(lineCount[c.ColorwayId].ToString(CultureInfo.InvariantCulture)),
            };
            if (withPictures) cells.Insert(0, new Cell(Image: pictures.GetValueOrDefault(c.ImageUrl ?? ""), Picture: true));
            table.Row(cells, minHeight: withPictures ? 48 : 0);
        }
        l.Y += 16;
    }

    private static void Bom(Layout l, StyleDetailDto d)
    {
        l.Heading("Bill of materials", d.BomLines.Count);
        if (d.BomLines.Count == 0)
        {
            l.Note("No BOM lines.");
            return;
        }
        var codes = d.Colorways.ToDictionary(c => c.ColorwayId, c => c.ColorwayCode);
        var table = new Table(l,
        [
            new Column("Part", 34), new Column("Material", 0), new Column("Type", 82), new Column("Supplier", 150),
            new Column("LCO cons.", 62, Right: true), new Column("Brand cons.", 62, Right: true), new Column("Colorways", 110),
        ]);
        foreach (var section in Sections(d.BomLines))
        {
            table.Section($"{section.Name} · {section.Lines.Count}");
            foreach (var b in section.Lines)
            {
                var used = b.Colorways.Select(x => codes.GetValueOrDefault(x.ColorwayId, "?")).ToList();
                var colorways = used.Count == 0 ? "—" : d.Colorways.Count > 1 && used.Count == d.Colorways.Count ? "All colorways" : string.Join(", ", used);
                table.Row(
                [
                    new Cell(b.PartNo?.ToString(CultureInfo.InvariantCulture) ?? "—", Muted: true),
                    new Cell(b.MaterialCode, Bold: true, Sub: b.MaterialDescription),
                    new Cell(b.MaterialTypeName ?? b.MaterialTypeCode ?? "—"),
                    new Cell(b.NominatedSupplierName ?? b.NominatedSupplierCode ?? "—", Sub: (b.SupplierName ?? b.SupplierCode) is { } sap ? $"SAP: {sap}" : null),
                    new Cell(Amount(b.LcoConsumption, b.UomCode) + (b.LcoIsEstimate ? " (est.)" : "")),
                    new Cell(Amount(b.BrandConsumption, b.UomCode)),
                    new Cell(colorways, Muted: used.Count == 0),
                ]);
            }
        }
        l.Y += 16;
    }

    /// <summary>Each BOM line's material colour in each colorway, a few colorways per table.</summary>
    private static void ColourMatrix(Layout l, StyleDetailDto d)
    {
        if (d.Colorways.Count == 0 || d.BomLines.Count == 0) return;
        for (var start = 0; start < d.Colorways.Count; start += MatrixColorways)
        {
            var chunk = d.Colorways.Skip(start).Take(MatrixColorways).ToList();
            var title = d.Colorways.Count > MatrixColorways
                ? $"Material colours by colorway ({start + 1}–{start + chunk.Count} of {d.Colorways.Count})"
                : "Material colours by colorway";
            l.Heading(title, null, newPage: start == 0);
            var columns = new List<Column> { new("Part", 34), new("Material", 210) };
            var colourWidth = (l.Right - l.Left - 34 - 210) / MatrixColorways;
            columns.AddRange(chunk.Select(c => new Column(c.ColorwayCode + (string.IsNullOrWhiteSpace(c.ColorwayName) ? "" : $" · {c.ColorwayName}"), colourWidth)));
            var table = new Table(l, columns);
            foreach (var section in Sections(d.BomLines))
            {
                table.Section(section.Name);
                foreach (var b in section.Lines)
                {
                    var cells = new List<Cell>
                    {
                        new(b.PartNo?.ToString(CultureInfo.InvariantCulture) ?? "—", Muted: true),
                        // One line of description here; the BOM table above has it in full.
                        new(b.MaterialCode, Bold: true, Sub: b.MaterialDescription, SubOneLine: true),
                    };
                    foreach (var c in chunk)
                    {
                        var colour = b.Colorways.FirstOrDefault(x => x.ColorwayId == c.ColorwayId);
                        cells.Add(colour is null
                            ? new Cell("not used", Muted: true)
                            : new Cell(colour.MaterialColorCode ?? colour.MaterialColorDescription ?? "—",
                                Sub: colour.MaterialColorCode is null ? null : colour.MaterialColorDescription));
                    }
                    table.Row(cells);
                }
            }
            l.Y += 16;
        }
    }

    private static List<(string Name, List<BomLineDto> Lines)> Sections(IReadOnlyList<BomLineDto> lines)
        // Lines arrive in section order (usp_Style_Get); keep it.
        => lines.GroupBy(b => b.ContentClassName ?? "Unclassified").Select(g => (g.Key, g.ToList())).ToList();

    private static string Amount(decimal? value, string? uom)
        => value is { } v ? $"{v.ToString("0.####", CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(uom) ? "" : " " + uom)}" : "—";

    // ----- Layout -----

    private sealed record Column(string Title, float Width, bool Right = false);

    /// <summary>A table cell: text (bold or muted) with an optional smaller second text (wrapped, or cut to one line), or a picture.</summary>
    private sealed record Cell(string? Text = null, bool Bold = false, bool Muted = false, string? Sub = null, bool SubOneLine = false, SKImage? Image = null,
        bool Picture = false);

    /// <summary>Rows with wrapped text; the column titles repeat at the top of every page the table runs onto.</summary>
    private sealed class Table
    {
        private const float Pad = 5, Line = 11.5f, SubLine = 10.5f, Size = 9, SubSize = 8;
        private readonly Layout l;
        private readonly List<Column> columns;
        private readonly float[] widths;
        private int headerPage = -1;

        public Table(Layout layout, IReadOnlyList<Column> cols)
        {
            l = layout;
            columns = cols.ToList();
            // Width 0 = take what is left.
            var fixedWidth = columns.Sum(c => c.Width);
            var flex = columns.Count(c => c.Width == 0);
            widths = columns.Select(c => c.Width == 0 ? (l.Right - l.Left - fixedWidth) / Math.Max(1, flex) : c.Width).ToArray();
        }

        private void HeaderIfNeeded(float nextRowHeight)
        {
            if (l.Y + nextRowHeight > l.Bottom) l.NewPage();
            if (headerPage == l.Page) return;
            if (l.Y + 22 + nextRowHeight > l.Bottom) l.NewPage();
            var y = l.Y;
            l.Fill(l.Left, y, l.Right - l.Left, 20, Layout.HeaderFill);
            var x = l.Left;
            for (var i = 0; i < columns.Count; i++)
            {
                var title = l.Fit(columns[i].Title, l.Fonts.Bold, 8, widths[i] - 2 * Pad);
                if (columns[i].Right) l.TextRight(title, l.Fonts.Bold, 8, Layout.Muted, x + widths[i] - Pad, y + 13.5f);
                else l.Text(title, l.Fonts.Bold, 8, Layout.Muted, x + Pad, y + 13.5f);
                x += widths[i];
            }
            l.Y += 20;
            headerPage = l.Page;
        }

        public void Section(string title)
        {
            HeaderIfNeeded(18 + 30);
            l.Fill(l.Left, l.Y, l.Right - l.Left, 18, Layout.SectionFill);
            l.Text(title, l.Fonts.Bold, 9, Layout.Ink, l.Left + Pad, l.Y + 12.5f);
            l.Y += 18;
        }

        public void Row(IReadOnlyList<Cell> cells, float minHeight = 0)
        {
            var wrapped = cells.Select((c, i) => (
                Main: c.Text is null ? [] : l.Wrap(c.Text, c.Bold ? l.Fonts.Bold : l.Fonts.Regular, Size, widths[i] - 2 * Pad),
                Sub: c.Sub is null ? []
                    : c.SubOneLine ? [l.Fit(c.Sub, l.Fonts.Regular, SubSize, widths[i] - 2 * Pad)]
                    : l.Wrap(c.Sub, l.Fonts.Regular, SubSize, widths[i] - 2 * Pad))).ToList();
            var height = Math.Max(minHeight, wrapped.Max(w => w.Main.Count * Line + w.Sub.Count * SubLine) + 2 * Pad);
            HeaderIfNeeded(height);
            var y = l.Y;
            var x = l.Left;
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.Picture) l.Picture(c.Image, null, x + Pad, y + 4, height - 8, height - 8);
                var ty = y + Pad + 8.5f;
                foreach (var text in wrapped[i].Main)
                {
                    var font = c.Bold ? l.Fonts.Bold : l.Fonts.Regular;
                    var colour = c.Muted ? Layout.Muted : Layout.Ink;
                    if (columns[i].Right) l.TextRight(text, font, Size, colour, x + widths[i] - Pad, ty);
                    else l.Text(text, font, Size, colour, x + Pad, ty);
                    ty += Line;
                }
                foreach (var text in wrapped[i].Sub)
                {
                    l.Text(text, l.Fonts.Regular, SubSize, Layout.Muted, x + Pad, ty - 1);
                    ty += SubLine;
                }
                x += widths[i];
            }
            l.Y += height;
            l.Line(l.Left, l.Y, l.Right, l.Y, Layout.Border);
        }
    }

    /// <summary>
    /// Regular and bold faces, with a fallback face for text the main face cannot show (e.g. Chinese). Skia embeds a
    /// font whole (no subsetting): fine for Segoe UI (about 1 MB for both), but a CJK face is 8-20 MB, so text in a
    /// fallback face is drawn as outlines instead (looks the same, not selectable).
    /// </summary>
    private sealed class Fonts : IDisposable
    {
        public SKTypeface Regular { get; } = Match(SKFontStyle.Normal);
        public SKTypeface Bold { get; } = Match(SKFontStyle.Bold);
        private readonly Dictionary<(SKTypeface, float), SKFont> cache = [];

        private static SKTypeface Match(SKFontStyle style)
            => SKFontManager.Default.MatchFamily("Segoe UI", style) ?? SKFontManager.Default.MatchFamily("Arial", style)
               ?? SKFontManager.Default.MatchFamily(null, style) ?? SKTypeface.Default;

        /// <summary>The font for the text: the face asked for, or one that has the first character it lacks.</summary>
        public SKFont For(SKTypeface face, float size, string text)
        {
            var font = Get(face, size);
            if (font.ContainsGlyphs(text)) return font;
            foreach (var rune in text.EnumerateRunes())
            {
                if (font.ContainsGlyph(rune.Value)) continue;
                return SKFontManager.Default.MatchCharacter(rune.Value) is { } other ? Get(other, size) : font;
            }
            return font;
        }

        public void Draw(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint)
        {
            if (font.Typeface == Regular || font.Typeface == Bold)
            {
                canvas.DrawText(text, x, baseline, align, font, paint);
                return;
            }
            var width = font.MeasureText(text);
            var left = align switch { SKTextAlign.Center => x - width / 2, SKTextAlign.Right => x - width, _ => x };
            using var path = font.GetTextPath(text, new SKPoint(left, baseline));
            canvas.DrawPath(path, paint);
        }

        private SKFont Get(SKTypeface face, float size)
        {
            if (!cache.TryGetValue((face, size), out var font)) cache[(face, size)] = font = new SKFont(face, size) { Subpixel = true };
            return font;
        }

        public void Dispose()
        {
            foreach (var f in cache.Values) f.Dispose();
        }
    }

    /// <summary>Pages as lists of drawing steps, a cursor (Y) moving down, and helpers that add steps to the current page.</summary>
    private sealed class Layout(Fonts fonts)
    {
        public const float PageWidth = 842, PageHeight = 595, Margin = 32, FooterHeight = 22;
        public static readonly SKColor Ink = new(0x11, 0x18, 0x27), Muted = new(0x6B, 0x72, 0x80), Accent = new(0x1E, 0x40, 0xAF),
            Border = new(0xE5, 0xE7, 0xEB), HeaderFill = new(0xF3, 0xF4, 0xF6), SectionFill = new(0xEE, 0xF2, 0xFF), Frame = new(0xD1, 0xD5, 0xDB);

        private readonly List<List<Action<SKCanvas>>> pages = [[]];
        public Fonts Fonts { get; } = fonts;
        public float Y { get; set; } = Margin;
        public float Left => Margin;
        public float Right => PageWidth - Margin;
        public float Bottom => PageHeight - Margin - FooterHeight;
        public int Page => pages.Count - 1;
        private List<Action<SKCanvas>> Steps => pages[^1];

        public void NewPage()
        {
            pages.Add([]);
            Y = Margin;
        }

        public void Heading(string title, int? count, bool newPage = false)
        {
            if (newPage || Y + 70 > Bottom) NewPage();
            Text(title, Fonts.Bold, 13, Ink, Left, Y + 13);
            if (count is { } n) Text($"{n}", Fonts.Regular, 11, Muted, Left + Measure(title, Fonts.Bold, 13) + 8, Y + 13);
            Y += 22;
        }

        public void Note(string text)
        {
            Text(text, Fonts.Regular, 10, Muted, Left, Y + 10);
            Y += 26;
        }

        public void Text(string text, SKTypeface face, float size, SKColor colour, float x, float baseline)
        {
            var font = Fonts.For(face, size, text);
            Steps.Add(c =>
            {
                using var paint = new SKPaint { Color = colour, IsAntialias = true };
                Fonts.Draw(c, text, x, baseline, SKTextAlign.Left, font, paint);
            });
        }

        public void TextRight(string text, SKTypeface face, float size, SKColor colour, float right, float baseline)
            => Text(text, face, size, colour, right - Measure(text, face, size), baseline);

        public void Fill(float x, float y, float w, float h, SKColor colour)
            => Steps.Add(c =>
            {
                using var paint = new SKPaint { Color = colour, Style = SKPaintStyle.Fill };
                c.DrawRect(x, y, w, h, paint);
            });

        public void Line(float x1, float y1, float x2, float y2, SKColor colour, float width = 0.6f)
            => Steps.Add(c =>
            {
                using var paint = new SKPaint { Color = colour, StrokeWidth = width, IsAntialias = true };
                c.DrawLine(x1, y1, x2, y2, paint);
            });

        public void Rule(SKColor colour, float width) => Line(Left, Y, Right, Y, colour, width);

        /// <summary>A picture fitted (not cropped) into a framed box; an empty box with the label when there is none.</summary>
        public void Picture(SKImage? image, string? label, float x, float y, float w, float h)
        {
            Steps.Add(c =>
            {
                using var frame = new SKPaint { Color = Frame, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, IsAntialias = true };
                c.DrawRoundRect(new SKRect(x, y, x + w, y + h), 4, 4, frame);
                if (image is null) return;
                var scale = Math.Min((w - 6) / image.Width, (h - 6) / image.Height);
                var (iw, ih) = (image.Width * scale, image.Height * scale);
                var dest = SKRect.Create(x + (w - iw) / 2, y + (h - ih) / 2, iw, ih);
                c.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell));
            });
            if (label is not null)
            {
                if (image is null) Text($"No {label.ToLowerInvariant()}", Fonts.Regular, 9, Muted, x + w / 2 - Measure($"No {label.ToLowerInvariant()}", Fonts.Regular, 9) / 2, y + h / 2 + 3);
                Text(label, Fonts.Regular, 8, Muted, x, y + h + 11);
            }
        }

        public float Measure(string text, SKTypeface face, float size) => Fonts.For(face, size, text).MeasureText(text);

        /// <summary>The text cut to fit the width, ending in "…" when cut.</summary>
        public string Fit(string text, SKTypeface face, float size, float width)
        {
            if (Measure(text, face, size) <= width) return text;
            var cut = text;
            while (cut.Length > 1 && Measure(cut + "…", face, size) > width) cut = cut[..^1];
            return cut.TrimEnd() + "…";
        }

        /// <summary>Word-wrapped lines (long words broken where they must be).</summary>
        public List<string> Wrap(string text, SKTypeface face, float size, float width)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", "").Split('\n'))
            {
                var current = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (Measure(candidate, face, size) <= width)
                    {
                        current = candidate;
                        continue;
                    }
                    if (current.Length > 0) lines.Add(current);
                    current = word;
                    while (Measure(current, face, size) > width && current.Length > 1)
                    {
                        var fit = current.Length - 1;
                        while (fit > 1 && Measure(current[..fit], face, size) > width) fit--;
                        lines.Add(current[..fit]);
                        current = current[fit..];
                    }
                }
                if (current.Length > 0 || lines.Count == 0) lines.Add(current);
            }
            return lines;
        }

        public byte[] Render(string footerLeft, string footerCentre, SKDocumentPdfMetadata metadata)
        {
            using var output = new MemoryStream();
            using (var document = SKDocument.CreatePdf(output, metadata))
            {
                for (var i = 0; i < pages.Count; i++)
                {
                    var canvas = document.BeginPage(PageWidth, PageHeight);
                    foreach (var step in pages[i]) step(canvas);
                    Footer(canvas, footerLeft, footerCentre, $"Page {i + 1} of {pages.Count}");
                    document.EndPage();
                }
                document.Close();
            }
            return output.ToArray();
        }

        private void Footer(SKCanvas canvas, string left, string centre, string right)
        {
            var y = PageHeight - Margin + 4;
            using var line = new SKPaint { Color = Border, StrokeWidth = 0.6f };
            canvas.DrawLine(Left, y - 12, Right, y - 12, line);
            using var paint = new SKPaint { Color = Muted, IsAntialias = true };
            foreach (var (text, x, align) in new[] { (left, Left, SKTextAlign.Left), (centre, PageWidth / 2, SKTextAlign.Center), (right, Right, SKTextAlign.Right) })
                Fonts.Draw(canvas, text, x, y, align, Fonts.For(Fonts.Regular, 7.5f, text), paint);
        }
    }
}
