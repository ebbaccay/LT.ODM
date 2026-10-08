using LT.ODM.Application.ConceptStudio;
using LT.ODM.Infrastructure.Files;
using SkiaSharp;

namespace LT.ODM.Api.Tests;

public sealed class ImageOptimizerTests
{
    /// <summary>Photo-like content (fine noise over a gradient): compresses like a real photo, not like flat colour.</summary>
    private static SKBitmap Photo(int width, int height)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var random = new Random(7);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bmp.SetPixel(x, y, new SKColor((byte)(x * 255 / width), (byte)random.Next(256), (byte)(y * 255 / height)));
        return bmp;
    }

    /// <summary>A flat sketch: black lines on a transparent background.</summary>
    private static SKBitmap Sketch(int width, int height)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var pen = new SKPaint { Color = SKColors.Black, StrokeWidth = 6, Style = SKPaintStyle.Stroke, IsAntialias = true };
        canvas.DrawRect(width * 0.2f, height * 0.1f, width * 0.6f, height * 0.8f, pen);
        canvas.DrawLine(width * 0.2f, height * 0.3f, width * 0.05f, height * 0.5f, pen);
        return bmp;
    }

    private static byte[] Encode(SKBitmap bmp, SKEncodedImageFormat format, int quality = 95)
        => bmp.Encode(format, quality).ToArray();

    private static SKBitmap Decode(byte[] content) => SKBitmap.Decode(content);

    [Fact]
    public void Large_photos_are_scaled_to_the_limit_and_stored_as_jpeg()
    {
        using var photo = Photo(3000, 2000);
        var original = Encode(photo, SKEncodedImageFormat.Jpeg);

        var (content, kind) = ImageOptimizer.Optimize(original, ImageKind.Jpeg, 1600);

        Assert.Equal(ImageKind.Jpeg, kind);
        using var stored = Decode(content);
        Assert.Equal((1600, 1067), (stored.Width, stored.Height));   // aspect ratio kept
        Assert.True(content.Length < original.Length / 3, $"{content.Length} vs {original.Length}");
    }

    [Fact]
    public void Photos_uploaded_as_png_become_jpeg()
    {
        using var photo = Photo(1200, 800);
        var (content, kind) = ImageOptimizer.Optimize(Encode(photo, SKEncodedImageFormat.Png), ImageKind.Png, 1600);
        Assert.Equal(ImageKind.Jpeg, kind);
        Assert.Equal(ConceptStudioRules.DetectImage(content), ImageKind.Jpeg);
    }

    [Fact]
    public void Sketches_keep_png_and_their_transparency()
    {
        using var sketch = Sketch(2400, 1600);
        var (content, kind) = ImageOptimizer.Optimize(Encode(sketch, SKEncodedImageFormat.Png), ImageKind.Png, 1600);

        Assert.Equal(ImageKind.Png, kind);
        using var stored = Decode(content);
        Assert.Equal(1600, stored.Width);
        Assert.Equal(0, stored.GetPixel(5, 5).Alpha);   // background still clear
    }

    [Fact]
    public void Small_files_that_cannot_be_made_smaller_are_kept_as_uploaded()
    {
        using var photo = Photo(300, 200);
        var original = Encode(photo, SKEncodedImageFormat.Jpeg, 60);
        var (content, kind) = ImageOptimizer.Optimize(original, ImageKind.Jpeg, 1600);
        Assert.Equal(ImageKind.Jpeg, kind);
        Assert.Same(original, content);
    }

    [Fact]
    public void Phone_photos_are_turned_upright()
    {
        // Stored 40 x 20: left half red, right half blue. EXIF orientation 6 = "rotate 90° clockwise to view".
        using var bmp = new SKBitmap(40, 20);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(0, 0, 20, 20, new SKPaint { Color = SKColors.Red });
        }
        var jpeg = WithOrientation(Encode(bmp, SKEncodedImageFormat.Jpeg), 6);

        var (content, _) = ImageOptimizer.Optimize(jpeg, ImageKind.Jpeg, 1600);

        using var upright = Decode(content);
        Assert.Equal((20, 40), (upright.Width, upright.Height));
        Assert.True(upright.GetPixel(10, 5).Red > 200 && upright.GetPixel(10, 5).Blue < 60, "top should be red");
        Assert.True(upright.GetPixel(10, 35).Blue > 200 && upright.GetPixel(10, 35).Red < 60, "bottom should be blue");
    }

    [Fact]
    public void Content_that_is_not_an_image_is_refused()
    {
        byte[] fake = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];   // PNG signature only
        Assert.Throws<UnreadableImageException>(() => ImageOptimizer.Optimize(fake, ImageKind.Png, 1600));
    }

    /// <summary>Inserts an EXIF block with only the orientation tag right after the JPEG start marker.</summary>
    private static byte[] WithOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,                                     // APP1, length 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,   // big-endian TIFF header, first IFD at 8
            0x00, 0x01,                                                 // one entry
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,             // Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                                     // no next IFD
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }
}
