using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.SignInPage;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class SignInPageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "sp" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static MultipartFormDataContent Upload(byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "file", "background.png" } };
    }

    private sealed record UploadResult(string BackgroundImage, string BackgroundUrl);

    [Fact]
    public async Task Admins_upload_and_save_a_background_that_the_sign_in_page_reads_without_signing_in()
    {
        var admin = await SignInAsync("Admin");
        var anonymous = factory.CreateHttpsClient();

        var uploaded = await (await admin.PostAsync("/api/v1/sign-in-page/images", Upload(Png))).Content.ReadFromJsonAsync<UploadResult>();
        var spare = await (await admin.PostAsync("/api/v1/sign-in-page/images", Upload(Png))).Content.ReadFromJsonAsync<UploadResult>();

        var saved = await admin.PutAsJsonAsync("/api/v1/sign-in-page", new
        {
            backgroundImage = uploaded!.BackgroundImage, focus = "top", backgroundBlur = 4, overlay = "dark", overlayStrength = 40,
            showGlow = false, glassOpacity = 35, glassBlur = 24, showIntro = true, showFeatures = false, introText = "light",
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var page = await anonymous.GetFromJsonAsync<SignInAppearanceDto>("/api/v1/sign-in-page");
        Assert.Equal(uploaded.BackgroundUrl, page!.BackgroundUrl);
        Assert.Equal(("top", "dark", 40, 35, false, "light"), (page.Focus, page.Overlay, page.OverlayStrength, page.GlassOpacity, page.ShowFeatures, page.IntroText));
        Assert.False(string.IsNullOrEmpty(page.UpdatedBy));

        var picture = await anonymous.GetAsync(page.BackgroundUrl);
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("nosniff", picture.Headers.GetValues("X-Content-Type-Options").Single());

        // The upload that was never saved is gone.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(spare!.BackgroundUrl)).StatusCode);

        // Back to the default look.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/sign-in-page", new { })).StatusCode);
        var reset = await anonymous.GetFromJsonAsync<SignInAppearanceDto>("/api/v1/sign-in-page");
        Assert.Null(reset!.BackgroundUrl);
        Assert.Equal((SignInAppearance.Default.GlassOpacity, SignInAppearance.Default.ShowGlow), (reset.GlassOpacity, reset.ShowGlow));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(uploaded.BackgroundUrl)).StatusCode);
    }

    [Fact]
    public async Task Dark_mode_can_have_its_own_picture_and_look()
    {
        var admin = await SignInAsync("Admin");
        var anonymous = factory.CreateHttpsClient();
        var light = await (await admin.PostAsync("/api/v1/sign-in-page/images", Upload(Png))).Content.ReadFromJsonAsync<UploadResult>();
        var dark = await (await admin.PostAsync("/api/v1/sign-in-page/images", Upload(Png))).Content.ReadFromJsonAsync<UploadResult>();

        var saved = await admin.PutAsJsonAsync("/api/v1/sign-in-page", new
        {
            backgroundImage = light!.BackgroundImage, introText = "dark", glassOpacity = 55,
            dark = new { backgroundImage = dark!.BackgroundImage, focus = "bottom", overlay = "dark", overlayStrength = 20, introText = "light", glassOpacity = 80 },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var page = await anonymous.GetFromJsonAsync<SignInAppearanceDto>("/api/v1/sign-in-page");
        Assert.Equal((light.BackgroundUrl, "dark"), (page!.BackgroundUrl, page.IntroText));
        Assert.Equal((dark.BackgroundUrl, "bottom", "light", 80), (page.Dark!.BackgroundUrl, page.Dark.Focus, page.Dark.IntroText, page.Dark.GlassOpacity));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(dark.BackgroundUrl)).StatusCode);

        var bad = await admin.PutAsJsonAsync("/api/v1/sign-in-page", new { dark = new { glassOpacity = 5, backgroundImage = new string('b', 32) + ".png" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        Assert.Contains("dark.glassOpacity", body);
        Assert.Contains("dark.backgroundImage", body);

        // Dropping the dark look removes its picture at the save.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/sign-in-page", new { backgroundImage = light.BackgroundImage })).StatusCode);
        Assert.Null((await anonymous.GetFromJsonAsync<SignInAppearanceDto>("/api/v1/sign-in-page"))!.Dark);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(dark.BackgroundUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(light.BackgroundUrl)).StatusCode);
    }

    [Fact]
    public async Task Saving_checks_the_values_and_the_picture()
    {
        var admin = await SignInAsync("Admin");

        var bad = await admin.PutAsJsonAsync("/api/v1/sign-in-page", new { focus = "middle", glassOpacity = 100, overlay = "red" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        Assert.Contains("focus", body);
        Assert.Contains("glassOpacity", body);
        Assert.Contains("overlay", body);

        var missing = await admin.PutAsJsonAsync("/api/v1/sign-in-page", new { backgroundImage = new string('a', 32) + ".png" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("backgroundImage", await missing.Content.ReadAsStringAsync());

        var notImage = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "x.png" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/sign-in-page/images", notImage)).StatusCode);
    }

    [Fact]
    public async Task Only_admins_change_the_sign_in_page()
    {
        var anonymous = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/sign-in-page")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/v1/sign-in-page", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/sign-in-page/images", Upload(Png))).StatusCode);

        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/sign-in-page", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/v1/sign-in-page/images", Upload(Png))).StatusCode);
    }
}
