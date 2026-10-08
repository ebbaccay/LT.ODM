using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.Translations;
using LT.ODM.Infrastructure.Translations;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class TranslationsTests : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private const string Api = "/api/v1/translations";
    private readonly ApiFactory factory;

    public TranslationsTests(ApiFactory factory)
    {
        this.factory = factory;
        // Every test starts from the same deployed files and no corrections.
        Directory.CreateDirectory(factory.TranslationsBase);
        WriteBase("en", """{ "common": { "save": "Save", "hello": "Hello, {{name}}." }, "nav": { "home": "Home" } }""");
        WriteBase("zh-Hans", """{ "common": { "save": "保存", "hello": "你好，{{name}}。" }, "nav": { "home": "首页" } }""");
        var overrides = Path.Combine(factory.FilesRoot, "i18n");
        if (Directory.Exists(overrides)) Directory.Delete(overrides, recursive: true);
    }

    private void WriteBase(string lang, string json)
    {
        var path = Path.Combine(factory.TranslationsBase, lang + ".json");
        System.IO.File.WriteAllText(path, json);
        System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(Random.Shared.Next(1, 100_000)));
    }

    private async Task<HttpClient> SignInAsync(params string[] roles)
    {
        var name = "tr" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static byte[] Workbook(params (string Key, string? En, string? Zh)[] rows)
        => new TranslationWorkbook().Write(new TranslationSheet(
            [new("en", "English"), new("zh-Hans", "Chinese (Simplified)")],
            rows.Select((r, i) => new TranslationSheetRow(i + 2, r.Key, new Dictionary<string, string?> { ["en"] = r.En, ["zh-Hans"] = r.Zh }, [])).ToList()));

    private static MultipartFormDataContent File(byte[] content, string name = "translations.xlsx")
        => new() { { new ByteArrayContent(content), "file", name } };

    private static Task<HttpResponseMessage> Set(HttpClient client, string lang, string key, string? value)
        => client.PutAsJsonAsync($"{Api}/entries", new { lang, key, value });

    [Fact]
    public async Task Corrections_are_public_and_the_rest_is_for_admins()
    {
        var anonymous = factory.CreateHttpsClient();
        var overrides = await anonymous.GetAsync($"{Api}/zh-Hans/overrides");
        Assert.Equal(HttpStatusCode.OK, overrides.StatusCode);
        Assert.Empty((await overrides.Content.ReadFromJsonAsync<Dictionary<string, string>>())!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Api)).StatusCode);

        var merchandiser = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.Forbidden, (await merchandiser.GetAsync(Api)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Set(merchandiser, "zh-Hans", "common.save", "存")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await merchandiser.PostAsync($"{Api}/import", File(Workbook()))).StatusCode);

        var admin = await SignInAsync("Admin");
        var table = await admin.GetFromJsonAsync<TranslationTableDto>(Api);
        Assert.Equal(["en", "zh-Hans"], table!.Languages.Select(l => l.Code));
        Assert.Equal(["common.save", "common.hello", "nav.home"], table.Rows.Select(r => r.Key));
        Assert.Equal("保存", table.Rows[0].Base["zh-Hans"]);
    }

    [Fact]
    public async Task An_edit_is_served_to_the_app_until_it_is_reset()
    {
        var admin = await SignInAsync("Admin");
        var saved = await Set(admin, "zh-Hans", "common.save", "存储");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("存储", (await saved.Content.ReadFromJsonAsync<TranslationRowDto>())!.Overrides["zh-Hans"]);

        var anonymous = factory.CreateHttpsClient();
        Assert.Equal("存储", (await anonymous.GetFromJsonAsync<Dictionary<string, string>>($"{Api}/zh-Hans/overrides"))!["common.save"]);
        Assert.Empty((await anonymous.GetFromJsonAsync<Dictionary<string, string>>($"{Api}/en/overrides"))!);

        // Typing the deployed text back (stray spaces too) removes the correction.
        Assert.Equal(HttpStatusCode.OK, (await Set(admin, "zh-Hans", "common.save", " 保存 ")).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<Dictionary<string, string>>($"{Api}/zh-Hans/overrides"))!);
    }

    [Fact]
    public async Task An_edit_must_keep_the_placeholders_and_use_a_known_key()
    {
        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(admin, "zh-Hans", "common.hello", "你好！")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(admin, "zh-Hans", "common.hello", "你好，{{user}}。")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Set(admin, "zh-Hans", "common.hello", "{{name}}，你好。")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Set(admin, "zh-Hans", "common.nope", "x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(admin, "fr", "common.save", "x")).StatusCode);
    }

    [Fact]
    public async Task Export_lists_every_key_with_the_texts_in_use()
    {
        var admin = await SignInAsync("Admin");
        await Set(admin, "zh-Hans", "nav.home", "主页");
        var response = await admin.GetAsync($"{Api}/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);

        var read = new TranslationWorkbook().Read(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ["en", "zh-Hans"]);
        Assert.Equal(["en", "zh-Hans"], read.Languages);
        Assert.Equal(["common.save", "common.hello", "nav.home"], read.Rows.Select(r => r.Key));
        Assert.Equal("主页", read.Rows[2].Values["zh-Hans"]);
        Assert.Equal("Hello, {{name}}.", read.Rows[1].Values["en"]);
    }

    [Fact]
    public async Task Upload_lists_the_changes_and_saves_them_only_when_applied()
    {
        var admin = await SignInAsync("Admin");
        await Set(admin, "zh-Hans", "nav.home", "主页");
        var xlsx = Workbook(
            ("common.save", "Save", "存储"),          // zh corrected
            ("common.hello", "Hi, {{name}}!", "你好"), // en corrected; zh loses the placeholder -> error
            ("nav.home", "Home", null),                 // zh cleared -> back to the deployed text
            ("nav.home", "Start", null),                // duplicate -> error
            ("old.key", "Old", "旧"));                   // not used by the app -> warning

        var preview = await (await admin.PostAsync($"{Api}/import", File(xlsx))).Content.ReadFromJsonAsync<TranslationImportResultDto>();
        Assert.False(preview!.Applied);
        Assert.Equal(5, preview.Rows);
        Assert.Equal(3, preview.Changes.Count);
        Assert.Contains(preview.Changes, c => c is { Key: "common.save", Lang: "zh-Hans", From: "保存", To: "存储", Revert: false });
        Assert.Contains(preview.Changes, c => c is { Key: "common.hello", Lang: "en", To: "Hi, {{name}}!" });
        Assert.Contains(preview.Changes, c => c is { Key: "nav.home", Lang: "zh-Hans", From: "主页", To: "首页", Revert: true });
        Assert.Equal(2, preview.Problems.Count(p => p.Severity == "Error"));
        Assert.Single(preview.Problems, p => p is { Severity: "Warning", Key: "old.key" });
        var anonymous = factory.CreateHttpsClient();
        Assert.Equal("主页", (await anonymous.GetFromJsonAsync<Dictionary<string, string>>($"{Api}/zh-Hans/overrides"))!["nav.home"]);

        var applied = await (await admin.PostAsync($"{Api}/import?apply=true", File(xlsx))).Content.ReadFromJsonAsync<TranslationImportResultDto>();
        Assert.True(applied!.Applied);
        Assert.Equal(3, applied.Changes.Count);
        Assert.Equal(new Dictionary<string, string> { ["common.save"] = "存储" }, await anonymous.GetFromJsonAsync<Dictionary<string, string>>($"{Api}/zh-Hans/overrides"));
        Assert.Equal(new Dictionary<string, string> { ["common.hello"] = "Hi, {{name}}!" }, await anonymous.GetFromJsonAsync<Dictionary<string, string>>($"{Api}/en/overrides"));

        // The same file again changes nothing.
        var again = await (await admin.PostAsync($"{Api}/import?apply=true", File(xlsx))).Content.ReadFromJsonAsync<TranslationImportResultDto>();
        Assert.False(again!.Applied);
        Assert.Empty(again.Changes);
    }

    [Fact]
    public async Task Upload_rejects_files_that_are_not_the_translations_workbook()
    {
        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{Api}/import", File("not excel"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{Api}/import", File(Workbook(), "translations.csv"))).StatusCode);

        var noLanguages = new TranslationWorkbook().Write(new TranslationSheet([new("fr", "French")],
            [new TranslationSheetRow(2, "common.save", new Dictionary<string, string?> { ["fr"] = "Enregistrer" }, [])]));
        var response = await admin.PostAsync($"{Api}/import", File(noLanguages));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no language column", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_release_that_rewords_a_corrected_text_is_flagged_and_new_keys_come_through()
    {
        var admin = await SignInAsync("Admin");
        await Set(admin, "zh-Hans", "nav.home", "主页");
        WriteBase("zh-Hans", """{ "common": { "save": "保存", "hello": "你好，{{name}}。" }, "nav": { "home": "首页面", "new": "新的" } }""");

        var table = await admin.GetFromJsonAsync<TranslationTableDto>(Api);
        var home = table!.Rows.Single(r => r.Key == "nav.home");
        Assert.Equal("主页", home.Overrides["zh-Hans"]);
        Assert.Equal(["zh-Hans"], home.BaseChanged);
        Assert.Equal("新的", table.Rows.Single(r => r.Key == "nav.new").Base["zh-Hans"]);
    }

    [Fact]
    public async Task An_earlier_version_can_be_restored()
    {
        var admin = await SignInAsync("Admin");
        await Set(admin, "zh-Hans", "common.save", "存储");
        await Set(admin, "zh-Hans", "nav.home", "主页");

        var history = await admin.GetFromJsonAsync<List<TranslationVersionDto>>($"{Api}/history");
        Assert.Equal(2, history!.Count);
        Assert.True(history[0].Current);
        Assert.Equal(2, history[0].Overrides);
        Assert.Equal(1, history[1].Overrides);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Api}/history/{history[1].Id}/restore", null)).StatusCode);
        var anonymous = factory.CreateHttpsClient();
        Assert.Equal(new Dictionary<string, string> { ["common.save"] = "存储" }, await anonymous.GetFromJsonAsync<Dictionary<string, string>>($"{Api}/zh-Hans/overrides"));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Api}/history/..%2Foverrides/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Api}/history/20000101T000000000Z/restore", null)).StatusCode);
    }
}
