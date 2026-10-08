using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace LT.ODM.Api.Tests;

/// <summary>The API serves the Angular build (one IIS site): app routes get index.html, API addresses never do.</summary>
public sealed class WebAppHostingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IDisposable
{
    private readonly string _webRoot = CreateWebRoot();

    private static string CreateWebRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ltodm-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "assets", "i18n"));
        File.WriteAllText(Path.Combine(dir, "index.html"), "<!doctype html><title>LT ODM</title>");
        File.WriteAllText(Path.Combine(dir, "main-ABCD1234.js"), "console.log(1)");
        File.WriteAllText(Path.Combine(dir, "ngsw.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "manifest.webmanifest"), "{}");
        File.WriteAllText(Path.Combine(dir, "assets", "i18n", "en.json"), "{}");
        return dir;
    }

    public void Dispose()
    {
        try { Directory.Delete(_webRoot, recursive: true); } catch (IOException) { }
    }

    private HttpClient Client() => factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot))
        .CreateClient(new() { BaseAddress = new Uri("https://localhost") });

    [Theory]
    [InlineData("/")]
    [InlineData("/styles/12")]
    [InlineData("/settings/import")]
    public async Task App_routes_get_index_html_without_sign_in_and_are_never_cached(string path)
    {
        var response = await Client().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("LT ODM", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Hashed_bundles_are_cached_and_service_worker_files_are_rechecked()
    {
        var client = Client();
        var bundle = await client.GetAsync("/main-ABCD1234.js");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        Assert.Contains("immutable", bundle.Headers.CacheControl?.ToString());

        foreach (var path in new[] { "/ngsw.json", "/assets/i18n/en.json" })
            Assert.Equal("no-cache", (await client.GetAsync(path)).Headers.CacheControl?.ToString());

        Assert.Equal("application/manifest+json", (await client.GetAsync("/manifest.webmanifest")).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Missing_files_and_unknown_api_addresses_are_not_the_app_page()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/chunk-MISSING1.js")).StatusCode);
        // Unknown API and hub addresses keep the API's rules: sign-in first, and never index.html.
        foreach (var path in new[] { "/api/v1/nothing-here", "/hubs/nothing" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("LT ODM", await response.Content.ReadAsStringAsync());
        }
        Assert.Contains("no-store", (await client.GetAsync("/health")).Headers.CacheControl?.ToString());   // API responses are still never stored
    }
}
