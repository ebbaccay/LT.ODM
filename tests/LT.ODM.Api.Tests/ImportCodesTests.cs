using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class ImportCodesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "imc" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Admins_see_new_codes_and_map_them_to_existing_codes()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Merchandiser")).GetAsync("/api/v1/style-library/imports/5/codes")).StatusCode);

        var admin = await SignInAsync("Admin");
        var codes = await admin.GetFromJsonAsync<ImportNewCodesDto>("/api/v1/style-library/imports/5/codes");
        Assert.Equal("yd", codes!.Codes.Single(c => c.Value == "yds").SuggestedCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/5/codes/map", new { list = "colour", value = "x", code = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/5/codes/map", new { list = "uom", value = "pcs", code = "nonsense" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/5/codes/map", new { list = "uom", value = "yds", code = " yd " })).StatusCode);
        Assert.Contains(factory.StyleImports.Maps, m => m is { BatchId: 5, Request: { List: "uom", Value: "yds", Code: "yd" } });
    }

    [Fact]
    public async Task Ai_suggestions_only_use_codes_on_the_list_and_only_for_unmatched_values()
    {
        var text = new FakeAiJsonClient();
        text.Answers["import code suggestions"] = """
            { "suggestions": [
                { "list": "gender", "value": "MAN", "code": "male", "reason": "MAN means men's." },
                { "list": "businessUnit", "value": "NEWBU", "code": "RUNNING", "reason": "Invented code." },
                { "list": "uom", "value": "yds", "code": "yd", "reason": "Already matched by the rules; not asked." }
            ] }
            """;
        var result = await new ImportCodeAdvisor(text).SuggestAsync(FakeStyleImportRepository.NewCodes);

        Assert.Equal(2, result.Count);
        Assert.Equal("MALE", result.Single(r => r.List == "gender").Code);          // the list's own spelling
        Assert.Null(result.Single(r => r.List == "businessUnit").Code);             // not on the list: keep as new
        Assert.DoesNotContain("yds", text.Calls.Single().UserText);                 // rule-matched values are not sent
    }
}
