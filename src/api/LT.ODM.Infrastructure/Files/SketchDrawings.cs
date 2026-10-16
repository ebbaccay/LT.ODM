using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>
/// The garment drawings on a sketch: the picture shrunk to 160 px wide, anything darker than near-white is ink, and
/// connected ink areas at least 35% the size of the largest are drawings (pins, labels, arrows and scan lines at the
/// edge are much smaller). Front and back drawn apart give two, also when offset diagonally; overlapping views give one.
/// </summary>
public static class SketchDrawings
{
    private const int Width = 160;

    /// <summary>Each drawing's box, in the bitmap's own pixels (largest first).</summary>
    public static IReadOnlyList<SKRectI> Find(SKBitmap source)
    {
        if (source.Width == 0 || source.Height == 0) return [];
        var height = Math.Max(1, source.Height * Width / source.Width);
        using var small = source.Resize(new SKImageInfo(Width, height), new SKSamplingOptions(SKFilterMode.Linear));
        if (small is null) return [];

        var ink = new bool[Width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < Width; x++)
            {
                var c = small.GetPixel(x, y);
                ink[y * Width + x] = c.Alpha > 128 && (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000 < 235;
            }

        var seen = new bool[ink.Length];
        var areas = new List<(int Size, int Left, int Top, int Right, int Bottom)>();
        var stack = new Stack<int>();
        for (var start = 0; start < ink.Length; start++)
        {
            if (!ink[start] || seen[start]) continue;
            int size = 0, left = Width, top = height, right = 0, bottom = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                size++;
                int px = p % Width, py = p / Width;
                left = Math.Min(left, px); right = Math.Max(right, px);
                top = Math.Min(top, py); bottom = Math.Max(bottom, py);
                foreach (var q in (int[])[px > 0 ? p - 1 : -1, px < Width - 1 ? p + 1 : -1, py > 0 ? p - Width : -1, py < height - 1 ? p + Width : -1])
                    if (q >= 0 && ink[q] && !seen[q]) { seen[q] = true; stack.Push(q); }
            }
            areas.Add((size, left, top, right, bottom));
        }
        if (areas.Count == 0) return [];

        var largest = areas.Max(a => a.Size);
        var fx = (double)source.Width / Width;
        var fy = (double)source.Height / height;
        return areas.Where(a => a.Size >= largest * 0.35).OrderByDescending(a => a.Size)
            .Select(a => new SKRectI((int)Math.Floor(a.Left * fx), (int)Math.Floor(a.Top * fy),
                Math.Min(source.Width, (int)Math.Ceiling((a.Right + 1) * fx)), Math.Min(source.Height, (int)Math.Ceiling((a.Bottom + 1) * fy))))
            .ToList();
    }

    /// <summary>All drawings together with a small border, or the whole picture when none is found.</summary>
    public static SKRectI Bounds(SKBitmap source)
    {
        var drawings = Find(source);
        if (drawings.Count == 0) return new SKRectI(0, 0, source.Width, source.Height);
        int left = drawings.Min(d => d.Left), top = drawings.Min(d => d.Top), right = drawings.Max(d => d.Right), bottom = drawings.Max(d => d.Bottom);
        var pad = Math.Max(2, Math.Max(right - left, bottom - top) / 30);
        return new SKRectI(Math.Max(0, left - pad), Math.Max(0, top - pad), Math.Min(source.Width, right + pad), Math.Min(source.Height, bottom + pad));
    }
}
