using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using LT.ODM.Infrastructure.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>
/// Bound from "ImageSearch:ImageModel": the look-alike image model run inside the API. Defaults fit Marqo-FashionSigLIP
/// (Apache 2.0; huggingface.co/Marqo/marqo-fashionSigLIP): copy its onnx/vision_model_int8.onnx (pictures) and, for search
/// by words, onnx/text_model_int8.onnx and tokenizer.json into App_Data/models/marqo-fashionSigLIP. Empty Folder turns
/// the model off (searches then use the Vision and Embedding jobs).
/// </summary>
public sealed class ImageModelOptions
{
    public const string SectionName = "ImageSearch:ImageModel";

    /// <summary>Model folder; relative paths start at the API's content root.</summary>
    public string Folder { get; set; } = "App_Data/models/marqo-fashionSigLIP";
    /// <summary>The vision tower's ONNX file inside Folder (takes pixel_values [n,3,Size,Size], returns image_embeds).</summary>
    public string File { get; set; } = "onnx/vision_model_int8.onnx";
    /// <summary>The text tower inside Folder (takes input_ids [n, 64], returns text_embeds in the same space as the pictures). Empty = no words.</summary>
    public string TextFile { get; set; } = "onnx/text_model_int8.onnx";
    /// <summary>Hugging Face tokenizer.json of the text tower, inside Folder.</summary>
    public string TokenizerFile { get; set; } = "tokenizer.json";
    /// <summary>Stored with every vector; changing it (or File) makes every picture be indexed again. Default: folder/file name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Square input size in pixels (pictures are squashed to it, as the model was trained).</summary>
    public int Size { get; set; } = 224;
    /// <summary>Pixel normalisation: (value / 255 - Mean) / Std, the same for R, G and B (SigLIP: 0.5 / 0.5).</summary>
    public float Mean { get; set; } = 0.5f;
    public float Std { get; set; } = 0.5f;
}

/// <summary>
/// Image and text embeddings with ONNX Runtime on the CPU, in one shared space. Pictures: decode (upright, transparency
/// on white), squash to Size x Size with a bicubic filter, normalise, run the vision tower. Words: <see cref="SiglipTokenizer"/>
/// then the text tower. Both L2-normalised. Each tower is loaded on first use and shared by all requests
/// (InferenceSession.Run is thread-safe). Nothing is sent anywhere.
/// </summary>
public sealed class OnnxImageEmbeddingModel : IImageEmbeddingModel, IDisposable
{
    private readonly ImageModelOptions _options;
    private readonly ILogger<OnnxImageEmbeddingModel> _logger;
    private readonly string _path;
    private readonly string _textPath;
    private readonly string _tokenizerPath;
    private readonly string _name;
    private readonly Lazy<InferenceSession> _session;
    private readonly Lazy<(InferenceSession Session, SiglipTokenizer Tokenizer)> _text;
    private string? _loadProblem;
    private string? _textProblem;

