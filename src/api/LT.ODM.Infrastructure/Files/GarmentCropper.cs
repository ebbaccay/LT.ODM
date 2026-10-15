using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>
/// Bound from "StyleImages:Upscaler": the picture upscaler run inside the API for Settings > Import pictures. Default fits
/// Real-ESRGAN general x4v3 (BSD-3-Clause; ONNX export at huggingface.co/jonathanst29/tinier-upscale-models, 4.6 MB):
/// input float [1, 3, h, w] RGB 0-1, output 4x. Empty File turns it off (small pictures are then enlarged smoothly).
/// </summary>
public sealed class UpscalerOptions
{
    public const string SectionName = "StyleImages:Upscaler";

    /// <summary>ONNX file; relative paths start at the API's content root.</summary>
    public string File { get; set; } = "App_Data/models/realesr-general-x4v3/realesr-general-x4v3.onnx";
}

/// <summary>
/// Cuts the garment out of a picture (<see cref="GarmentCropFinder"/>; the look-alike image model chooses between the
/// candidates when it is installed and AI is on) and enlarges small results 4x per pass with the upscaler.
/// Everything runs on the API's CPU; nothing is sent anywhere. Pictures larger than 1600 px are analysed at 1600 px.
/// </summary>
public sealed class GarmentCropper : IGarmentCropper, IDisposable
{
    /// <summary>Analysis size limit (longest side); boxes are scaled back to the full picture.</summary>
    private const int AnalyseMax = 1600;
    /// <summary>Results this small or larger are not enlarged (the style store keeps at most 1600 px anyway).</summary>
    public const int EnhanceBelow = 600;
    /// <summary>Results smaller than this get a second 4x pass (a 60 px thumbnail would otherwise end at 240 px).</summary>
    public const int SecondPassBelow = 150;

    // Words the image model compares each candidate with: garment pictures against table text.
    private static readonly string[] GarmentWords =
        ["a sketch of a garment", "a photo of clothing", "a technical flat drawing of a jacket, trousers, shorts or shirt"];
    private static readonly string[] OtherWords = ["text", "a table with words and numbers", "a spreadsheet", "a blank white area"];

    private readonly IImageEmbeddingModel _model;
    private readonly ILogger<GarmentCropper> _logger;
    private readonly string _upscalerPath;
    private readonly Lazy<InferenceSession> _upscaler;
    private readonly Lazy<(float[][] Garment, float[][] Other)> _words;
    private readonly Lazy<(float[][] Sketch, float[][] Photo)> _kindWords;
    private string? _upscalerProblem;

