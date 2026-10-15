using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>Garment views of one colour: their boxes (pixels of the picture), the box to use for the colorway (the widest view) and the colour.</summary>
public sealed record ColourGroup(SKColor Colour, IReadOnlyList<SKRectI> Views, SKRectI Box);

/// <summary>A colorway matched to a colour group: colour difference (CIE76) and whether the match is clear.</summary>
public sealed record ColourMatch(int Group, double Distance, bool Sure);

/// <summary>
/// Splits a style sketch that shows several colourways into colour groups, and matches colorways to them by colour.
///   1. garment = everything not reachable from the picture's edge over white (so white garments inside an outline
///      count); thin call-out lines and text are removed;
///   2. each column's main colour (outlines left out); the picture is cut left to right where that colour changes
///      for several columns, or where there is a gap; this separates colourways drawn side by side or fanned over
///      each other;
///   3. views of nearly the same colour are one group (front and back of a colourway).
/// Prints and stripes (zebra, multi-stripe) change colour inside a garment: those give extra groups or wrong colours;
/// the screen shows every match for checking.
/// </summary>
public static class ColourViews
{
    private const int WorkSize = 600;
    /// <summary>Column colours this far apart (CIE76) start a new view.</summary>
    private const double CutDistance = 12;
    /// <summary>Views closer than this are the same colourway.</summary>
    private const double SameColour = 10;
    /// <summary>A match is clear when this close, and the next best is at least <see cref="ClearMargin"/> further.</summary>
    private const double SureDistance = 25;
    private const double ClearMargin = 12;
    /// <summary>Farther than this is no match at all (a burgundy colorway is not the white drawing).</summary>
    private const double MaxDistance = 45;