    public OnnxImageEmbeddingModel(IOptions<ImageModelOptions> options, IHostEnvironment env, ILogger<OnnxImageEmbeddingModel> logger)
    {
        _options = options.Value;
        _logger = logger;
        var folder = string.IsNullOrWhiteSpace(_options.Folder) ? "" : Path.GetFullPath(Path.Combine(env.ContentRootPath, _options.Folder));
        _path = folder.Length == 0 ? "" : Path.Combine(folder, _options.File);
        _textPath = folder.Length == 0 || string.IsNullOrWhiteSpace(_options.TextFile) ? "" : Path.Combine(folder, _options.TextFile);
        _tokenizerPath = folder.Length == 0 ? "" : Path.Combine(folder, _options.TokenizerFile);
        _name = !string.IsNullOrWhiteSpace(_options.Name)
            ? _options.Name.Trim()
            : $"{Path.GetFileName(folder.TrimEnd('/', '\\'))}/{Path.GetFileNameWithoutExtension(_options.File)}";
        _session = new Lazy<InferenceSession>(() => Load(_path, "image_embeds", "image", p => _loadProblem = p), LazyThreadSafetyMode.ExecutionAndPublication);
        _text = new Lazy<(InferenceSession, SiglipTokenizer)>(LoadText, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ImageModelInfo Info
    {
        get
        {
            if (_path.Length == 0) return new ImageModelInfo(false, _name, "The look-alike image model is turned off (ImageSearch:ImageModel:Folder is empty).");
            if (!System.IO.File.Exists(_path)) return new ImageModelInfo(false, _name, $"The look-alike image model is not installed ({_path} is missing).");
            return new ImageModelInfo(_loadProblem is null, _name, _loadProblem);
        }
    }

    public ImageModelInfo TextInfo
    {
        get
        {
            if (Info is { IsAvailable: false } image) return image;
            if (_textPath.Length == 0) return new ImageModelInfo(false, _name, "Search by words with the image model is turned off (ImageSearch:ImageModel:TextFile is empty).");
            foreach (var p in new[] { _textPath, _tokenizerPath })
                if (!System.IO.File.Exists(p)) return new ImageModelInfo(false, _name, $"The image model's text part is not installed ({p} is missing).");
            return new ImageModelInfo(_textProblem is null, _name, _textProblem);
        }
    }

    public float[] Embed(byte[] content)
    {
        if (!Info.IsAvailable) throw new AiServiceException(Info.Problem ?? "The look-alike image model is not available.");
        var session = Get(_session, "The look-alike image model could not be loaded.");
        var input = Pixels(content);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        return Normalised(results.FirstOrDefault(r => r.Name == "image_embeds") ?? results.First());
    }

    public float[] EmbedText(string text)
    {
        if (!TextInfo.IsAvailable) throw new AiServiceException(TextInfo.Problem ?? "Search by words with the image model is not available.");
        var (session, tokenizer) = Get(_text, "The image model's text part could not be loaded.");
        var ids = tokenizer.Encode(text);
        var input = new DenseTensor<long>(ids, [1, ids.Length]);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        return Normalised(results.FirstOrDefault(r => r.Name == "text_embeds") ?? results.First());
    }

    private static T Get<T>(Lazy<T> lazy, string message)
    {
        try
        {
            return lazy.Value;
        }
        catch (Exception ex) when (ex is not AiServiceException)
        {
            throw new AiServiceException(message, ex);
        }
    }

    private static float[] Normalised(DisposableNamedOnnxValue output)
    {
        var vector = output.AsEnumerable<float>().ToArray();
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0) for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
        return vector;
    }

    private (InferenceSession, SiglipTokenizer) LoadText()
    {
        SiglipTokenizer tokenizer;
        try
        {
            tokenizer = SiglipTokenizer.Load(_tokenizerPath);
        }
        catch (Exception ex)
        {
            _textProblem = "The image model's tokenizer could not be read: " + ex.Message;
            _logger.LogError(ex, "Tokenizer {Path} could not be read.", _tokenizerPath);
            throw;
        }
        return (Load(_textPath, "text_embeds", "text", p => _textProblem = p), tokenizer);
    }

    /// <summary>Loads one tower and checks it returns ImageDimensions numbers; a problem is kept so the screens can say why.</summary>
    private InferenceSession Load(string path, string output, string part, Action<string> problem)
    {
        try
        {
            var session = new InferenceSession(path, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });
            var outputs = session.OutputMetadata;
            var meta = outputs.TryGetValue(output, out var m) ? m : outputs.Values.First();
            var dims = meta.Dimensions.LastOrDefault();
            if (dims != ImageSearchRules.ImageDimensions)
            {
                session.Dispose();
                var message = $"The image model's {part} part returns {dims} numbers; image search needs {ImageSearchRules.ImageDimensions}.";
                problem(message);
                throw new AiServiceException(message);
            }
            _logger.LogInformation("Image model {Name} ({Part}) loaded from {Path}.", _name, part, path);
            return session;
        }
        catch (Exception ex) when (ex is not AiServiceException)
        {
            problem($"The image model's {part} part could not be loaded: " + ex.Message);
            _logger.LogError(ex, "Image model {Path} could not be loaded.", path);
            throw;
        }
    }

    /// <summary>[1, 3, Size, Size] in RGB order, normalised. Throws UnreadableImageException for content that is not a picture.</summary>
    private DenseTensor<float> Pixels(byte[] content)
    {
        var kind = ConceptStudioRules.DetectImage(content) ?? throw new UnreadableImageException();
        // Upright (EXIF) and at most 2x the input size, so the final squash is a small, clean step.
        var (upright, _) = ImageOptimizer.Optimize(content, kind, _options.Size * 2);
        using var source = SKBitmap.Decode(upright) ?? throw new UnreadableImageException();

        var size = _options.Size;
        using var target = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.White);   // transparent sketches on white, like the product images the model was trained on
            canvas.DrawImage(image, new SKRect(0, 0, size, size), new SKSamplingOptions(SKCubicResampler.CatmullRom));
        }

        var tensor = new DenseTensor<float>([1, 3, size, size]);
        var bytes = target.GetPixelSpan();
        float scale = 1f / (255f * _options.Std), shift = _options.Mean / _options.Std;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var i = (y * size + x) * 4;
            tensor[0, 0, y, x] = bytes[i] * scale - shift;
            tensor[0, 1, y, x] = bytes[i + 1] * scale - shift;
            tensor[0, 2, y, x] = bytes[i + 2] * scale - shift;
        }
        return tensor;
    }

    public void Dispose()
    {
        if (_session.IsValueCreated) _session.Value.Dispose();
        if (_text.IsValueCreated) _text.Value.Session.Dispose();
    }
}
