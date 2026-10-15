using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace LT.ODM.Api.Tests;

/// <summary>Styles per file key (set Matches); records every SetPicture call.</summary>
public sealed class FakeStyleImageImportRepository : IStyleImageImportRepository
{
    public Dictionary<string, List<StyleImageMatchDto>> Matches { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Styles that already have a picture (kept unless overwrite).</summary>
    public HashSet<int> HasPicture { get; } = [];
    public List<(int[] StyleIds, string Field, string Url, bool Overwrite, string By)> Sets { get; } = [];

    public Task<IReadOnlyList<(string FileKey, StyleImageMatchDto Match)>> MatchAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<(string, StyleImageMatchDto)>>(
            keys.SelectMany(k => Matches.TryGetValue(k, out var list) ? list.Select(m => (k, m)) : []).ToList());

    public Task<IReadOnlyList<(int StyleId, bool Updated)>> SetPictureAsync(
        IReadOnlyCollection<int> styleIds, string field, string imageUrl, bool overwrite, string changedBy, CancellationToken ct = default)
    {
        Sets.Add((styleIds.ToArray(), field, imageUrl, overwrite, changedBy));
        return Task.FromResult<IReadOnlyList<(int, bool)>>(styleIds.Select(id => (id, overwrite || !HasPicture.Contains(id))).ToList());
    }

    /// <summary>Styles with stored pictures, for "Check existing pictures".</summary>
    public List<ExistingPictureDto> Existing { get; } = [];
    public List<ExistingPictureMove[]> Moves { get; } = [];

    public Task<IReadOnlyList<ExistingPictureDto>> ExistingAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExistingPictureDto>>(Existing.ToList());

    public Task<IReadOnlyList<(int StyleId, bool Moved)>> MoveExistingAsync(IReadOnlyCollection<ExistingPictureMove> moves, string changedBy, CancellationToken ct = default)
    {
        Moves.Add(moves.ToArray());
        return Task.FromResult<IReadOnlyList<(int, bool)>>(moves.Select(m => (m.StyleId, Existing.Any(e => e.StyleId == m.StyleId && e.ImageUrl == m.ImageUrl && e.SketchUrl == m.SketchUrl))).ToList());
    }
}

/// <summary>Synthetic pages: a checkpoint-style table with a garment drawing beside it, and a page that is all garment.</summary>
internal static class GarmentPages
{
    /// <summary>900 x 400: a grid of text-like cells on the left (0-600), a garment at (660-840, 60-330).</summary>
    public static SKBitmap Sheet()
    {
        var bmp = new SKBitmap(900, 400, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var line = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 };
        for (var y = 20; y <= 380; y += 40) canvas.DrawLine(10, y, 600, y, line);
        for (var x = 10; x <= 600; x += 118) canvas.DrawLine(x, 20, x, 380, line);
        using var ink = new SKPaint { Color = SKColors.Black };
        for (var y = 34; y < 380; y += 40)
        for (var x = 20; x < 590; x += 118)
        for (var c = 0; c < 6; c++)
            canvas.DrawRect(x + c * 11, y, 7, 10, ink);   // "letters"
        Garment(canvas, 660, 60, 180, 270);
        return bmp;
    }

