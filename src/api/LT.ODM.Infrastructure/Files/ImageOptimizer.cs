using LT.ODM.Application.ConceptStudio;
using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>
/// Shrinks images before they are stored, so uploads and AI renders do not fill the server:
///   - turned upright (phone photos record their rotation separately) and converted to sRGB;
///   - scaled down so the longest side is at most the store's limit (never scaled up);
///   - re-encoded without metadata (camera details and GPS position are dropped);
///   - saved as JPEG (photos) or PNG (sketches and graphics with flat colours or transparency), whichever suits it.
/// The original is kept when it is already small enough and re-encoding would not make it smaller.
/// </summary>
public static class ImageOptimizer
{
    /// <summary>JPEG quality: no visible loss on product photos and renders at the stored sizes, about a third the size of quality 95.</summary>
    public const int JpegQuality = 82;

    /// <summary>PNG is kept for images with transparency or flat colours unless it is this much bigger than the JPEG.</summary>
    private const double PngAllowance = 1.3;

    public static (byte[] Content, ImageKind Kind) Optimize(byte[] content, ImageKind kind, int maxDimension)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data) ?? throw new UnreadableImageException();
        var origin = codec.EncodedOrigin;
        var info = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul).WithColorSpace(SKColorSpace.CreateSrgb());
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new UnreadableImageException();

        // Size after turning upright: 90° rotations swap width and height.
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (uprightW, uprightH) = swap ? (info.Height, info.Width) : (info.Width, info.Height);
        var scale = Math.Min(1.0, (double)maxDimension / Math.Max(uprightW, uprightH));
        var width = Math.Max(1, (int)Math.Round(uprightW * scale));
        var height = Math.Max(1, (int)Math.Round(uprightH * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()))
            ?? throw new UnreadableImageException();
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)scale);
        ApplyOrigin(canvas, origin, info.Width, info.Height);
        using (var source = SKImage.FromBitmap(decoded))
            // Mipmaps give clean results when shrinking a lot (a 4000 px photo to 1600 px).
            canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();
        using var image = surface.Snapshot();
        using var pixels = image.PeekPixels();

        var hasAlpha = HasTransparency(pixels);
        var jpeg = EncodeJpeg(image, width, height);
        byte[]? png = null;
        // Photos are never smaller as PNG; only try it for images that may be graphics.
        if (hasAlpha || kind is ImageKind.Png or ImageKind.Gif)
            png = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        var (best, bestKind) = png is not null && png.Length <= jpeg.Length * PngAllowance
            ? (png, ImageKind.Png)
            : (jpeg, ImageKind.Jpeg);

        // Already small and upright: keep the uploaded file if re-encoding does not help.
        var untouched = scale >= 1.0 && origin == SKEncodedOrigin.TopLeft && kind is ImageKind.Jpeg or ImageKind.Png;
        return untouched && content.Length <= best.Length ? (content, kind) : (best, bestKind);
    }

    /// <summary>
    /// The image (turned upright, cut to its garment drawings) scaled to fit width x height with a margin, centred on white, as PNG: the
    /// input of an image-to-image model, which draws at the size it is given. Trimming matters: a wide front-and-back sketch
    /// with its own margins came out small on the square, and the model filled the empty space above and below with
    /// mannequin heads and legs while small details (a back slit) were lost (2026-10-15). Not stretched: that changes the
    /// garment's proportions. Transparent parts become white.
    /// </summary>
    public static byte[] FitOnCanvas(byte[] content, int width, int height) => SketchCanvas(content, width, height, fitShape: false).Png;

    /// <summary>Share of the canvas left white on each side of the sketch.</summary>
    public const float CanvasMargin = 0.06f;

    /// <summary>A drawing this much taller than wide (or wider than tall) gets a portrait (landscape) canvas.</summary>
    public const double ShapedCanvasRatio = 1.25;

    /// <summary>
    /// As <see cref="FitOnCanvas"/>; with fitShape, a clearly tall drawing (a dress) goes on a portrait canvas of
    /// width x 1.5 height, a clearly wide one on a landscape canvas, so the garment gets more of the pixels (a 256x256
    /// square left a dress about 150 px wide). Sides are multiples of 16, as image models need. Returns the size used.
    /// </summary>
    public static (byte[] Png, int Width, int Height) SketchCanvas(byte[] content, int width, int height, bool fitShape)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data) ?? throw new UnreadableImageException();
        var origin = codec.EncodedOrigin;
        var info = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul).WithColorSpace(SKColorSpace.CreateSrgb());
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new UnreadableImageException();

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (uprightW, uprightH) = swap ? (info.Height, info.Width) : (info.Width, info.Height);
        using var upright = new SKBitmap(new SKImageInfo(uprightW, uprightH, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using (var c = new SKCanvas(upright))
        using (var source = SKImage.FromBitmap(decoded))
        {
            c.Clear(SKColors.White);
            ApplyOrigin(c, origin, info.Width, info.Height);
            c.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }

        var box = SketchDrawings.Bounds(upright);   // the garment drawings only: pins, labels and edge lines left out
        if (fitShape && width == height)
        {
            var longer = (int)Math.Round(width * 1.5 / 16) * 16;
            var ratio = (double)box.Height / box.Width;
            if (ratio >= ShapedCanvasRatio) height = longer;
            else if (1 / ratio >= ShapedCanvasRatio) width = longer;
        }
        // A margin all round: a drawing touching the edge (a tall dress on a square) came back with its hem cut off.
        var scale = Math.Min(width * (1 - 2 * CanvasMargin) / box.Width, height * (1 - 2 * CanvasMargin) / box.Height);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()))
            ?? throw new UnreadableImageException();
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        var w = (float)(box.Width * scale);
        var h = (float)(box.Height * scale);
        // Centred: renders from a sketch are flat lays now (on mannequins FLUX drew heads in any space above the collar).
        var target = SKRect.Create((width - w) / 2, (height - h) / 2, w, h);
        using (var image = SKImage.FromBitmap(upright))
            canvas.DrawImage(image, SKRect.Create(box.Left, box.Top, box.Width, box.Height), target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100) ?? throw new UnreadableImageException();
        return (encoded.ToArray(), width, height);
    }

    /// <summary>JPEG has no transparency: transparent parts become white (a sketch on a clear background stays readable).</summary>
    private static byte[] EncodeJpeg(SKImage image, int width, int height)
    {
        using var flat = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()))!;
        flat.Canvas.Clear(SKColors.White);
        flat.Canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));   // same size: no resampling
        flat.Canvas.Flush();
        using var snapshot = flat.Snapshot();
        using var pixels = snapshot.PeekPixels();
        using var encoded = pixels.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
            ?? throw new UnreadableImageException();
        return encoded.ToArray();
    }

    private static bool HasTransparency(SKPixmap pixels)
    {
        var span = pixels.GetPixelSpan();
        for (var i = 3; i < span.Length; i += 4)
            if (span[i] != 255) return true;
        return false;
    }

    /// <summary>
    /// Canvas transform that draws a decoded image (stored w x h) upright, for the 8 EXIF orientations.
    /// Canvas calls apply to points last-first, e.g. RightTop: rotate 90° clockwise, then move right by h.
    /// </summary>
    internal static void ApplyOrigin(SKCanvas canvas, SKEncodedOrigin origin, int w, int h)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;                          // mirrored
            case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;                 // upside down
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;                        // flipped
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;                         // transposed
            case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;                     // rotated 90° (phone held upright)
            case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break; // transverse
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(-90); break;                  // rotated 270°
        }
    }
}
