using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>
/// Cuts a front-and-back sketch into its two drawings (<see cref="SketchDrawings"/>) and joins the two renders side by
/// side. Each drawing keeps a small border; the renders are placed on one row at the same height.
/// </summary>
public sealed class SketchViews : ISketchViews
{
    public (ReferenceImage Front, ReferenceImage Back)? Split(ReferenceImage sketch)
    {
        using var picture = Decode(sketch.Content);
        if (picture is null) return null;
        var drawings = SketchDrawings.Find(picture);
        if (drawings.Count != 2) return null;

        // Front first: the left drawing; when one is clearly above the other (stacked), the upper one.
        var ordered = drawings.OrderBy(d => d.MidX).ToList();
        if (Math.Abs(drawings[0].MidX - drawings[1].MidX) < Math.Min(drawings[0].Width, drawings[1].Width) / 4)
            ordered = drawings.OrderBy(d => d.MidY).ToList();
        return (Cut(picture, ordered[0]), Cut(picture, ordered[1]));
    }

    public GeneratedImage Join(GeneratedImage front, GeneratedImage back)
    {
        using var left = SKBitmap.Decode(front.Content) ?? throw new UnreadableImageException();
        using var right = SKBitmap.Decode(back.Content) ?? throw new UnreadableImageException();
        var height = Math.Max(left.Height, right.Height);
        var leftWidth = left.Width * height / left.Height;
        var rightWidth = right.Width * height / right.Height;
        using var surface = SKSurface.Create(new SKImageInfo(leftWidth + rightWidth, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()))
            ?? throw new UnreadableImageException();
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using (var image = SKImage.FromBitmap(left)) canvas.DrawImage(image, SKRect.Create(0, 0, leftWidth, height), sampling);
        using (var image = SKImage.FromBitmap(right)) canvas.DrawImage(image, SKRect.Create(leftWidth, 0, rightWidth, height), sampling);
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100) ?? throw new UnreadableImageException();
        return new GeneratedImage(png.ToArray(), ImageKind.Png);
    }

    private static ReferenceImage Cut(SKBitmap picture, SKRectI box)
    {
        var pad = Math.Max(2, Math.Max(box.Width, box.Height) / 30);
        var rect = new SKRectI(Math.Max(0, box.Left - pad), Math.Max(0, box.Top - pad), Math.Min(picture.Width, box.Right + pad), Math.Min(picture.Height, box.Bottom + pad));
        using var piece = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(piece))
        using (var image = SKImage.FromBitmap(picture))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, rect, SKRect.Create(0, 0, rect.Width, rect.Height), new SKSamplingOptions(SKFilterMode.Nearest));
        }
        using var png = piece.Encode(SKEncodedImageFormat.Png, 100) ?? throw new UnreadableImageException();
        return new ReferenceImage(png.ToArray(), "image/png");
    }

    /// <summary>Upright (EXIF) and full size; null when it cannot be read.</summary>
    private static SKBitmap? Decode(byte[] content)
    {
        if (ConceptStudioRules.DetectImage(content) is not { } kind) return null;
        try
        {
            var (upright, _) = ImageOptimizer.Optimize(content, kind, int.MaxValue);
            return SKBitmap.Decode(upright);
        }
        catch (UnreadableImageException)
        {
            return null;
        }
    }
}