    /// <summary>300 x 300, the garment fills nearly all of it.</summary>
    public static SKBitmap WholeGarment()
    {
        var bmp = new SKBitmap(300, 300, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        Garment(canvas, 6, 6, 288, 288);
        return bmp;
    }

    /// <summary>A trouser-like shape: grey fill with a print, black outline.</summary>
    private static void Garment(SKCanvas canvas, float x, float y, float w, float h)
    {
        using var path = new SKPath();
        path.MoveTo(x + w * 0.1f, y);
        path.LineTo(x + w * 0.9f, y);
        path.LineTo(x + w, y + h);
        path.LineTo(x + w * 0.58f, y + h);
        path.LineTo(x + w * 0.5f, y + h * 0.35f);
        path.LineTo(x + w * 0.42f, y + h);
        path.LineTo(x, y + h);
        path.Close();
        using var fill = new SKPaint { Color = new SKColor(150, 150, 160), IsAntialias = true };
        canvas.DrawPath(path, fill);
        using var spot = new SKPaint { Color = new SKColor(40, 40, 50), IsAntialias = true };
        var random = new Random(3);
        canvas.Save();
        canvas.ClipPath(path);
        for (var i = 0; i < 60; i++) canvas.DrawCircle(x + (float)random.NextDouble() * w, y + (float)random.NextDouble() * h, 3 + (float)random.NextDouble() * 6, spot);
        canvas.Restore();
        using var outline = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
        canvas.DrawPath(path, outline);
    }

    public static byte[] Png(SKBitmap bmp) => bmp.Encode(SKEncodedImageFormat.Png, 100).ToArray();
}

public sealed class StyleImageImportRulesTests
{
    [Theory]
    [InlineData("RC2606OW001_Img01.jpg", "RC2606OW001")]
    [InlineData("rc2606ow001_img01.JPG", "RC2606OW001")]
    [InlineData("RC2702OM001A_Img01.png", "RC2702OM001A")]
    [InlineData("C:\\pictures\\RC2617OW006-H_front.png", "RC2617OW006-H")]
    [InlineData("RC2606OW001.jpg", "RC2606OW001")]
    [InlineData("_Img01.jpg", null)]
    [InlineData("my picture_1.jpg", null)]
    public void File_key_is_the_name_before_the_first_underscore(string fileName, string? key)
        => Assert.Equal(key, StyleImageImportRules.FileKey(fileName));
}

public sealed class GarmentCropFinderTests
{
    [Fact]
    public void Finds_the_garment_beside_a_table_without_the_model()
    {
        using var page = GarmentPages.Sheet();
        var choice = GarmentCropFinder.Choose(GarmentCropFinder.Analyse(page), scores: null);

        Assert.Equal(CropOutcome.Cropped, choice.Outcome);
        var box = choice.Box!.Value;
        // Holds the whole garment (660-840 x 60-330) and none of the table (x <= 600).
        Assert.True(box.X > 600 && box.X <= 660, $"left {box.X}");
        Assert.True(box.Y <= 60 && box.X + box.Width >= 840 && box.Y + box.Height >= 330, $"box {box}");
    }

    [Fact]
    public void Scores_choose_between_candidates_and_low_scores_find_nothing()
    {
        using var page = GarmentPages.Sheet();
        var analysis = GarmentCropFinder.Analyse(page);
        Assert.NotEmpty(analysis.Candidates);

        var low = analysis.Candidates.Select(_ => -0.06f).ToList();
        var none = GarmentCropFinder.Choose(analysis, low);
        Assert.Equal(CropOutcome.NotFound, none.Outcome);
        Assert.Null(none.Box);
    }

    [Fact]
    public void A_picture_that_is_all_garment_is_kept_whole()
    {
        using var page = GarmentPages.WholeGarment();
        var choice = GarmentCropFinder.Choose(GarmentCropFinder.Analyse(page), scores: null);
        Assert.Equal(CropOutcome.Whole, choice.Outcome);
        Assert.Null(choice.Box);
    }
}

public sealed class PictureKindRulesTests
{
    [Theory]
    [InlineData(0.03, 0.01, 0.6, "Sketch", true)]     // the model says sketch
    [InlineData(-0.02, 0.0, 0.4, "Photo", true)]      // the model says photo
    [InlineData(-0.012, 0.047, 0.34, "Sketch", false)] // dark flat: outlines tip it to sketch, close call
    [InlineData(-0.007, 0.029, 0.0, "Photo", true)]   // black tee on a grey studio background
    public void Model_score_outlines_and_background_decide(double model, double outline, double white, string kind, bool sure)
        => Assert.Equal((kind, sure), PictureKindRules.Decide(model, new PictureKindFeatures(outline, white)));

