using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakeStyleRepository : IStyleRepository
{
    public static readonly byte[] CurrentRowVer = [0, 0, 0, 0, 0, 0, 0, 7];

    public List<(string Action, int? Id, object? Request, string ChangedBy)> Writes { get; } = [];

    public Task<StyleLookupsDto> GetLookupsAsync(CancellationToken ct = default)
        => Task.FromResult(new StyleLookupsDto([new LookupItem("ADI", "adidas")], [], [], [], [], [], [], [], [], []));

    public StyleListQuery? LastListQuery { get; private set; }

    public Task<PagedResult<StyleListItemDto>> ListAsync(StyleListQuery query, CancellationToken ct = default)
    {
        LastListQuery = query;
        return Task.FromResult(new PagedResult<StyleListItemDto>([], 0));
    }

    public Task<StyleDetailDto?> GetAsync(int styleId, CancellationToken ct = default)
        => Task.FromResult<StyleDetailDto?>(styleId == 404 ? null : new StyleDetailDto(
            new StyleHeaderDto(styleId, "ADI", "adidas", "2027-SS", "S1_SS27", "S1", null, "M", "Model", null, null, null, null, null, null, null, null,
                null, null, null, null, "x", DateTime.UtcNow, null, null, CurrentRowVer), [], [], []));

    public Task<StyleDashboardDto> GetDashboardAsync(CancellationToken ct = default)
        => Task.FromResult(new StyleDashboardDto(new StyleDashboardTotalsDto(3, 5, 4, 2, 20, 9, 2, 1, 2, 1, 1, 1, 2, 3),
            [new DashboardSeasonDto("2027-SS", 3, 5, 20)], [new DashboardCountDto("ADI", "adidas", 3, 5, null)], [], [], [], [], [], null));

    private Task<int> Write(string action, int? id, object? request, string changedBy, int result = 1)
    {
        Writes.Add((action, id, request, changedBy));
        return Task.FromResult(result);
    }

    public Task<int> SaveStyleAsync(int? styleId, SaveStyleRequest request, string changedBy, CancellationToken ct = default)
    {
        if (request.StyleNo == "taken") throw new StyleRuleException(409, "A style with this number already exists for this customer and season.");
        if (styleId is not null && !request.RowVer!.SequenceEqual(CurrentRowVer))
            throw new StyleRuleException(409, "Someone else changed this style after you opened it. Reload and try again.");
        return Write("style", styleId, request, changedBy, styleId ?? 42);
    }

    public Task DeleteStyleAsync(int styleId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Write("delete-style", styleId, rowVer, changedBy);

    public Task<int> CopyStyleAsync(int styleId, CopyStyleRequest request, string changedBy, CancellationToken ct = default)
        => Write("copy", styleId, request, changedBy, 43);

    public Task<int> SaveColorwayAsync(int styleId, int? colorwayId, SaveColorwayRequest request, string changedBy, CancellationToken ct = default)
        => Write("colorway", colorwayId, request, changedBy, 7);

    public Task DeleteColorwayAsync(int colorwayId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Write("delete-colorway", colorwayId, rowVer, changedBy);

    public Task<int> SaveBomLineAsync(int styleId, int? bomLineId, SaveBomLineRequest request, string changedBy, CancellationToken ct = default)
        => Write("bom-line", bomLineId, request, changedBy, 9);

    public Task DeleteBomLineAsync(int bomLineId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Write("delete-bom-line", bomLineId, rowVer, changedBy);

    public Task SetHistoryAsync(int styleId, SetStyleHistoryRequest request, string changedBy, CancellationToken ct = default)
        => request.SourceStyleId == 999
            ? throw new StyleRuleException(400, "That link would make a loop: the other style already comes from this one.")
            : Write("history", styleId, request, changedBy);

    public Task RemoveHistoryAsync(int styleId, string changedBy, CancellationToken ct = default) => Write("remove-history", styleId, null, changedBy);
}

public sealed class StylesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private static readonly string RowVer = Convert.ToBase64String(FakeStyleRepository.CurrentRowVer);

    private async Task<(HttpClient Client, UserProfileDto User)> SignInAsync(params string[] roles)
    {
        var name = "sty" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, login.User);
    }

    private static object Style(string styleNo = "S1_SS27", string? rowVer = null, string season = "2027-SS", string? gender = "FEMALE")
        => new { rowVer, customerCode = "ADI", seasonCode = season, styleNo, modelName = "Model", gender };

    [Fact]
    public async Task Viewers_read_editors_change_factories_do_neither()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/styles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/styles/5")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/styles", Style())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/v1/styles/5?rowVer={Uri.EscapeDataString(RowVer)}")).StatusCode);

        var (factoryUser, _) = await SignInAsync("Factory");
        Assert.Equal(HttpStatusCode.Forbidden, (await factoryUser.GetAsync("/api/v1/styles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factoryUser.GetAsync("/api/v1/styles/dashboard")).StatusCode);

        var (merchandiser, _) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.OK, (await merchandiser.PostAsJsonAsync("/api/v1/styles", Style())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await merchandiser.GetAsync("/api/v1/styles/404")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_summarises_the_library_for_readers()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        var dashboard = await viewer.GetFromJsonAsync<StyleDashboardDto>("/api/v1/styles/dashboard");
        Assert.Equal(3, dashboard!.Totals.Styles);
        Assert.Equal("2027-SS", Assert.Single(dashboard.Seasons).SeasonCode);
        Assert.Null(dashboard.LastImport);
    }

    [Fact]
    public async Task Styles_are_validated_field_by_field()
    {
        var (admin, _) = await SignInAsync("Admin");
        var bad = await admin.PostAsJsonAsync("/api/v1/styles", Style(styleNo: "", season: "SS27", gender: "MEN"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        Assert.Contains("\"styleNo\"", body);
        Assert.Contains("e.g. 2027-SS", body);
        Assert.Contains("\"gender\"", body);

        var image = await admin.PostAsJsonAsync("/api/v1/styles",
            new { customerCode = "ADI", seasonCode = "2027-SS", styleNo = "S2", imageUrl = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, image.StatusCode);
        Assert.Contains("\"imageUrl\"", await image.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Changes_are_recorded_under_the_signed_in_user()
    {
        var (admin, me) = await SignInAsync("Admin");
        var created = await admin.PostAsJsonAsync("/api/v1/styles", Style());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("\"styleId\":42", await created.Content.ReadAsStringAsync());
        Assert.Equal(me.UserName, factory.Styles.Writes.Last().ChangedBy);

        var line = await admin.PostAsJsonAsync("/api/v1/styles/5/bom-lines", new
        {
            materialCode = "STSTRFQ_CAMB", contentClassCode = "LNP", lcoConsumption = 1.5, uomCode = "pc",
            colorways = new[] { new { colorwayId = 1, materialColorCode = "0" }, new { colorwayId = 2, materialColorCode = "0" } },
        });
        Assert.Equal(HttpStatusCode.OK, line.StatusCode);
        var saved = (SaveBomLineRequest)factory.Styles.Writes.Last().Request!;
        Assert.Equal(2, saved.Colorways!.Count);
        Assert.Equal(me.UserName, factory.Styles.Writes.Last().ChangedBy);
    }

    [Fact]
    public async Task Edits_and_deletes_need_the_row_version_and_conflicts_come_back_as_409()
    {
        var (admin, _) = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/styles/5", Style())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/v1/styles/5")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/v1/styles/5/colorways/3?rowVer=not-base64")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/v1/styles/5", Style(rowVer: RowVer))).StatusCode);
        var stale = await admin.PutAsJsonAsync("/api/v1/styles/5", Style(rowVer: Convert.ToBase64String(new byte[8])));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Someone else changed this style", await stale.Content.ReadAsStringAsync());

        var duplicate = await admin.PostAsJsonAsync("/api/v1/styles", Style(styleNo: "taken"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/styles/5/bom-lines/9?rowVer={Uri.EscapeDataString(RowVer)}")).StatusCode);
        Assert.Equal(FakeStyleRepository.CurrentRowVer, (byte[])factory.Styles.Writes.Last().Request!);
    }

    [Fact]
    public async Task History_links_and_copies_are_checked()
    {
        var (admin, _) = await SignInAsync("Admin");
        var badRelation = await admin.PutAsJsonAsync("/api/v1/styles/5/history", new { sourceStyleId = 3, relation = "Sibling" });
        Assert.Equal(HttpStatusCode.BadRequest, badRelation.StatusCode);
        var loop = await admin.PutAsJsonAsync("/api/v1/styles/5/history", new { sourceStyleId = 999, relation = "CarryOver" });
        Assert.Equal(HttpStatusCode.BadRequest, loop.StatusCode);
        Assert.Contains("loop", await loop.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/styles/5/history", new { sourceStyleId = 3, relation = "Variant" })).StatusCode);

        var copy = await admin.PostAsJsonAsync("/api/v1/styles/5/copy", new { seasonCode = "2028-SS", styleNo = "S1_SS28" });
        Assert.Equal(HttpStatusCode.OK, copy.StatusCode);
        Assert.Contains("\"styleId\":43", await copy.Content.ReadAsStringAsync());
    }
}
