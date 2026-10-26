using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace LT.ODM.Api.Tests;

/// <summary>
/// Renders with a sketch on in-house (OpenAI-compatible) servers: image-to-image through images/edits, as OpenVINO Model
/// Server does with FLUX.1-schnell. Without a sketch, or with ImageSketchStrength 0, images/generations as before.
/// </summary>
public sealed class SketchRenderTests
{
    private static readonly AiConnection InHouse = new("OpenVINO", AiConnectionKinds.OpenAiCompatible, "http://ai.test/v3", null, "flux", true, 600, "settings");

    private sealed class Resolver(AiConnection c) : IAiConnectionResolver
    {
        public Task<AiConnection?> ResolveAsync(string purpose, CancellationToken ct = default) => Task.FromResult<AiConnection?>(c);
        public Task<AiResolution> ExplainAsync(string purpose, CancellationToken ct = default) => Task.FromResult(new AiResolution(c, null));
        public void Invalidate() { }
    }

    /// <summary>Records each request (path, content type, multipart parts) and answers with a 1x1 PNG.</summary>
    private sealed class Server : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public Dictionary<string, string> Fields { get; } = [];
        public byte[]? Image { get; private set; }
        public string? ImageType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            if (request.Content is MultipartFormDataContent form)
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name == "image") { Image = await part.ReadAsByteArrayAsync(ct); ImageType = part.Headers.ContentType?.MediaType; }
                    else Fields[name] = await part.ReadAsStringAsync(ct);
                }
            var body = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["b64_json"] = Convert.ToBase64String(FakeAiImageClient.Png) }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private static RoutedAiImageClient Client(Server server, double strength, string size = "512x512", bool fitShape = false)
        => new(new HttpClient(server), new Resolver(InHouse), new AiCallLimiter(),
            Options.Create(new OpenAiCompatibleOptions { ImageSize = size, ImageSketchStrength = strength, ImageFitShape = fitShape }),
            NullLogger<RoutedAiImageClient>.Instance);

    /// <summary>A wide sketch (two views side by side), red so the canvas padding can be told apart.</summary>
    private static ReferenceImage WideSketch()
    {
        using var bitmap = new SKBitmap(300, 100);
        bitmap.Erase(SKColors.Red);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 90);
        return new ReferenceImage(data.ToArray(), "image/jpeg");
    }

    [Fact]
    public async Task A_sketch_goes_to_images_edits_on_a_white_canvas_of_the_render_size()
    {
        var server = new Server();

        var image = await Client(server, 0.85).GenerateImageAsync("a style render", "Photorealistic product photo", WideSketch());

        Assert.Equal("/v3/images/edits", server.Path);
        Assert.Equal(("flux", "Photorealistic product photo", "512x512", "0.85"), (server.Fields["model"], server.Fields["prompt"], server.Fields["size"], server.Fields["strength"]));
        Assert.Equal("image/png", server.ImageType);
        using var sent = SKBitmap.Decode(server.Image);
        Assert.Equal((512, 512), (sent.Width, sent.Height));             // the size the model draws at
        Assert.Equal(SKColors.White, sent.GetPixel(256, 60));             // white above the wide sketch, not stretched
        var middle = sent.GetPixel(256, 256);
        Assert.True(middle.Red > 200 && middle.Green < 60, $"sketch in the middle, got {middle}");
        Assert.NotEmpty(image.Content);
    }

    private static ReferenceImage Sketch(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return new ReferenceImage(data.ToArray(), "image/png");
    }

    [Theory]
    [InlineData(100, 300, "256x384")]   // a dress: portrait, the garment gets 1.5 times the pixels
    [InlineData(300, 100, "384x256")]   // wide: landscape
    [InlineData(200, 220, "256x256")]   // about square: as set
    public async Task Tall_or_wide_drawings_get_a_canvas_of_their_shape(int width, int height, string expected)
    {
        var server = new Server();

        await Client(server, 0.85, "256x256", fitShape: true).GenerateImageAsync("a style render", "prompt", Sketch(width, height));

        Assert.Equal(expected, server.Fields["size"]);
        using var sent = SKBitmap.Decode(server.Image);
        Assert.Equal(expected, $"{sent.Width}x{sent.Height}");
    }

    [Fact]
    public void The_sketchs_white_margins_are_trimmed_so_the_garment_fills_the_canvas()
    {
        // A small drawing in the middle of a big white sheet (as uploaded sketches often are).
        using var bitmap = new SKBitmap(600, 600);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(new SKRect(200, 250, 400, 350), paint);
        }
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);

        using var fitted = SKBitmap.Decode(LT.ODM.Infrastructure.Files.ImageOptimizer.FitOnCanvas(data.ToArray(), 512, 512));

        Assert.Equal(SKColors.Red, fitted.GetPixel(50, 256));      // the drawing now spans almost the whole width
        Assert.Equal(SKColors.White, fitted.GetPixel(256, 60));    // not stretched: white above and below the 2:1 drawing
        Assert.Equal(SKColors.White, fitted.GetPixel(8, 256));     // and a margin at the edge (a hem touching it came back cut off)
    }

    [Fact]
    public async Task Without_a_sketch_or_with_strength_0_the_prompt_alone_goes_to_images_generations()
    {
        var server = new Server();
        await Client(server, 0.85).GenerateImageAsync("a style render", "prompt", null);
        Assert.Equal("/v3/images/generations", server.Path);

        var off = new Server();
        var client = Client(off, 0);
        await client.GenerateImageAsync("a style render", "prompt", WideSketch());
        Assert.Equal("/v3/images/generations", off.Path);
        Assert.False((await client.GetInfoAsync()).SupportsReferenceImage);   // the render page then offers no sketch
    }

    [Fact]
    public async Task In_house_image_connections_report_that_they_use_the_sketch()
        => Assert.True((await Client(new Server(), 0.85).GetInfoAsync()).SupportsReferenceImage);

    private static RenderFactsDto Dress(IReadOnlyList<string>? shape, bool frontAndBack)
        => new("dress", "women's", null, "ZEBRA DRESS", "LI8341", null, ["93% bci cotton 7% elastane"], ["white/black zebra"], [], shape, frontAndBack);

    [Fact]
    public void Shape_from_the_sketch_goes_into_the_description_of_a_flat_lay_for_image_to_image()
    {
        var prompt = RenderPrompt.Build(Dress(["long sleeves", "a high neck", "maxi length"], true), withSketch: true, sketchAsStart: true);

        Assert.StartsWith("Photorealistic flat lay product photo of a women's dress with long sleeves, a high neck and maxi length.", prompt);
        Assert.Contains("laid flat on a plain light-grey surface", prompt);
        Assert.DoesNotContain("mannequin", prompt);         // FLUX draws a head on any mannequin
        Assert.DoesNotContain("back view", prompt);         // each view is drawn on its own
        Assert.Contains("not a drawing", prompt);
        Assert.DoesNotContain("sketch", prompt);            // FLUX reads no instructions; "sketch" pulls it towards a drawing
        Assert.DoesNotContain("front view, centred", prompt);
    }

    [Fact]
    public void Instruction_following_models_are_still_told_to_follow_the_attached_sketch()
    {
        var gemini = RenderPrompt.Build(Dress(["long sleeves"], true), withSketch: true, sketchAsStart: false);
        Assert.Contains("attached flat sketch", gemini);
        Assert.Contains("with long sleeves.", gemini);

        var jersey = RenderPrompt.Build(new RenderFactsDto("jersey (sleeveless)", "men's", null, null, null, null, [], [], [], ["short sleeves"]), true, true);
        Assert.StartsWith("Photorealistic flat lay product photo of a men's jersey with short sleeves.", jersey);   // the sketch wins

        var noSketch = RenderPrompt.Build(Dress(null, false), withSketch: false);
        Assert.Contains("of a women's dress. Main fabric", noSketch);
        Assert.Contains("front view, centred", noSketch);
    }

    private static byte[] Drawing(params SKRect[] shapes)
    {
        using var bitmap = new SKBitmap(800, 500);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = new SKColor(200, 200, 200), IsAntialias = false };
            foreach (var r in shapes) canvas.DrawRect(r, paint);
        }
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Front_and_back_are_two_separate_drawings_of_similar_size()
    {
        var front = new SKRect(40, 60, 340, 460);
        var back = new SKRect(420, 60, 720, 460);
        var pin = new SKRect(380, 20, 392, 32);
        Assert.Equal(2, SketchShapeReader.CountDrawings(Drawing(front, back, pin)));          // a pin does not count
        Assert.Equal(2, SketchShapeReader.CountDrawings(Drawing(new SKRect(40, 20, 340, 240), new SKRect(420, 260, 720, 480))));   // offset diagonally
        Assert.Equal(1, SketchShapeReader.CountDrawings(Drawing(new SKRect(100, 50, 700, 450))));
        Assert.Equal(1, SketchShapeReader.CountDrawings(Drawing(new SKRect(40, 60, 340, 460), new SKRect(300, 100, 600, 480))));   // overlapping
    }

    /// <summary>Stands in for the Import pictures upscaler: "enlarges" to a marker PNG.</summary>
    private sealed class Enlarger : IGarmentCropper
    {
        public static readonly byte[] Big = FakeAiImageClient.Png.Concat(new byte[] { 1, 2, 3 }).ToArray();
        public int Calls { get; private set; }
        public UpscalerInfo Upscaler => new(true, null);
        public CropSuggestionDto Suggest(byte[] content, bool useModel) => throw new NotSupportedException();
        public PictureKindDto Classify(byte[] content, CropBoxDto? box, bool useModel) => throw new NotSupportedException();
        public (byte[] Png, int Width, int Height, bool Enhanced) Render(byte[] content, CropBoxDto? box, bool enhance, bool useModel)
        {
            Calls++;
            return (Big, 1024, 1024, true);
        }
    }

    private sealed class SavedImages : IStyleImageStore
    {
        public byte[]? Saved { get; private set; }
        public Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default)
        {
            Saved = content;
            return Task.FromResult("0".PadLeft(32, '0') + ".png");
        }
        public (Stream Content, string ContentType)? Open(string fileName) => null;
    }

    [Fact]
    public async Task Small_renders_are_enlarged_by_the_import_upscaler_before_they_are_saved()
    {
        var enlarger = new Enlarger();
        var store = new SavedImages();
        var assistant = new StyleAiAssistant(new FakeAiJsonClient(), new FakeAiImageClient(), new FakeStyleRepository(), new FakeStyleAiRepository(), store,
            new FakeAiConnectionResolver(), enlarger: enlarger);

        await assistant.RenderAsync(1, new RenderRequest(null, "Photorealistic product photo of a women's dress, plain background.", false), "tester");

        Assert.Equal(1, enlarger.Calls);
        Assert.Equal(Enlarger.Big, store.Saved);
    }

    [Fact]
    public void A_front_and_back_sketch_is_cut_into_its_drawings_and_the_renders_are_joined()
    {
        using var bitmap = new SKBitmap(800, 500);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(new SKRect(420, 60, 720, 460), blue);   // drawn first, but it is on the right: the back
            canvas.DrawRect(new SKRect(40, 60, 340, 460), red);
        }
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        var views = new LT.ODM.Infrastructure.Files.SketchViews();

        var parts = views.Split(new ReferenceImage(data.ToArray(), "image/png"))!.Value;

        using (var front = SKBitmap.Decode(parts.Front.Content)) Assert.Equal(SKColors.Red, front.GetPixel(front.Width / 2, front.Height / 2));
        using (var back = SKBitmap.Decode(parts.Back.Content)) Assert.Equal(SKColors.Blue, back.GetPixel(back.Width / 2, back.Height / 2));
        Assert.Null(views.Split(new ReferenceImage(Drawing(new SKRect(100, 50, 700, 450)), "image/png")));   // one drawing: nothing to split

        using var joined = SKBitmap.Decode(views.Join(new GeneratedImage(parts.Front.Content, ImageKind.Png), new GeneratedImage(parts.Back.Content, ImageKind.Png)).Content);
        Assert.Equal(SKColors.Red, joined.GetPixel(joined.Width / 4, joined.Height / 2));
        Assert.Equal(SKColors.Blue, joined.GetPixel(joined.Width * 3 / 4, joined.Height / 2));
    }

    /// <summary>Records each picture asked for; reports an image-to-image server.</summary>
    private sealed class RecordingImages : IAiImageClient
    {
        public List<(string Prompt, ReferenceImage? Reference)> Calls { get; } = [];
        public Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default)
            => Task.FromResult(new AiProviderInfo("OpenVINO", "flux", true, false, SupportsReferenceImage: true, SketchAsStartImage: true));
        public Task<GeneratedImage> GenerateImageAsync(string purpose, string prompt, ReferenceImage? reference, CancellationToken ct = default)
        {
            Calls.Add((prompt, reference));
            return Task.FromResult(new GeneratedImage(FakeAiImageClient.Png, ImageKind.Png));
        }
    }

    private sealed class FrontAndBackShape : ISketchShapeReader
    {
        public Task<SketchShapeDto> ReadAsync(string cacheKey, Func<CancellationToken, Task<byte[]?>> sketch, string? productTypeName, CancellationToken ct = default)
            => Task.FromResult(new SketchShapeDto(["long sleeves"], FrontAndBack: true));
    }

    private sealed class SketchFile : IStyleImageStore
    {
        public Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default) => Task.FromResult("0".PadLeft(32, '0') + ".png");
        public (Stream Content, string ContentType)? Open(string fileName)
            => (new MemoryStream(Drawing(new SKRect(40, 60, 340, 460), new SKRect(420, 60, 720, 460))), "image/png");
    }

    [Fact]
    public async Task A_front_and_back_sketch_is_rendered_one_view_at_a_time()
    {
        var images = new RecordingImages();
        var styles = new FakeStyleRepository { SketchUrl = "/api/v1/styles/images/" + "1".PadLeft(32, '1') + ".png" };
        var assistant = new StyleAiAssistant(new FakeAiJsonClient(), images, styles, new FakeStyleAiRepository(), new SketchFile(), new FakeAiConnectionResolver(),
            shapes: new FrontAndBackShape(), views: new LT.ODM.Infrastructure.Files.SketchViews());

        var brief = await assistant.RenderBriefAsync(1, null);
        await assistant.RenderAsync(1, new RenderRequest(null, null, UseSketch: true), "tester");

        Assert.Equal(2, brief.Estimate!.SketchRenders);
        Assert.Equal(2, images.Calls.Count);
        Assert.EndsWith(RenderPrompt.FrontView, images.Calls[0].Prompt);
        Assert.EndsWith(RenderPrompt.BackView, images.Calls[1].Prompt);
        Assert.All(images.Calls, c => Assert.NotNull(c.Reference));   // each view with its own drawing
    }

    /// <summary>Serves a picture per file name and records which ones were opened.</summary>
    private sealed class Pictures : IStyleImageStore
    {
        public List<string> Opened { get; } = [];
        public Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default) => Task.FromResult("0".PadLeft(32, '0') + ".png");
        public (Stream Content, string ContentType)? Open(string fileName)
        {
            Opened.Add(fileName);
            return (new MemoryStream(Drawing(new SKRect(100, 50, 700, 450))), "image/png");
        }
    }

    [Fact]
    public async Task The_colorways_own_picture_is_used_instead_of_the_sketch_when_it_has_one()
    {
        var sketch = "1".PadLeft(32, '1') + ".png";
        var picture = "2".PadLeft(32, '2') + ".png";
        var styles = new FakeStyleRepository
        {
            SketchUrl = "/api/v1/styles/images/" + sketch,
            Colorways =
            [
                new ColorwayDto(7, 1, "KX1225", "RESORT SHIRT BLUE", "INRANGE", "/api/v1/styles/images/" + picture, "x", DateTime.UtcNow, null, null, [1]),
                new ColorwayDto(8, 2, "KX1226", "RESORT SHIRT OWHITE", "INRANGE", null, "x", DateTime.UtcNow, null, null, [1]),
            ],
        };
        var images = new RecordingImages();
        var store = new Pictures();
        var assistant = new StyleAiAssistant(new FakeAiJsonClient(), images, styles, new FakeStyleAiRepository(), store, new FakeAiConnectionResolver());

        var brief = await assistant.RenderBriefAsync(1, 7);
        await assistant.RenderAsync(1, new RenderRequest(7, "Photorealistic flat lay product photo of a men's shirt.", UseSketch: true), "tester");
        Assert.Equal((true, "/api/v1/styles/images/" + picture), (brief.GuideIsColorway, brief.GuideUrl));
        Assert.Equal(picture, store.Opened.Last());

        var other = await assistant.RenderBriefAsync(1, 8);   // no picture of its own: the sketch
        Assert.Equal((false, "/api/v1/styles/images/" + sketch), (other.GuideIsColorway, other.GuideUrl));
    }

    [Theory]
    [InlineData("DRESS", "dress")]
    [InlineData("SKIRT", "skirt")]
    [InlineData("SHORTS (1/2)", "bottom")]
    [InlineData("PANTS1/1", "bottom")]
    [InlineData("HOODED SWEAT", "top")]
    [InlineData(null, null)]
    public void Product_types_settle_an_unsure_garment_type(string? productType, string? type)
        => Assert.Equal(type, SketchShapeReader.TypeFromProductType(productType));

    [Theory]
    [InlineData(900, true)]      // a CPU render from a sketch: 5-10 minutes
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    [InlineData(4, false)]
    public void Connection_timeouts_allow_slow_image_servers(int seconds, bool valid)
    {
        var request = new SaveAiConnectionRequest(null, "OpenVINO", AiConnectionKinds.OpenAiCompatible, "http://localhost:8000/v1", null, false, true, seconds, null, true);
        Assert.Equal(valid, !AiSettingsValidation.Connection(request).ContainsKey("timeoutSeconds"));
    }

    [Theory]
    [InlineData("512x512", 512, 512)]
    [InlineData("768X1024", 768, 1024)]
    [InlineData("big", 1024, 1024)]
    [InlineData("10x10", 1024, 1024)]
    public void Render_sizes_are_read_safely(string size, int width, int height)
        => Assert.Equal((width, height), OpenAiApi.ParseSize(size));
}
