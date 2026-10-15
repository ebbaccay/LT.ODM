using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>Pixel measures of a garment picture (at most 400 px, on white) that tell sketches from photos.</summary>
/// <param name="Outline">Share of pixels that are very dark (luminance &lt; 70) with near-white within 2 px: the black outlines of a flat sketch.</param>
/// <param name="White">Share of near-white pixels (luminance &gt; 235): sketches sit on white; studio photos often on grey.</param>
public readonly record struct PictureKindFeatures(double Outline, double White);

/// <summary>
/// Sketch or photo? The look-alike image model compares the picture with words for sketches and for photos; black
/// outlines (sketch) and a background with no white (studio photo) add to that. Tuned on 45 pictures (2026-10: 38
/// sketches and flats from checkpoint sheets and uploads, 7 product and studio photos): all 45 right, with 2 close to
/// the line (marked unsure). Without the model the rules alone decide only clear cases.
/// </summary>
public static class PictureKindRules
{
    public const string Sketch = "Sketch";
    public const string Photo = "Photo";

    public static readonly string[] SketchWords =
        ["a flat technical sketch of a garment", "a fashion design drawing", "a vector illustration of clothing", "a line drawing of clothes"];
    public static readonly string[] PhotoWords =
        ["a photo of a real garment", "a photograph of a person wearing clothes", "a product photo of clothing on a hanger", "a realistic photo of fabric clothing"];

    /// <summary>Weight of the outline share in the combined score.</summary>
    private const double OutlineWeight = 0.3;
    /// <summary>Below this share of white the background is taken as a studio/photo background.</summary>
    private const double NoWhite = 0.1;
    private const double NoWhitePenalty = 0.02;
    /// <summary>Scores closer to 0 than this are reported as unsure (the closest test pictures scored +0.002 and -0.005).</summary>
    public const double SureMargin = 0.008;

    /// <summary>
    /// <paramref name="model"/>: mean similarity to the sketch words minus mean similarity to the photo words (null =
    /// model not used). Returns the kind (null = cannot tell) and whether the decision is clear.
    /// </summary>
    public static (string? Kind, bool Sure) Decide(double? model, PictureKindFeatures f)
    {
        if (model is { } m)
        {
            var score = m + OutlineWeight * f.Outline - (f.White < NoWhite ? NoWhitePenalty : 0);
            return (score >= 0 ? Sketch : Photo, Math.Abs(score) >= SureMargin);
        }
        // Rules only: strong black outlines on white = sketch; no white at all = photo; anything else unknown.
        if (f.Outline >= 0.03 && f.White >= 0.2) return (Sketch, false);
        if (f.White < NoWhite && f.Outline < 0.01) return (Photo, false);
        return (null, false);
    }

    public static PictureKindFeatures Measure(SKBitmap picture)
    {
        var scale = Math.Min(1.0, 400.0 / Math.Max(picture.Width, picture.Height));
        using var small = new SKBitmap(new SKImageInfo(Math.Max(1, (int)(picture.Width * scale)), Math.Max(1, (int)(picture.Height * scale)),
            SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(small))
        using (var image = SKImage.FromBitmap(picture))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, new SKRect(0, 0, small.Width, small.Height), new SKSamplingOptions(SKFilterMode.Linear));
        }
        int w = small.Width, h = small.Height;
        if (w < 5 || h < 5) return new PictureKindFeatures(0, 1);
        var px = small.GetPixelSpan();
        var lum = new int[w * h];
        for (var i = 0; i < lum.Length; i++) lum[i] = (px[i * 4] * 299 + px[i * 4 + 1] * 587 + px[i * 4 + 2] * 114) / 1000;

        int outline = 0, white = 0;
        for (var y = 2; y < h - 2; y++)
        for (var x = 2; x < w - 2; x++)
        {
            var l = lum[y * w + x];
            if (l > 235) { white++; continue; }
            if (l >= 70) continue;
            var near = false;
            for (var dy = -2; dy <= 2 && !near; dy++)
            for (var dx = -2; dx <= 2 && !near; dx++)
                near = lum[(y + dy) * w + x + dx] > 220;
            if (near) outline++;
        }
        double all = (w - 4) * (h - 4);
        return new PictureKindFeatures(outline / all, white / all);
    }
}
