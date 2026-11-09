using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>Business units per user name; none = may change every style (the database decides Admins).</summary>
public sealed class FakeStyleEditScopeRepository : IStyleEditScopeRepository
{
    public ConcurrentDictionary<string, string[]> Units { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> GetBusinessUnitsAsync(string userName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Units.TryGetValue(userName, out var units) ? units : []);
}

/// <summary>Business unit teams: users with business units change only those units' styles; everyone reads everything.</summary>
public sealed class BusinessUnitTeamsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<(HttpClient Client, UserProfileDto User)> SignInAsync(params string[] roles)
    {
        var name = "but" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, login.User);
    }

    [Fact]
    public void The_scope_lets_a_team_change_only_its_business_units()
    {
        var team = new StyleEditScope(["FTB", "ORA"]);
        Assert.True(team.Limited);
        Assert.True(team.CanEdit("ftb", "someone", "me"));
        Assert.False(team.CanEdit("DRL1", "me", "me"));
        // A style without a business unit: only the person who created it.
        Assert.True(team.CanEdit(null, "me", "me"));
        Assert.False(team.CanEdit(null, "someone", "me"));

        Assert.False(StyleEditScope.Unlimited.Limited);
        Assert.True(StyleEditScope.Unlimited.CanEdit("DRL1", "someone", "me"));
    }

    [Fact]
    public async Task A_team_member_sees_other_units_styles_as_view_only()
    {
        var (designer, user) = await SignInAsync("Designer");
        factory.EditScope.Units[user.UserName] = ["FTB"];

        // The fake style has no business unit and someone else created it: view only.
        var style = await designer.GetFromJsonAsync<StyleDetailDto>("/api/v1/styles/5");
        Assert.False(style!.CanEdit);

        var (merchandiser, _) = await SignInAsync("Merchandiser");   // no business units: changes every style
        Assert.True((await merchandiser.GetFromJsonAsync<StyleDetailDto>("/api/v1/styles/5"))!.CanEdit);
    }

    [Fact]
    public async Task The_business_unit_pick_list_narrows_to_the_team_but_filters_keep_every_unit()
    {
        var (designer, user) = await SignInAsync("Designer");
        factory.EditScope.Units[user.UserName] = ["FTB", "ORA"];
        var lookups = await designer.GetFromJsonAsync<StyleLookupsDto>("/api/v1/styles/lookups");
        Assert.Equal(["FTB", "ORA"], lookups!.MyBusinessUnits);

        var (merchandiser, _) = await SignInAsync("Merchandiser");
        Assert.Null((await merchandiser.GetFromJsonAsync<StyleLookupsDto>("/api/v1/styles/lookups"))!.MyBusinessUnits);
    }

    [Fact]
    public void A_users_business_units_are_checked()
    {
        Assert.Empty(AdminValidation.Access(new SetUserAccessRequest([], null, null, null, ["FTB", "ORA"])));
        Assert.Empty(AdminValidation.Access(new SetUserAccessRequest([], null, null, null, [])));   // none = every style
        Assert.Contains("businessUnits", AdminValidation.Access(new SetUserAccessRequest([], null, null, null, ["A,B"])).Keys);
        Assert.Contains("businessUnits", AdminValidation.Access(new SetUserAccessRequest([], null, null, null, [new string('X', 17)])).Keys);
    }

    [Fact]
    public async Task Edit_access_saves_the_users_business_units()
    {
        var (admin, _) = await SignInAsync("Admin");
        var response = await admin.PutAsJsonAsync("/api/v1/admin/users/42/access",
            new { roles = new[] { "Designer" }, displayName = "Jande Doe", businessUnits = new[] { "FTB" } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var saved = factory.Access.AccessChanges.Last(c => c.UserId == 42).Request;
        Assert.Equal(["FTB"], saved.BusinessUnits);
        Assert.Equal("Jande Doe", saved.DisplayName);
    }
}
