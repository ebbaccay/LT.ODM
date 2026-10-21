using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace LT.ODM.Api.Tests;

public sealed class TechPackTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<HttpClient> SignInAsync(params string[] roles)
    {
        var name = "tp" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Readers_download_the_tech_pack_as_a_pdf_named_after_the_style()
    {
        var viewer = await SignInAsync("Viewer");
        var response = await viewer.GetAsync("/api/v1/styles/5/tech-pack");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("S1_SS27_2027-SS_techpack.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await response.Content.ReadAsByteArrayAsync())[..4]));

        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/styles/404/tech-pack")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).GetAsync("/api/v1/styles/5/tech-pack")).StatusCode);
    }

    [Fact]
    public async Task A_large_style_runs_onto_several_pages_with_pictures_and_any_script()
    {
        var store = new OneImageStore();
        var writer = new TechPackPdfWriter(store);
        var pdf = await writer.WriteAsync(LargeStyle(), "tester", new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]));
        // Header + colorways, BOM onto further pages, then two colour matrices (6 colorways, 4 per table).
        Assert.True(Pages(pdf) >= 4, $"Expected at least 4 pages, got {Pages(pdf)}.");
        Assert.Contains("sketch.png", store.Opened);
        Assert.Contains("missing.png", store.Opened);

        // TECHPACK_OUT=<folder> keeps the file to look at.
        if (Environment.GetEnvironmentVariable("TECHPACK_OUT") is { Length: > 0 } folder)
            await File.WriteAllBytesAsync(Path.Combine(folder, "techpack-test.pdf"), pdf);
    }

    private static int Pages(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var count = 0;
        for (var i = text.IndexOf("/Type /Page", StringComparison.Ordinal); i >= 0; i = text.IndexOf("/Type /Page", i + 1, StringComparison.Ordinal))
            if (!text.AsSpan(i + 11).StartsWith("s")) count++;
        return count;
    }

    private static StyleDetailDto LargeStyle()
    {
        byte[] rowVer = [0, 0, 0, 0, 0, 0, 0, 1];
        var colorways = Enumerable.Range(1, 6).Select(i => new ColorwayDto(i, (short)i, $"JX{4100 + i}", i == 3 ? "藏青 Collegiate Navy" : $"Colour {i}",
            i == 6 ? "DROPPED" : "INRANGE", i == 1 ? "/api/v1/styles/images/sketch.png" : null, "x", DateTime.UtcNow, null, null, rowVer)).ToList();
        string[] sections = ["Fabric", "Trims", "Accessories", "Labels & packaging"];
        var lines = Enumerable.Range(1, 44).Select(i => new BomLineDto(i, i, i * 10, $"7003{8100 + i}_{i}",
            i % 5 == 0
                ? "Tape; MADE TO BE REMADE; MADE WITH RECYCLED MATERIALS; Elasticity - no Info, Function - no Info, Workmanship - no Info, Woven; Unicolor; None; 100%POLYESTER(100%RECYCLED); 6.0 X 1.0 CM"
                : "100%Recycle Polyester,Single jersey,FD 60D/60F FDY+FD 30D/36F DTY",
            "FAB", "Fabric", sections[(i - 1) * sections.Length / 44], sections[(i - 1) * sections.Length / 44], "HF", "HUAFENG (VNM)", "S100", "Huafeng Textile",
            1.25m + i / 100m, i % 4 == 0 ? null : 1.3m, "YD", null,
            colorways.Where(c => (i + c.ColorwayId) % 3 != 0).Select(c => new BomLineColorwayDto(c.ColorwayId, $"C{c.ColorwayId}{i:00}",
                c.ColorwayId == 3 ? "藏青色" : $"Shade {c.ColorwayId}-{i}")).ToList(),
            "x", DateTime.UtcNow, null, null, rowVer)).ToList();
        return new StyleDetailDto(
            new StyleHeaderDto(1, "ADI", "adidas", "2028-SS", "S2808MR0000", "S2808MR0000",
                "MALE; 100%POLYESTER(100%RECYCLED); Solid; 101.0 G/SQM; T-SHIRT (SHORT SLEEVE)", "YY391", "ADI365 PRE T M", "KNT", "Knit",
                "TSS", "T-SHIRT (SHORT SLEEVE)", "MALE", 90, "RUX", "Running G", "/api/v1/styles/images/sketch.png", "/api/v1/styles/images/missing.png",
                null, 1, "ebaccay", DateTime.UtcNow, null, null, rowVer),
            colorways, lines, []);
    }

    /// <summary>Serves one small PNG as "sketch.png"; every other name is missing.</summary>
    private sealed class OneImageStore : IStyleImageStore
    {
        public List<string> Opened { get; } = [];

        public Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default) => throw new NotSupportedException();

        public (Stream Content, string ContentType)? Open(string fileName)
        {
            Opened.Add(fileName);
            if (fileName != "sketch.png") return null;
            using var surface = SKSurface.Create(new SKImageInfo(120, 160));
            surface.Canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.SteelBlue, IsAntialias = true };
            surface.Canvas.DrawRoundRect(new SKRect(20, 20, 100, 140), 12, 12, paint);
            using var image = surface.Snapshot();
            return (new MemoryStream(image.Encode(SKEncodedImageFormat.Png, 100).ToArray()), "image/png");
        }
    }
}
