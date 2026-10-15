using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LT.ODM.Application.Ai;
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

    private static RoutedAiImageClient Client(Server server, double strength, string size = "512x512")
        => new(new HttpClient(server), new Resolver(InHouse),
            Options.Create(new OpenAiCompatibleOptions { ImageSize = size, ImageSketchStrength = strength }), NullLogger<RoutedAiImageClient>.Instance);

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
        Assert.Equal(SKColors.White, sent.GetPixel(256, 10));             // padding above the wide sketch, not stretched
        var middle = sent.GetPixel(256, 256);
        Assert.True(middle.Red > 200 && middle.Green < 60, $"sketch in the middle, got {middle}");
        Assert.NotEmpty(image.Content);
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

    [Theory]
    [InlineData("512x512", 512, 512)]
    [InlineData("768X1024", 768, 1024)]
    [InlineData("big", 1024, 1024)]
    [InlineData("10x10", 1024, 1024)]
    public void Render_sizes_are_read_safely(string size, int width, int height)
        => Assert.Equal((width, height), OpenAiApi.ParseSize(size));
}
