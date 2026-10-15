using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

public readonly record struct CropBox(int X, int Y, int Width, int Height);

public enum CropOutcome { Cropped, Whole, NotFound }

/// <summary>Score: the image model's score of the chosen candidate (null without the model).</summary>
public sealed record CropChoice(CropBox? Box, CropOutcome Outcome, float? Score = null);

/// <summary>What <see cref="GarmentCropFinder.Analyse"/> found: picture-like regions, thickest first, and the table lines.</summary>
public sealed class CropAnalysis(int width, int height, IReadOnlyList<CropBox> candidates, bool[] lines)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public IReadOnlyList<CropBox> Candidates { get; } = candidates;
    internal bool[] Lines { get; } = lines;
}

/// <summary>
/// Finds the garment picture on a page that also holds tables and text (checkpoint sheets, screenshots of a sketch
/// window). Rules, no AI:
///   1. edges = pixels that differ clearly from the pixel to the right or below (flat cell colours are not edges);
///   2. long straight runs of edges are table and window lines: removed;
///   3. what is left is grouped into blobs; text makes short strips or stacked bands and is left out, so the
///      candidates are the thick blobs (a garment, or the views of one);
///   4. <see cref="Choose"/> takes the best candidate (scored by the image model when there is one, else the
///      thickest) and joins the neighbouring views (front and back, colourways) that look like garments too.
/// Tuned on 37 checkpoint sheets and sketch screenshots (2026-10): about 6 in 7 are right without help; the screen
/// shows every box so the rest can be corrected.
/// </summary>
public static class GarmentCropFinder
{
    /// <summary>Colour step (0-255, largest of R, G, B) that counts as an edge.</summary>
    private const int EdgeThreshold = 40;
    /// <summary>Candidates handed to the scorer; more is slower and rarely finds anything new.</summary>
    public const int MaxCandidates = 12;
    /// <summary>Below this the best candidate looks more like text than a garment: nothing is cut, the user draws the box.</summary>
    public const float MinScore = -0.03f;
    /// <summary>
    /// At or above this the model is fairly sure the box is a garment (on the 37 test sheets, boxes around text or a
    /// small part of a garment scored lower; boxes that miss one of several views can still score higher). Below, the
    /// screen asks the admin to check the box.
    /// </summary>
    public const float SureScore = 0.02f;
    /// <summary>Neighbours join the picture when they score at least this and not far below the best.</summary>
    public const float JoinScore = -0.015f;
    public const float JoinMargin = 0.05f;
    /// <summary>A picture covering this share of the page already is the style picture: kept whole.</summary>
    public const double WholeShare = 0.8;

    public static CropAnalysis Analyse(SKBitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        if (w < 32 || h < 32) return new CropAnalysis(w, h, [], new bool[w * h]);
        var px = Rgba(bitmap);

        // 1. Edges.
        var edge = new bool[w * h];
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
        {
            var i = (y * w + x) * 4;
            if (Diff(px, i, i + 4) > EdgeThreshold || Diff(px, i, i + w * 4) > EdgeThreshold) edge[y * w + x] = true;
        }

        // 2. Lines: runs at least this long, across or down.
        var minRun = Math.Max(24, (int)(Math.Min(w, h) * 0.045));
        var line = new bool[w * h];
        for (var y = 0; y < h; y++)
            MarkRuns(edge, line, y * w, 1, w, minRun);
        for (var x = 0; x < w; x++)
            MarkRuns(edge, line, x, w, h, minRun);
        var ink = new bool[w * h];
        for (var i = 0; i < ink.Length; i++) ink[i] = edge[i] && !line[i];

        // 3. Blobs (8-connected after joining pixels one apart), grouped into candidates.
        var blobs = Blobs(Dilate(ink, w, h), ink, w, h);
        var candidates = Groups(blobs, ink, w, h).Select(g => new CropBox(g.X0, g.Y0, g.Width, g.Height)).ToList();
        return new CropAnalysis(w, h, candidates, line);
    }