    [Fact]
    public void Without_the_model_only_clear_cases_are_decided_and_never_sure()
    {
        Assert.Equal(("Sketch", false), PictureKindRules.Decide(null, new PictureKindFeatures(0.05, 0.6)));
        Assert.Equal(("Photo", false), PictureKindRules.Decide(null, new PictureKindFeatures(0.0, 0.02)));
        Assert.Equal(((string?)null, false), PictureKindRules.Decide(null, new PictureKindFeatures(0.01, 0.5)));
    }

    [Fact]
    public void Outlines_and_white_are_measured()
    {
        using var sketch = GarmentPages.WholeGarment();
        var s = PictureKindRules.Measure(sketch);
        Assert.True(s.Outline > 0.01, $"outline {s.Outline}");
        Assert.True(s.White > 0.1, $"white {s.White}");   // the garment fills most of this page

        using var grey = new SKBitmap(200, 200);
        grey.Erase(new SKColor(200, 200, 205));
        Assert.True(PictureKindRules.Measure(grey).White < 0.01);
    }
}

public sealed class StyleImageImportTests : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private readonly ApiFactory factory;

    public StyleImageImportTests(ApiFactory factory)
    {
        this.factory = factory;
        factory.ImageImports.Sets.Clear();
        factory.ImageImports.HasPicture.Clear();
        factory.ImageImports.Matches["RC2606OW001"] =
        [
            new(750, "RC2606OW001", "2026-FW", "ADI", "Pants", null, null, true, "Exact", true),
            new(751, "RC2606OW001_H", "2026-FW", "ADI", "Pants", "/api/v1/styles/images/" + new string('a', 32) + ".jpg", null, true, "Suffix", true),
        ];
        factory.ImageModel.IsAvailable = false;
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "ii" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static MultipartFormDataContent Form(byte[] file, string name, params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", name);
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        return form;
    }

    [Fact]
    public async Task Only_admins_can_import_pictures()
    {
        var client = await SignInAsync("Merchandiser");
        using var page = GarmentPages.Sheet();
        var response = await client.PostAsync("/api/v1/style-library/image-imports/analyse", Form(GarmentPages.Png(page), "RC2606OW001_Img01.png"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Analyse_reads_the_style_number_and_suggests_the_garment_box()
    {
        var client = await SignInAsync("Admin");
        using var page = GarmentPages.Sheet();
        var response = await client.PostAsync("/api/v1/style-library/image-imports/analyse", Form(GarmentPages.Png(page), "RC2606OW001_Img01.png"));
        response.EnsureSuccessStatusCode();
        var a = await response.Content.ReadFromJsonAsync<StyleImageAnalysisDto>();

        Assert.Equal("RC2606OW001", a!.FileKey);
        Assert.Equal([750, 751], a.Matches.Select(m => m.StyleId));
        Assert.Equal((900, 400), (a.Crop.Width, a.Crop.Height));
        Assert.Equal("Cropped", a.Crop.Outcome);
        Assert.Equal("Rules", a.Crop.Method);   // model not installed
        Assert.Equal("Rules", a.Kind.Method);
        Assert.False(a.Kind.Sure);
        Assert.False(a.Crop.Sure);
        Assert.True(a.Crop.Box!.X > 600);
    }

    [Fact]
    public async Task Save_cuts_enlarges_and_sets_the_picture_on_the_ticked_styles()
    {
        var client = await SignInAsync("Admin");
        factory.ImageImports.HasPicture.Add(751);
        using var page = GarmentPages.Sheet();
        var response = await client.PostAsync("/api/v1/style-library/image-imports/save", Form(GarmentPages.Png(page), "RC2606OW001_Img01.png",
            ("x", "650"), ("y", "50"), ("width", "200"), ("height", "100"), ("enhance", "true"), ("field", "Image"), ("overwrite", "false"),
            ("styleIds", "750,751")));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StyleImageSaveResultDto>();

        Assert.Equal((800, 400), (result!.Width, result.Height));   // 200 x 100 cut, enlarged 4x
        Assert.True(result.Enhanced);
        Assert.Equal([750], result.Updated);
        Assert.Equal([751], result.Skipped);   // already had a picture
        var set = Assert.Single(factory.ImageImports.Sets);
        Assert.Equal("Image", set.Field);
        Assert.Matches(@"^/api/v1/styles/images/[0-9a-f]{32}\.(png|jpg)$", set.Url);

        // The stored file is served like any style picture.
        var stored = await client.GetAsync(set.Url);
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_the_cut_without_saving_anything()
    {
        var client = await SignInAsync("Admin");
        using var page = GarmentPages.Sheet();
        var response = await client.PostAsync("/api/v1/style-library/image-imports/preview", Form(GarmentPages.Png(page), "x.png",
            ("x", "650"), ("y", "50"), ("width", "700"), ("height", "100"), ("enhance", "false")));
        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        using var bmp = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal((250, 100), (bmp.Width, bmp.Height));   // the box is kept inside the picture (650 + 250 = 900)
        Assert.Empty(factory.ImageImports.Sets);
    }

    [Fact]
    public async Task Check_existing_suggests_moving_a_sketch_saved_as_the_picture_and_move_swaps_it()
    {
        // A line sketch on white, stored as style 900's picture.
        using var bmp = new SKBitmap(300, 300, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        using (var pen = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 6 })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawRect(50, 50, 200, 200, pen);
        }
        var store = factory.Services.GetRequiredService<IStyleImageStore>();
        var url = StyleImageImportService.StyleImageUrl(await store.SaveAsync(GarmentPages.Png(bmp), LT.ODM.Application.ConceptStudio.ImageKind.Png));
        factory.ImageImports.Existing.Clear();
        factory.ImageImports.Existing.Add(new ExistingPictureDto(900, "S900", "2027-SS", "ADI", null, url, null, null, null, "", false));
        factory.ImageImports.Moves.Clear();

        var merchandiser = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.Forbidden, (await merchandiser.PostAsync("/api/v1/style-library/image-imports/existing/check", null)).StatusCode);

        var client = await SignInAsync("Admin");
        var check = await (await client.PostAsync("/api/v1/style-library/image-imports/existing/check", null)).Content.ReadFromJsonAsync<ExistingPicturesCheckDto>();
        var s = Assert.Single(check!.Suggestions);
        Assert.Equal(("MoveToSketch", "Sketch", false), (s.Action, s.ImageKind!.Kind, s.Sure));   // rules only (no model): never sure

        var move = await client.PostAsJsonAsync("/api/v1/style-library/image-imports/existing/move",
            new { items = new[] { new { StyleId = 900, ImageUrl = url, SketchUrl = (string?)null }, new { StyleId = 901, ImageUrl = "x", SketchUrl = (string?)null } } });
        var result = await move.Content.ReadFromJsonAsync<ExistingPicturesMoveResultDto>();
        Assert.Equal([900], result!.Moved);
        Assert.Equal([901], result.Skipped);   // not what the check saw
    }

    [Fact]
    public async Task Save_needs_a_ticked_style_and_a_real_picture()
    {
        var client = await SignInAsync("Admin");
        using var page = GarmentPages.Sheet();
        var none = await client.PostAsync("/api/v1/style-library/image-imports/save", Form(GarmentPages.Png(page), "a.png", ("styleIds", "")));
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

        var notImage = await client.PostAsync("/api/v1/style-library/image-imports/save", Form("not a picture"u8.ToArray(), "a.png", ("styleIds", "750")));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);
        Assert.Empty(factory.ImageImports.Sets);
    }
}