    public static IReadOnlyList<ColourGroup> Find(SKBitmap picture)
    {
        var scale = Math.Min(1.0, (double)WorkSize / Math.Max(picture.Width, picture.Height));
        int w = Math.Max(1, (int)(picture.Width * scale)), h = Math.Max(1, (int)(picture.Height * scale));
        if (w < 20 || h < 20) return [];
        using var small = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(small))
        using (var image = SKImage.FromBitmap(picture))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Linear));
        }
        var px = small.GetPixelSpan().ToArray();

        // 1. Garment mask.
        var white = new bool[w * h];
        for (var i = 0; i < w * h; i++)
        {
            int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            int lo = Math.Min(r, Math.Min(g, b)), hi = Math.Max(r, Math.Max(g, b));
            white[i] = lo >= 215 && hi - lo < 30;
        }
        var background = FloodFromEdges(white, w, h);
        var garment = new bool[w * h];
        for (var i = 0; i < garment.Length; i++) garment[i] = !background[i];
        var opened = Dilate(Erode(garment, w, h, 2), w, h, 2);
        var keep = BigParts(opened, w, h);
        var inner = Erode(keep, w, h, 2);

        // 2. Column colours and cuts.
        var colLab = new (double L, double A, double B)?[w];
        var coverage = new int[w];
        for (var x = 0; x < w; x++)
        {
            var bins = new Dictionary<int, (long R, long G, long B, int N)>();
            for (var y = 0; y < h; y++)
            {
                var i = y * w + x;
                if (keep[i]) coverage[x]++;
                if (!inner[i]) continue;
                int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
                var key = (r >> 5) << 6 | (g >> 5) << 3 | (b >> 5);
                bins.TryGetValue(key, out var acc);
                bins[key] = (acc.R + r, acc.G + g, acc.B + b, acc.N + 1);
            }
            if (bins.Count == 0) continue;
            var top = bins.Values.MaxBy(v => v.N);
            if (top.N >= 3) colLab[x] = Lab(new SKColor((byte)(top.R / top.N), (byte)(top.G / top.N), (byte)(top.B / top.N)));
        }
        var maxCoverage = coverage.Max();
        if (maxCoverage == 0) return [];
        var minWidth = Math.Max(6, (int)(coverage.Count(c => c > 0) * 0.04));

        var segments = new List<(int X0, int X1)>();
        int? start = null;
        (double L, double A, double B) mean = default;
        var count = 0;
        var off = 0;
        for (var x = 0; x <= w; x++)
        {
            var gap = x == w || coverage[x] < maxCoverage * 0.1 || colLab[x] is null;
            if (gap)
            {
                if (start is { } s0 && x - s0 >= minWidth) segments.Add((s0, x - 1));
                start = null;
                continue;
            }
            var c = colLab[x]!.Value;
            if (start is null) { start = x; mean = c; count = 1; off = 0; continue; }
            if (Distance(c, mean) > CutDistance)
            {
                // Cut only when the change holds for a few columns (a seam or logo is not a new view).
                if (++off >= 5)
                {
                    var cut = x - off + 1;
                    if (cut - start.Value >= minWidth) segments.Add((start.Value, cut - 1));
                    start = cut; mean = colLab[cut]!.Value; count = 1; off = 0;
                    for (var k = cut + 1; k <= x; k++) if (colLab[k] is { } ck) { mean = Mix(mean, count, ck); count++; }
                }
                continue;
            }
            off = 0;
            mean = Mix(mean, count, c);
            count++;
        }

        // 3. Views and colour groups.
        var views = new List<(SKRectI Box, (double L, double A, double B) Lab, int Area)>();
        foreach (var (x0, x1) in segments)
        {
            int y0 = h, y1 = -1, area = 0;
            var bins = new Dictionary<int, (long R, long G, long B, int N)>();
            for (var x = x0; x <= x1; x++)
            for (var y = 0; y < h; y++)
            {
                var i = y * w + x;
                if (!keep[i]) continue;
                area++;
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                if (!inner[i]) continue;
                int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
                var key = (r >> 5) << 6 | (g >> 5) << 3 | (b >> 5);
                bins.TryGetValue(key, out var acc);
                bins[key] = (acc.R + r, acc.G + g, acc.B + b, acc.N + 1);
            }
            if (y1 < 0 || bins.Count == 0) continue;
            var top = bins.Values.MaxBy(v => v.N);
            var box = new SKRectI((int)(x0 / scale), (int)(y0 / scale), Math.Min(picture.Width, (int)Math.Ceiling((x1 + 1) / scale)),
                Math.Min(picture.Height, (int)Math.Ceiling((y1 + 1) / scale)));
            views.Add((box, Lab(new SKColor((byte)(top.R / top.N), (byte)(top.G / top.N), (byte)(top.B / top.N))), area));
        }

        // Colourways drawn fanned over each other show only a strip each: the colorway box is widened (around the strip)
        // to the widest single view, at most as wide as the garment is high.
        var widest = views.Count == 0 ? 0 : views.Max(v => Math.Min(v.Box.Width, v.Box.Height));
        var groups = new List<List<int>>();
        for (var v = 0; v < views.Count; v++)
        {
            var home = groups.FirstOrDefault(g => Distance(views[g[0]].Lab, views[v].Lab) < SameColour);
            if (home is null) groups.Add([v]); else home.Add(v);
        }
        return groups.Select(g =>
        {
            double area = g.Sum(v => views[v].Area);
            var lab = (g.Sum(v => views[v].Lab.L * views[v].Area) / area, g.Sum(v => views[v].Lab.A * views[v].Area) / area,
                g.Sum(v => views[v].Lab.B * views[v].Area) / area);
            var boxes = g.Select(v => views[v].Box).ToList();
            return new ColourGroup(Rgb(lab), boxes, Widen(boxes.MaxBy(b => b.Width), widest, picture.Width));
        }).ToList();
    }

    private static SKRectI Widen(SKRectI box, int width, int pictureWidth)
    {
        if (box.Width >= width) return box;
        var left = Math.Clamp(box.MidX - width / 2, 0, Math.Max(0, pictureWidth - width));
        return new SKRectI(left, box.Top, Math.Min(pictureWidth, left + width), box.Bottom);
    }

    /// <summary>
    /// One match per colorway (null = no colour in its name, or every group taken). Closest pairs first, each group
    /// used once, so two colorways never get the same picture.
    /// </summary>
    public static ColourMatch?[] Match(IReadOnlyList<ColourGroup> groups, IReadOnlyList<NamedColour?> colorways)
    {
        var result = new ColourMatch?[colorways.Count];
        var d = new double[colorways.Count, groups.Count];
        for (var c = 0; c < colorways.Count; c++)
        for (var g = 0; g < groups.Count; g++)
            d[c, g] = colorways[c] is { } n ? HueFirst(Lab(n.Rgb), Lab(groups[g].Colour)) : double.MaxValue;

        var usedGroup = new bool[groups.Count];
        for (var round = 0; round < Math.Min(colorways.Count, groups.Count); round++)
        {
            (int C, int G, double D) best = (-1, -1, double.MaxValue);
            for (var c = 0; c < colorways.Count; c++)
            {
                if (result[c] is not null || colorways[c] is null) continue;
                for (var g = 0; g < groups.Count; g++)
                    if (!usedGroup[g] && d[c, g] < best.D) best = (c, g, d[c, g]);
            }
            if (best.C < 0 || best.D > MaxDistance) break;
            // Clear: close, and both the next group for this colorway and the next named colorway for this group are well behind.
            double nextGroup = double.MaxValue, nextColorway = double.MaxValue;
            for (var g = 0; g < groups.Count; g++) if (g != best.G) nextGroup = Math.Min(nextGroup, d[best.C, g]);
            for (var c = 0; c < colorways.Count; c++) if (c != best.C && colorways[c] is not null) nextColorway = Math.Min(nextColorway, d[c, best.G]);
            var sure = best.D <= SureDistance && nextGroup - best.D >= ClearMargin && nextColorway - best.D >= ClearMargin;
            result[best.C] = new ColourMatch(best.G, best.D, sure);
            usedGroup[best.G] = true;
        }
        return result;
    }

    // ----- colour maths (sRGB D65 -> CIE Lab) -----

    public static (double L, double A, double B) Lab(SKColor c)
    {
        static double Lin(byte v) { var s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        double r = Lin(c.Red), g = Lin(c.Green), b = Lin(c.Blue);
        double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, y = 0.2126 * r + 0.7152 * g + 0.0722 * b, z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    private static SKColor Rgb((double L, double A, double B) lab)
    {
        var fy = (lab.L + 16) / 116; var fx = fy + lab.A / 500; var fz = fy - lab.B / 200;
        static double Inv(double t) => t * t * t > 0.008856 ? t * t * t : (t - 16.0 / 116) / 7.787;
        double x = Inv(fx) * 0.95047, y = Inv(fy), z = Inv(fz) * 1.08883;
        double r = 3.2406 * x - 1.5372 * y - 0.4986 * z, g = -0.9689 * x + 1.8758 * y + 0.0415 * z, b = 0.0557 * x - 0.2040 * y + 1.0570 * z;
        static byte Gam(double v) { v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(Math.Max(v, 0), 1 / 2.4) - 0.055; return (byte)Math.Clamp(Math.Round(v * 255), 0, 255); }
        return new SKColor(Gam(r), Gam(g), Gam(b));
    }

    /// <summary>Colour difference with lightness counted less: shading in a sketch changes lightness more than hue.</summary>
    private static double HueFirst((double L, double A, double B) a, (double L, double A, double B) b)
        => Math.Sqrt(0.5 * (a.L - b.L) * (a.L - b.L) + (a.A - b.A) * (a.A - b.A) + (a.B - b.B) * (a.B - b.B));

    public static double Distance((double L, double A, double B) a, (double L, double A, double B) b)
        => Math.Sqrt((a.L - b.L) * (a.L - b.L) + (a.A - b.A) * (a.A - b.A) + (a.B - b.B) * (a.B - b.B));

    private static (double, double, double) Mix((double L, double A, double B) mean, int n, (double L, double A, double B) c)
        => ((mean.L * n + c.L) / (n + 1), (mean.A * n + c.A) / (n + 1), (mean.B * n + c.B) / (n + 1));

    // ----- masks -----

    private static bool[] FloodFromEdges(bool[] open, int w, int h)
    {
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        void Seed(int i) { if (open[i] && !seen[i]) { seen[i] = true; stack.Push(i); } }
        for (var x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (var y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            int x = p % w, y = p / w;
            if (x > 0) Seed(p - 1);
            if (x < w - 1) Seed(p + 1);
            if (y > 0) Seed(p - w);
            if (y < h - 1) Seed(p + w);
        }
        return seen;
    }

    /// <summary>Parts at least 5% of the largest one's area and a quarter of its height (drops text and swatches).</summary>
    private static bool[] BigParts(bool[] mask, int w, int h)
    {
        var label = new int[w * h];
        var parts = new List<(int Area, int Height, List<int> Pixels)>();
        var stack = new Stack<int>();
        for (var s = 0; s < w * h; s++)
        {
            if (!mask[s] || label[s] != 0) continue;
            var pixels = new List<int>();
            int y0 = h, y1 = 0;
            label[s] = parts.Count + 1;
            stack.Push(s);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                pixels.Add(p);
                int x = p % w, y = p / w;
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                if (x > 0 && mask[p - 1] && label[p - 1] == 0) { label[p - 1] = label[s]; stack.Push(p - 1); }
                if (x < w - 1 && mask[p + 1] && label[p + 1] == 0) { label[p + 1] = label[s]; stack.Push(p + 1); }
                if (y > 0 && mask[p - w] && label[p - w] == 0) { label[p - w] = label[s]; stack.Push(p - w); }
                if (y < h - 1 && mask[p + w] && label[p + w] == 0) { label[p + w] = label[s]; stack.Push(p + w); }
            }
            parts.Add((pixels.Count, y1 - y0 + 1, pixels));
        }
        var result = new bool[w * h];
        if (parts.Count == 0) return result;
        int maxArea = parts.Max(p => p.Area), maxHeight = parts.Max(p => p.Height);
        foreach (var p in parts.Where(p => p.Area >= maxArea * 0.05 && p.Height >= maxHeight * 0.25))
            foreach (var i in p.Pixels) result[i] = true;
        return result;
    }

    private static bool[] Erode(bool[] m, int w, int h, int r)
    {
        var o = new bool[m.Length];
        for (var y = r; y < h - r; y++)
        for (var x = r; x < w - r; x++)
        {
            if (!m[y * w + x]) continue;
            var all = true;
            for (var dy = -r; dy <= r && all; dy++)
            for (var dx = -r; dx <= r && all; dx++)
                all = m[(y + dy) * w + x + dx];
            o[y * w + x] = all;
        }
        return o;
    }

    private static bool[] Dilate(bool[] m, int w, int h, int r)
    {
        var o = new bool[m.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (!m[y * w + x]) continue;
            for (var dy = Math.Max(-r, -y); dy <= r && y + dy < h; dy++)
            for (var dx = Math.Max(-r, -x); dx <= r && x + dx < w; dx++)
                o[(y + dy) * w + x + dx] = true;
        }
        return o;
    }
}
