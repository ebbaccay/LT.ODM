using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleAi;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class PictureDescribeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    /// <summary>Each text its own direction (one-hot by a stable hash), so the picture made from one phrase matches only that phrase.</summary>
    private sealed class PhraseModel : IImageEmbeddingModel
    {
        public ImageModelInfo Info => new(true, "phrase-model", null);
        public ImageModelInfo TextInfo => Info;
        public float[] Embed(byte[] content) => throw new NotSupportedException();

        public float[] EmbedText(string text)
        {
            var v = new float[768];
            v[(int)((uint)text.Aggregate(17, (h, c) => h * 31 + c) % 768)] = 1;
            return v;
        }
    }

    private sealed class Context : IPictureDescribeRepository
    {
        public Task<(IReadOnlyList<PictureProductType> ProductTypes, IReadOnlyList<PictureNeighbourStyle> Neighbours)> ContextAsync(
            string? customerCode, IReadOnlyList<int> styleIds, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<PictureProductType>, IReadOnlyList<PictureNeighbourStyle>)>((
            [
                new("70011", "SHORTS (1/2)", "SHORTS", 3, 3, 40),
                new("70010", "SHORTS (1/4)", "SHORTS", 9, 9, 60),
                new("70099", "UNUSED SHORTS", "SHORTS", 0, 0, 0),
                new("70020", "PANTS (1/1)", "PANTS", 12, 12, 80),
            ],
            [
                new(1, "A1", "ADI", "2027-SS", "KNT", "FEMALE", "100%POLYESTER(100%RECYCLED); 120 G/SQM", "SHORTS (1/4)"),
                new(2, "A2", "ADI", "2027-SS", "KNT", "FEMALE", "100%POLYESTER(100%RECYCLED)", "SHORTS (1/4)"),
                new(3, "A3", "ADI", "2027-SS", "WVN", "MALE", "100%NYLON", "SHORTS (1/2)"),
            ]));
    }

    [Fact]
    public async Task A_shorts_sketch_offers_the_customers_shorts_types_and_hints_from_lookalikes()
    {
        var model = new PhraseModel();
        var describer = new PictureDescriber(model, null!, new FakeImageSearchRepository(), new PictureTagger(model, null!), new Context(), null!);
        var picture = model.EmbedText(PictureDescribeRules.Phrase("shorts", isSketch: true));

        var d = await describer.DescribeVectorAsync(picture, isSketch: true, "ADI");

        Assert.Equal("SHORTS", d.Garment!.Value);
        Assert.True(d.Garment.Confident);
        // The customer's shorts types, most used first; never set alone, never an unused one or another garment's.
        Assert.Equal(["SHORTS (1/4)", "SHORTS (1/2)"], d.ProductTypes.Select(t => t.Name));
        Assert.Null(d.Sleeve);   // shorts have no sleeves to read
        Assert.StartsWith("SHORTS", d.Description);
        // Hints come from the look-alikes, with their votes.
        Assert.Equal(("KNT", 2, 3), (d.Weave!.Value, d.Weave.Votes, d.Weave.Of));
        Assert.Equal(("Polyester (recycled) 100%", 2), (d.Fabric!.Value, d.Fabric.Votes));
    }

    [Fact]
    public async Task Only_editors_and_only_when_the_image_model_can_run()
    {
        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync("/api/v1/styles/describe-picture", new { imageUrl = "/api/v1/styles/images/a.png", isSketch = true })).StatusCode);

        var editor = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/v1/styles/describe-picture", new { imageUrl = "", isSketch = true })).StatusCode);
        factory.ImageModel.IsAvailable = false;
        var response = await editor.PostAsJsonAsync("/api/v1/styles/describe-picture", new { imageUrl = "/api/v1/styles/images/a.png", isSketch = true });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("image model", await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> SignInAsync(params string[] roles)
    {
        var name = "pd" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}
