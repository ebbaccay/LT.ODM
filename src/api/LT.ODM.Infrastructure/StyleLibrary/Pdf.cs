using SkiaSharp;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// PDF drawing helpers shared by the tech pack and the style package line sheet (SkiaSharp): pages laid out as drawing
/// steps and drawn after (for "page x of y"), fonts with a fallback for Chinese, wrapped-text tables and framed pictures.
/// </summary>
internal static class Pdf
{
    private const string ImagePrefix = "/api/v1/styles/images/";

    /// <summary>The stored file name of a style image address (/api/v1/styles/images/...), or null for any other address (skipped).</summary>
    internal static string? StyleImageFile(string? url)
    {
        if (url is null || !url.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var name = url[ImagePrefix.Length..];
        var q = name.IndexOf('?');
        return q >= 0 ? name[..q] : name;
    }

    internal sealed record Column(string Title, float Width, bool Right = false);

    /// <summary>A table cell: text (bold or muted) with an optional smaller second text (wrapped, or cut to one line), or a picture.</summary>
    internal sealed record Cell(string? Text = null, bool Bold = false, bool Muted = false, string? Sub = null, bool SubOneLine = false, SKImage? Image = null,
        bool Picture = false);

    /// <summary>Rows with wrapped text; the column titles repeat at the top of every page the table runs onto.</summary>
    internal sealed class Table
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
    internal sealed class Fonts : IDisposable
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
    internal sealed class Layout(Fonts fonts)
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