    public GarmentCropper(IImageEmbeddingModel model, IOptions<UpscalerOptions> options, IHostEnvironment env, ILogger<GarmentCropper> logger)
    {
        _model = model;
        _logger = logger;
        var file = options.Value.File;
        _upscalerPath = string.IsNullOrWhiteSpace(file) ? "" : Path.GetFullPath(Path.Combine(env.ContentRootPath, file));
        _upscaler = new Lazy<InferenceSession>(LoadUpscaler, LazyThreadSafetyMode.ExecutionAndPublication);
        _words = new Lazy<(float[][], float[][])>(
            () => (GarmentWords.Select(_model.EmbedText).ToArray(), OtherWords.Select(_model.EmbedText).ToArray()),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _kindWords = new Lazy<(float[][], float[][])>(
            () => (PictureKindRules.SketchWords.Select(_model.EmbedText).ToArray(), PictureKindRules.PhotoWords.Select(_model.EmbedText).ToArray()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public UpscalerInfo Upscaler
    {
        get
        {
            if (_upscalerPath.Length == 0) return new UpscalerInfo(false, "The upscaler is turned off (StyleImages:Upscaler:File is empty).");
            if (!System.IO.File.Exists(_upscalerPath)) return new UpscalerInfo(false, $"The upscaler is not installed ({_upscalerPath} is missing).");
            return new UpscalerInfo(_upscalerProblem is null, _upscalerProblem);
        }
    }

    public CropSuggestionDto Suggest(byte[] content, bool useModel)
    {
        using var picture = Decode(content);
        var scale = Math.Min(1.0, (double)AnalyseMax / Math.Max(picture.Width, picture.Height));
        using var small = scale < 1 ? picture.Resize(new SKImageInfo(Math.Max(1, (int)(picture.Width * scale)), Math.Max(1, (int)(picture.Height * scale))),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) : null;
        var analysed = small ?? picture;

        var analysis = GarmentCropFinder.Analyse(analysed);
        var scores = useModel && _model.TextInfo.IsAvailable ? TryScores(analysed, analysis.Candidates) : null;
        var choice = GarmentCropFinder.Choose(analysis, scores);
        CropBoxDto? box = null;
        if (choice.Box is { } b)
        {
            int x0 = (int)Math.Floor(b.X / scale), y0 = (int)Math.Floor(b.Y / scale);
            int x1 = Math.Min(picture.Width, (int)Math.Ceiling((b.X + b.Width) / scale)), y1 = Math.Min(picture.Height, (int)Math.Ceiling((b.Y + b.Height) / scale));
            box = new CropBoxDto(x0, y0, x1 - x0, y1 - y0);
        }
        var sure = choice.Outcome == CropOutcome.Whole || choice.Score >= GarmentCropFinder.SureScore;
        return new CropSuggestionDto(picture.Width, picture.Height, box, choice.Outcome.ToString(), scores is null ? "Rules" : "Model", sure);
    }

    public PictureKindDto Classify(byte[] content, CropBoxDto? box, bool useModel)
    {
        using var picture = Decode(content);
        var rect = Clamp(box, picture.Width, picture.Height);
        using var crop = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(crop))
        using (var image = SKImage.FromBitmap(picture))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, rect, new SKRect(0, 0, rect.Width, rect.Height), new SKSamplingOptions(SKFilterMode.Nearest));
        }
        var features = PictureKindRules.Measure(crop);
        double? model = null;
        if (useModel && _model.TextInfo.IsAvailable)
        {
            try
            {
                var (sketch, photo) = _kindWords.Value;
                using var png = crop.Encode(SKEncodedImageFormat.Png, 100);
                var v = _model.Embed(png.ToArray());
                model = sketch.Average(s => Dot(s, v)) - photo.Average(p => Dot(p, v));
            }
            catch (Exception ex) when (ex is LT.ODM.Application.Ai.AiServiceException or OnnxRuntimeException)
            {
                _logger.LogWarning(ex, "Image model unavailable for sketch/photo detection; using the rules only.");
            }
        }
        var (kind, sure) = PictureKindRules.Decide(model, features);
        return new PictureKindDto(kind, sure, model is null ? "Rules" : "Model");
    }

    public (byte[] Png, int Width, int Height, bool Enhanced) Render(byte[] content, CropBoxDto? box, bool enhance, bool useModel)
    {
        using var picture = Decode(content);
        var rect = Clamp(box, picture.Width, picture.Height);
        var current = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(current))
        using (var image = SKImage.FromBitmap(picture))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, rect, new SKRect(0, 0, rect.Width, rect.Height), new SKSamplingOptions(SKFilterMode.Nearest));
        }

        var enhanced = false;
        try
        {
            var longest = Math.Max(current.Width, current.Height);
            if (enhance && longest < EnhanceBelow)
            {
                var passes = longest < SecondPassBelow ? 2 : 1;
                var ai = useModel && Upscaler.IsAvailable;
                for (var i = 0; i < passes; i++)
                {
                    var next = ai ? TryUpscale(current) ?? Smooth(current, 4) : Smooth(current, 4);
                    current.Dispose();
                    current = next;
                }
                enhanced = true;
            }
            using var data = current.Encode(SKEncodedImageFormat.Png, 100) ?? throw new UnreadableImageException();
            return (data.ToArray(), current.Width, current.Height, enhanced);
        }
        finally
        {
            current.Dispose();
        }
    }

    /// <summary>Scores, or null (rules only) when the image model fails; the reason is logged.</summary>
    private float[]? TryScores(SKBitmap picture, IReadOnlyList<CropBox> candidates)
    {
        try
        {
            return Scores(picture, candidates);
        }
        catch (Exception ex) when (ex is LT.ODM.Application.Ai.AiServiceException or OnnxRuntimeException)
        {
            _logger.LogWarning(ex, "Image model unavailable for picture import; using the rules only.");
            return null;
        }
    }

    /// <summary>One score per candidate: closest garment words minus closest other words (image model vectors).</summary>
    private float[] Scores(SKBitmap picture, IReadOnlyList<CropBox> candidates)
    {
        var (garment, other) = _words.Value;
        var scores = new float[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            using var crop = new SKBitmap(new SKImageInfo(c.Width, c.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            picture.ExtractSubset(crop, new SKRectI(c.X, c.Y, c.X + c.Width, c.Y + c.Height));
            using var copy = crop.Copy();
            using var png = copy.Encode(SKEncodedImageFormat.Png, 100);
            var v = _model.Embed(png.ToArray());
            scores[i] = garment.Max(g => Dot(g, v)) - other.Max(o => Dot(o, v));
        }
        return scores;
    }

    private static float Dot(float[] a, float[] b)
    {
        var s = 0f;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    /// <summary>Upscaled, or null (plain enlargement) when the upscaler fails to load or run; the reason is logged.</summary>
    private SKBitmap? TryUpscale(SKBitmap source)
    {
        try
        {
            return Upscale(source);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Upscaler failed; enlarging without it.");
            return null;
        }
    }

    /// <summary>4x with the upscaler (Real-ESRGAN): sharp lines and print instead of the blur of a plain resize.</summary>
    private SKBitmap Upscale(SKBitmap source)
    {
        var session = _upscaler.Value;
        int w = source.Width, h = source.Height;
        var px = source.GetPixelSpan();
        var input = new DenseTensor<float>([1, 3, h, w]);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            input[0, 0, y, x] = px[i] / 255f;
            input[0, 1, y, x] = px[i + 1] / 255f;
            input[0, 2, y, x] = px[i + 2] / 255f;
        }
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var output = results.First().AsTensor<float>();
        int oh = output.Dimensions[2], ow = output.Dimensions[3];
        var bytes = new byte[ow * oh * 4];
        for (var y = 0; y < oh; y++)
        for (var x = 0; x < ow; x++)
        {
            var i = (y * ow + x) * 4;
            bytes[i] = Byte(output[0, 0, y, x]);
            bytes[i + 1] = Byte(output[0, 1, y, x]);
            bytes[i + 2] = Byte(output[0, 2, y, x]);
            bytes[i + 3] = 255;
        }
        var result = new SKBitmap(new SKImageInfo(ow, oh, SKColorType.Rgba8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, result.GetPixels(), bytes.Length);
        return result;

        static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
    }

    /// <summary>Plain enlargement when the upscaler is not there (soft, but the picture is not tiny on the style page).</summary>
    private static SKBitmap Smooth(SKBitmap source, int factor)
        => source.Resize(new SKImageInfo(source.Width * factor, source.Height * factor, SKColorType.Rgba8888, SKAlphaType.Premul),
               new SKSamplingOptions(SKCubicResampler.Mitchell))
           ?? throw new UnreadableImageException();

    private InferenceSession LoadUpscaler()
    {
        try
        {
            var session = new InferenceSession(_upscalerPath, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });
            _logger.LogInformation("Upscaler loaded from {Path}.", _upscalerPath);
            return session;
        }
        catch (Exception ex)
        {
            _upscalerProblem = "The upscaler could not be loaded: " + ex.Message;
            _logger.LogError(ex, "Upscaler {Path} could not be loaded.", _upscalerPath);
            throw;
        }
    }

    /// <summary>Upright (EXIF), full size, sRGB. Coordinates of boxes refer to this picture.</summary>
    private static SKBitmap Decode(byte[] content)
    {
        var kind = ConceptStudioRules.DetectImage(content) ?? throw new UnreadableImageException();
        var (upright, _) = ImageOptimizer.Optimize(content, kind, int.MaxValue);
        return SKBitmap.Decode(upright) ?? throw new UnreadableImageException();
    }

    private static SKRectI Clamp(CropBoxDto? box, int w, int h)
    {
        if (box is null) return new SKRectI(0, 0, w, h);
        int x0 = Math.Clamp(box.X, 0, w - 1), y0 = Math.Clamp(box.Y, 0, h - 1);
        int x1 = Math.Clamp(box.X + box.Width, x0 + 1, w), y1 = Math.Clamp(box.Y + box.Height, y0 + 1, h);
        return new SKRectI(x0, y0, x1, y1);
    }

    public void Dispose()
    {
        if (_upscaler.IsValueCreated) _upscaler.Value.Dispose();
    }
}