    /// <summary>
    /// Picks the picture. With <paramref name="scores"/> (one per candidate; higher = more garment-like) the best one
    /// wins and neighbours that also look like garments join it; without, the first (thickest) candidate wins and
    /// level neighbours join it. Adds a small margin that stops short of table lines.
    /// </summary>
    public static CropChoice Choose(CropAnalysis analysis, IReadOnlyList<float>? scores)
    {
        var candidates = analysis.Candidates;
        int width = analysis.Width, height = analysis.Height;
        if (candidates.Count == 0) return new CropChoice(null, CropOutcome.NotFound);
        var best = 0;
        if (scores is not null)
            for (var i = 1; i < candidates.Count; i++) if (scores[i] > scores[best]) best = i;
        if (scores is not null && scores[best] < MinScore) return new CropChoice(null, CropOutcome.NotFound, scores[best]);

        var group = Blob.Of(candidates[best]);
        var used = new bool[candidates.Count];
        used[best] = true;
        var gap = Math.Max(8, (int)(Math.Min(width, height) * 0.02));
        for (var grown = true; grown;)
        {
            grown = false;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (used[i]) continue;
                if (scores is not null && (scores[i] < JoinScore || scores[i] < scores[best] - JoinMargin)) continue;
                var c = Blob.Of(candidates[i]);
                if (!Near(group, c, gap) && !SideBySide(group, c)) continue;
                group = group.Union(c);
                used[i] = grown = true;
            }
        }

