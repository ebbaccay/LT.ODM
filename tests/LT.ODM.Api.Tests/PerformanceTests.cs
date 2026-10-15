using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace LT.ODM.Api.Tests;

/// <summary>Picture caching and thumbnails, and the dashboard kept in memory.</summary>
public sealed class PerformanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "pf" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static byte[] Photo(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(20, 30, 80));
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    private static (int Width, int Height) SizeOf(byte[] picture)
    {
        using var bitmap = SKBitmap.Decode(picture);
        return (bitmap.Width, bitmap.Height);
    }

    /// <summary>private (needs sign-in), a year, never re-checked: names are never reused.</summary>
    private static void AssertKept(HttpResponseMessage r)
    {
        var c = r.Headers.CacheControl!;
        Assert.True(c.Private);
        Assert.Equal(TimeSpan.FromDays(365), c.MaxAge);
        Assert.Contains(c.Extensions, e => e.Name == "immutable");
        Assert.False(c.NoStore);
    }

    [Fact]
    public async Task Pictures_are_cached_by_browsers_and_come_as_thumbnails()
    {
        var store = factory.Services.GetRequiredService<IStyleImageStore>();
        var url = "/api/v1/styles/images/" + await store.SaveAsync(Photo(1200, 800), ImageKind.Jpeg);
        var viewer = await SignInAsync("Viewer");

        var original = await viewer.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        AssertKept(original);
        Assert.Equal((1200, 800), SizeOf(await original.Content.ReadAsByteArrayAsync()));

        var thumb = await viewer.GetAsync(url + "?w=160");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
        AssertKept(thumb);
        Assert.Equal((160, 107), SizeOf(await thumb.Content.ReadAsByteArrayAsync()));
        Assert.Equal("nosniff", thumb.Headers.GetValues("X-Content-Type-Options").Single());

        // Made once, then served from disk.
        var name = url[(url.LastIndexOf('/') + 1)..];
        Assert.True(File.Exists(Path.Combine(factory.FilesRoot, "style-library", "_thumbs", "160", name)));
        Assert.Equal((160, 107), SizeOf(await viewer.GetByteArrayAsync(url + "?w=160")));

        // Sizes not offered get the original; unknown files are 404 either way.
        Assert.Equal((1200, 800), SizeOf(await viewer.GetByteArrayAsync(url + "?w=5000")));
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/styles/images/" + new string('f', 32) + ".jpg?w=160")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync(url + "?w=160")).StatusCode);
    }

    // ----- Request limit -----

    [Fact]
    public async Task The_request_limit_is_per_user_and_pictures_do_not_count()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:PermitPerMinute", "6"));
        async Task<HttpClient> SignIn()
        {
            var name = "rl" + Guid.NewGuid().ToString("N")[..8];
            await factory.Users.CreateUserAsync(name, $"{name}@company.test", name,
                limited.Services.GetRequiredService<IPasswordHasher>().Hash(Password), false, ["Viewer"]);
            var client = limited.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
                .Content.ReadFromJsonAsync<AuthResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
            return client;
        }

        var url = "/api/v1/styles/images/" + await limited.Services.GetRequiredService<IStyleImageStore>().SaveAsync(Photo(400, 300), ImageKind.Jpeg);
        var first = await SignIn();
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(url + "?w=160")).StatusCode);   // pictures are free

        for (var i = 0; i < 6; i++) Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/v1/styles/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.GetAsync("/api/v1/styles/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(url)).StatusCode);                                            // still free

        // Another user on the same address (an office behind one IP) has their own allowance.
        var second = await SignIn();
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/styles/dashboard")).StatusCode);
    }

    // ----- Dashboard cache -----

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static StyleDashboardDto Dashboard(int styles)
        => new(new StyleDashboardTotalsDto(styles, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [], [], [], [], [], [], [], null);

    [Fact]
    public async Task The_dashboard_is_loaded_once_until_the_library_changes_or_minutes_pass()
    {
        var clock = new Clock();
        var cache = new StyleDashboardCache(clock);
        var loads = 0;
        Task<StyleDashboardDto> Load(CancellationToken _) => Task.FromResult(Dashboard(++loads));

        Assert.Equal(1, (await cache.GetAsync(Load, default)).Totals.Styles);
        Assert.Equal(1, (await cache.GetAsync(Load, default)).Totals.Styles);   // kept
        cache.Invalidate();                                                        // a style was saved
        Assert.Equal(2, (await cache.GetAsync(Load, default)).Totals.Styles);
        clock.Now += StyleDashboardCache.CacheFor;                                 // changes made outside the app
        Assert.Equal(3, (await cache.GetAsync(Load, default)).Totals.Styles);

        // Many users at once after a change: one database call.
        cache.Invalidate();
        var gate = new TaskCompletionSource();
        async Task<StyleDashboardDto> Slow(CancellationToken _) { await gate.Task; return Dashboard(++loads); }
        var all = Enumerable.Range(0, 5).Select(_ => cache.GetAsync(Slow, default)).ToList();
        gate.SetResult();
        Assert.All(await Task.WhenAll(all), d => Assert.Equal(4, d.Totals.Styles));
        Assert.Equal(4, loads);
    }

    [Fact]
    public async Task A_dashboard_read_during_a_change_is_not_kept()
    {
        var cache = new StyleDashboardCache(new Clock());
        var loads = 0;
        var during = await cache.GetAsync(_ =>
        {
            cache.Invalidate();                                                    // a save lands while the dashboard is read
            return Task.FromResult(Dashboard(++loads));
        }, default);
        Assert.Equal(1, during.Totals.Styles);
        Assert.Equal(2, (await cache.GetAsync(_ => Task.FromResult(Dashboard(++loads)), default)).Totals.Styles);   // read again
    }
}
