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

    [Fact]
    public async Task Answers_can_be_given_in_bulk_grouped_and_forgotten()
    {
        var admin = await SignInAsync("Admin");
        var many = await admin.PostAsJsonAsync("/api/v1/style-library/imports/6/codes/map-many", new
        {
            items = new[] { new { list = "supplier", value = "0001100131", code = "1100131" }, new { list = "season", value = "FA26", code = "2026-FA" } },
            remember = false,
        });
        Assert.Equal(HttpStatusCode.OK, many.StatusCode);
        Assert.Contains(factory.StyleImports.Maps, m => m is { BatchId: 6, Request: { List: "season", Value: "FA26", Remember: false } });
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/6/codes/map-many", new { items = Array.Empty<object>() })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/6/codes/group-customers",
            new { values = new[] { "0000000131" }, code = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/6/codes/group-customers",
            new { values = new[] { "0000000131", "0000000187" }, code = " polo ", name = "Polo Ralph Lauren" })).StatusCode);
        var group = factory.StyleImports.Groups.Last();
        Assert.Equal("POLO", group.Request.Code);
        Assert.True(group.Request.Remember);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/codes/forget", new { list = "supplier", value = "0001100131" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/codes/forget", new { list = "supplier", value = "0001100131" })).StatusCode);
    }

    [Fact]
    public void Customer_accounts_are_grouped_by_brand_and_matched_to_existing_customers()
    {
        ImportNewCodeDto Account(string value, string name, int rows = 10) => new("customer", value, name, rows, 1, false, null, null);
        var groups = ImportCustomerGrouping.Build(
            [
                Account("0000001299", "TJ Skechers Women Domestic"), Account("0000001304", "TJ Skechers Mens Export(TOMG)"),
                Account("0000000131", "POLO - FOA Men's Knit"), Account("0000000187", "POLO -PARENT LINE KNIT"),
                Account("0000001006", "DILLARDS - Murano Men's Knit"), Account("0000001000", "DIL - Daniel Cremieux Jeans Men's W"),
                Account("0000001355", "BUCK MASON Knits"), Account("0000001419", "TW Biemlofen"),
            ],
            [new ImportCodeOptionDto("customer", "SKE", "SKE"), new ImportCodeOptionDto("customer", "ADI", "adidas")]);

        var ske = groups[0];
        Assert.Equal("SKE", ske.SuggestedCode);
        Assert.Equal(2, ske.Accounts.Count);
        Assert.Equal(["0000000131", "0000000187"], groups.Single(g => g.Key == "POLO").Accounts.Select(a => a.Value).Order());
        Assert.Equal(2, groups.Single(g => g.Key == "DILLARDS").Accounts.Count);   // DIL and DILLARDS
        Assert.DoesNotContain(groups, g => g.Key is "BUCK" or "BIEMLOFEN");          // one account, no existing customer: not asked
    }

    [Fact]
    public async Task Accounts_can_stay_separate_customers_under_one_brand()
    {
        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/7/codes/brand-customers",
            new { values = new[] { "0000000131" }, brand = "A|B" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/style-library/imports/7/codes/brand-customers",
            new { values = new[] { "0000000131", "0000000187" }, brand = " polo ", name = "Polo Ralph Lauren", alsoCustomer = " " })).StatusCode);
        var brand = factory.StyleImports.Brands.Last().Request;
        Assert.Equal("POLO", brand.Brand);
        Assert.Null(brand.AlsoCustomer);

        // Accounts that already have a brand are not asked about again.
        ImportNewCodeDto Account(string value, string name, string? b = null) => new("customer", value, name, 5, 1, false, null, null, b);
        var groups = ImportCustomerGrouping.Build(
            [Account("0000000131", "POLO - FOA Men's Knit", "POLO"), Account("0000000187", "POLO -PARENT LINE KNIT", "POLO")], []);
        Assert.Empty(groups);
    }
}