        var pad = Math.Max(4, (int)(Math.Max(group.Width, group.Height) * 0.05));
        var box = TrimLines(analysis.Lines, width, height, group,
            Math.Max(0, group.X0 - pad), Math.Max(0, group.Y0 - pad), Math.Min(width - 1, group.X1 + pad), Math.Min(height - 1, group.Y1 + pad));
        float? score = scores is null ? null : scores[best];
        return (double)box.Width * box.Height >= WholeShare * width * height
            ? new CropChoice(null, CropOutcome.Whole, score)
            : new CropChoice(box, CropOutcome.Cropped, score);
    }

    /// <summary>Moves each side of the margin inwards past a table line crossing it, so the picture has no border stuck to it.</summary>
    private static CropBox TrimLines(bool[] lines, int w, int h, Blob inner, int x0, int y0, int x1, int y1)
    {
        bool Row(int y) => CountLine(lines, w, x0, x1, y, y) * 2 >= x1 - x0 + 1;
        bool Col(int x) => CountLine(lines, w, x, x, y0, y1) * 2 >= y1 - y0 + 1;
        for (var y = inner.Y0 - 1; y >= y0; y--) if (Row(y)) { y0 = y + 1; break; }
        for (var y = inner.Y1 + 1; y <= y1; y++) if (Row(y)) { y1 = y - 1; break; }
        for (var x = inner.X0 - 1; x >= x0; x--) if (Col(x)) { x0 = x + 1; break; }
        for (var x = inner.X1 + 1; x <= x1; x++) if (Col(x)) { x1 = x - 1; break; }
        return new CropBox(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    private static int CountLine(bool[] lines, int w, int x0, int x1, int y0, int y1)
    {
        var n = 0;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            if (lines[y * w + x]) n++;
        return n;
    }

    /// <summary>Picture-like groups (text and specks left out), thickest first: a garment is thicker than a line of text.</summary>
    private static IEnumerable<Blob> Groups(List<Blob> blobs, bool[] ink, int w, int h)
    {
        var minHeight = Math.Max(14, (int)(h * 0.03));
        var groups = blobs.Where(b => b.Height >= minHeight && b.Width >= 10 && b.Ink >= 40 && !LooksLikeText(ink, w, b)).ToList();
        var gap = Math.Max(6, (int)(Math.Min(w, h) * 0.012));
        for (var merged = true; merged;)
        {
            merged = false;
            for (var a = 0; a < groups.Count && !merged; a++)
            for (var b = a + 1; b < groups.Count && !merged; b++)
                if (Near(groups[a], groups[b], gap))
                {
                    groups[a] = groups[a].Union(groups[b]);
                    groups.RemoveAt(b);
                    merged = true;
                }
        }
        foreach (var start in groups.OrderByDescending(g => Math.Min(g.Width, g.Height)).Take(MaxCandidates))
        {
            // Small details (buttons, labels, drawstrings) inside or touching the picture belong to it.
            var g = start;
            foreach (var b in blobs)
                if (b.X0 >= g.X0 - gap && b.X1 <= g.X1 + gap && b.Y0 >= g.Y0 - gap && b.Y1 <= g.Y1 + gap && Overlaps(b, g))
                    g = g.Union(b);
            yield return g;
        }
    }

    /// <summary>Views of one style drawn next to each other (front and back, colourways): level, similar height, apart by less than their height.</summary>
    private static bool SideBySide(Blob a, Blob b)
    {
        var overlap = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0) + 1;
        int lo = Math.Min(a.Height, b.Height), hi = Math.Max(a.Height, b.Height);
        var gapX = Math.Max(a.X0, b.X0) - Math.Min(a.X1, b.X1);
        return overlap >= 0.6 * lo && lo >= 0.5 * hi && gapX <= hi;
    }

    /// <summary>
    /// Stacked lines of text form a tall blob too, but its rows of ink come in bands with empty rows between them, all
    /// bands low. One line of large text is long and low. A garment's ink runs through most rows.
    /// </summary>
    private static bool LooksLikeText(bool[] ink, int w, Blob b)
    {
        var bands = new List<int>();
        var run = 0;
        for (var y = b.Y0; y <= b.Y1 + 1; y++)
        {
            var any = false;
            if (y <= b.Y1)
                for (var x = b.X0; x <= b.X1 && !any; x++) any = ink[y * w + x];
            if (any) { run++; continue; }
            if (run > 0) bands.Add(run);
            run = 0;
        }
        if (bands.Count >= 2 && bands.Max() <= 24) return true;
        return bands.Count == 1 && b.Height <= 34 && b.Width >= 4 * b.Height;
    }

    private static int Diff(byte[] px, int a, int b)
        => Math.Max(Math.Abs(px[a] - px[b]), Math.Max(Math.Abs(px[a + 1] - px[b + 1]), Math.Abs(px[a + 2] - px[b + 2])));

    private static void MarkRuns(bool[] edge, bool[] line, int start, int step, int count, int minRun)
    {
        var run = 0;
        for (var k = 0; k <= count; k++)
        {
            if (k < count && edge[start + k * step]) { run++; continue; }
            if (run >= minRun)
                for (var j = k - run; j < k; j++) line[start + j * step] = true;
            run = 0;
        }
    }

    private static bool[] Dilate(bool[] mask, int w, int h)
    {
        var o = new bool[mask.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            if (mask[y * w + x])
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h) o[ny * w + nx] = true;
                }
        return o;
    }

    /// <summary>Connected areas of <paramref name="grown"/>, counting the <paramref name="ink"/> pixels in each.</summary>
    private static List<Blob> Blobs(bool[] grown, bool[] ink, int w, int h)
    {
        var seen = new bool[grown.Length];
        var list = new List<Blob>();
        var stack = new Stack<int>();
        for (var s = 0; s < grown.Length; s++)
        {
            if (!grown[s] || seen[s]) continue;
            int x0 = w, y0 = h, x1 = 0, y1 = 0, count = 0;
            stack.Push(s);
            seen[s] = true;
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                int x = p % w, y = p / w;
                if (ink[p]) count++;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    var n = ny * w + nx;
                    if (grown[n] && !seen[n]) { seen[n] = true; stack.Push(n); }
                }
            }
            if (count > 0) list.Add(new Blob(x0, y0, x1, y1, count));
        }
        return list;
    }

    private static bool Near(Blob a, Blob b, int gap)
        => a.X0 - gap <= b.X1 && b.X0 - gap <= a.X1 && a.Y0 - gap <= b.Y1 && b.Y0 - gap <= a.Y1;

    private static bool Overlaps(Blob a, Blob b) => a.X0 <= b.X1 && b.X0 <= a.X1 && a.Y0 <= b.Y1 && b.Y0 <= a.Y1;

    /// <summary>RGBA bytes with transparent parts on white (sketches on a clear background).</summary>
    private static byte[] Rgba(SKBitmap bitmap)
    {
        using var flat = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(flat))
        using (var image = SKImage.FromBitmap(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        return flat.GetPixelSpan().ToArray();
    }

    private sealed record Blob(int X0, int Y0, int X1, int Y1, int Ink)
    {
        public int Width => X1 - X0 + 1;
        public int Height => Y1 - Y0 + 1;
        public static Blob Of(CropBox c) => new(c.X, c.Y, c.X + c.Width - 1, c.Y + c.Height - 1, 0);
        public Blob Union(Blob o) => new(Math.Min(X0, o.X0), Math.Min(Y0, o.Y0), Math.Max(X1, o.X1), Math.Max(Y1, o.Y1), Ink + o.Ink);
    }
}
