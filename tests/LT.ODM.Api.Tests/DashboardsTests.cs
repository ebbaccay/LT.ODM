using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Auth;
using LT.ODM.Application.Dashboards;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakeDesignerDashboardRepository : IDesignerDashboardRepository
{
    public (string UserName, string? Season)? LastCall { get; private set; }

    public DesignerDashboardData Data { get; set; } = new([new DesignerSeasonDto("2027-SS", 2)], [], [], []);

    public Task<DesignerDashboardData> GetAsync(string userName, string? season, CancellationToken ct = default)
    {
        LastCall = (userName, season);
        return Task.FromResult(Data);
    }
}

public sealed class DashboardsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private static readonly DateTime At = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

    private async Task<(HttpClient Client, UserProfileDto User)> SignInAsync(params string[] roles)
    {
        var name = "dsh" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, login.User);
    }

    /// <summary>A style with everything a designer fills in, unless changed with <c>with</c>.</summary>
    private static DesignerStyleFacts Complete(int id = 1) => new(id, $"S{id}", "Jacket", "Model", "ADI", "2027-SS", null, "/sketch.png",
        "JACKET", "FEMALE", null, Colorways: 3, ColorwaysNotOnBom: 0, BomLines: 12, LinesNoColour: 0, LinesNoDetails: 0, LinesNoLco: 0,
        LinesNoSupplier: 0, Packages: 0, Outcome: null, At, At);

    // ----- Completeness checks -----

    [Fact]
    public void A_complete_style_scores_100_and_misses_nothing()
    {
        var (score, missing) = DesignerChecks.Evaluate(Complete());
        Assert.Equal(100, score);
        Assert.Empty(missing);
    }

    [Fact]
    public void A_new_style_lists_only_what_to_do_next_but_scores_every_check()
    {
        var bare = Complete() with { SketchUrl = null, ProductTypeCode = null, Gender = null, Colorways = 0, BomLines = 0 };
        var (score, missing) = DesignerChecks.Evaluate(bare);
        Assert.Equal(0, score);
        Assert.Equal([DesignerChecks.Picture, DesignerChecks.ProductType, DesignerChecks.Gender, DesignerChecks.Colorways, DesignerChecks.Bom],
            missing.Select(m => m.Check));
    }

    [Fact]
    public void Bom_line_gaps_carry_how_many_lines_or_colorways_are_affected()
    {
        var style = Complete() with { ColorwaysNotOnBom = 1, LinesNoColour = 2, LinesNoLco = 5, LinesNoSupplier = 1 };
        var (score, missing) = DesignerChecks.Evaluate(style);
        Assert.Equal(60, score);
        Assert.Equal(1, missing.Single(m => m.Check == DesignerChecks.ColorwaysOnBom).Count);
        Assert.Equal(2, missing.Single(m => m.Check == DesignerChecks.BomColours).Count);
        Assert.Equal(5, missing.Single(m => m.Check == DesignerChecks.Consumption).Count);
        Assert.Equal(1, missing.Single(m => m.Check == DesignerChecks.Suppliers).Count);
        Assert.DoesNotContain(missing, m => m.Check == DesignerChecks.BomDetails);
    }

    [Fact]
    public void Without_colorways_the_colour_checks_fail_but_only_colorways_is_listed()
    {
        var (score, missing) = DesignerChecks.Evaluate(Complete() with { Colorways = 0 });
        Assert.Equal(70, score);
        Assert.Equal(DesignerChecks.Colorways, Assert.Single(missing).Check);
    }

    [Fact]
    public void The_summary_counts_complete_styles_gaps_and_outcomes()
    {
        var data = new DesignerDashboardData([], [
            Complete(1) with { Packages = 1, Outcome = "Ordered" },
            Complete(2) with { Gender = null, Packages = 2, Outcome = "Sampling" },
            Complete(3) with { Gender = null, LinesNoLco = 4 },
        ], [], []);
        var dto = DesignerDashboard.Build("2027-SS", data);
        Assert.Equal(3, dto.Totals.Styles);
        Assert.Equal(1, dto.Totals.Complete);
        Assert.Equal((100 + 90 + 80) / 3, dto.Totals.AverageScore);
        Assert.Equal(2, dto.Totals.InPackages);
        Assert.Equal(1, dto.Totals.Sampling);
        Assert.Equal(1, dto.Totals.Ordered);
        Assert.Equal(2, dto.Checks.Single(c => c.Check == DesignerChecks.Gender).Styles);
        Assert.Equal(1, dto.Checks.Single(c => c.Check == DesignerChecks.Consumption).Styles);
        Assert.Equal(DesignerChecks.All, dto.Checks.Select(c => c.Check));
    }

    // ----- Which dashboard a user opens with -----

    private static readonly RoleDto[] Roles =
    [
        new(1, "Admin", null, null, 1),
        new(2, "Merchandiser", null, null, 1),
        new(3, "Designer", null, null, 1, "designer"),
        new(4, "Factory", null, null, 1),
        new(5, "Viewer", null, null, 1, "retired-dashboard"),
    ];

    private static DashboardChoiceDto Resolve(params string[] mine)
        => DashboardChoice.Resolve(r => mine.Contains(r, StringComparer.OrdinalIgnoreCase), Roles);

    [Fact]
    public void Roles_pick_the_home_dashboard()
    {
        Assert.Equal(DashboardKeys.Designer, Resolve("Designer").DefaultKey);
        Assert.Equal(DashboardKeys.Library, Resolve("Merchandiser").DefaultKey);
        // A role that chose a dashboard wins over one that kept the default.
        Assert.Equal(DashboardKeys.Designer, Resolve("Admin", "Designer").DefaultKey);
        // Unknown keys (a dashboard removed in a later release) fall back to the overview.
        Assert.Equal(DashboardKeys.Library, Resolve("Viewer").DefaultKey);
    }

    [Fact]
    public void Only_admins_may_switch_dashboards()
    {
        Assert.Equal([DashboardKeys.Designer], Resolve("Designer").Available);
        Assert.Equal([DashboardKeys.Library], Resolve("Merchandiser").Available);
        Assert.Equal(DashboardKeys.All, Resolve("Admin").Available);
        Assert.Equal(DashboardKeys.All, Resolve("Admin", "Designer").Available);
    }

    [Fact]
    public void Users_who_cannot_read_the_library_get_no_dashboard()
    {
        var choice = Resolve("Factory");
        Assert.Null(choice.DefaultKey);
        Assert.Empty(choice.Available);
    }

    [Fact]
    public void Roles_accept_only_known_dashboards()
    {
        Assert.Empty(AdminValidation.Role(new SaveRoleRequest(0, "Designer", null, null, "designer")));
        Assert.Empty(AdminValidation.Role(new SaveRoleRequest(0, "Designer", null, null, "")));
        Assert.Empty(AdminValidation.Role(new SaveRoleRequest(0, "Designer", null, null, "library")));
        Assert.Contains("dashboardKey", AdminValidation.Role(new SaveRoleRequest(0, "Designer", null, null, "sales")).Keys);
        Assert.Null(DashboardKeys.Normalize(" Library "));
        Assert.Equal("designer", DashboardKeys.Normalize("Designer"));
    }

    // ----- API -----

    [Fact]
    public async Task Mine_reads_the_dashboard_of_the_users_roles()
    {
        if (!factory.Access.Roles.Any(r => r.Name == "Designer"))
            factory.Access.Roles.Add(new RoleDto(3, "Designer", "Designer", null, 0, "designer"));

        var (designer, _) = await SignInAsync("Designer");
        var choice = await designer.GetFromJsonAsync<DashboardChoiceDto>("/api/v1/dashboards/mine");
        Assert.Equal("designer", choice!.DefaultKey);
        Assert.Equal(["designer"], choice.Available);

        var (admin, _) = await SignInAsync("Admin");
        choice = await admin.GetFromJsonAsync<DashboardChoiceDto>("/api/v1/dashboards/mine");
        Assert.Equal("library", choice!.DefaultKey);
        Assert.Equal(DashboardKeys.All, choice.Available);

        var (factoryUser, _) = await SignInAsync("Factory");
        choice = await factoryUser.GetFromJsonAsync<DashboardChoiceDto>("/api/v1/dashboards/mine");
        Assert.Null(choice!.DefaultKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync("/api/v1/dashboards/mine")).StatusCode);
    }

    [Fact]
    public async Task Designer_dashboard_shows_the_signed_in_users_own_styles()
    {
        factory.DesignerDashboard.Data = new DesignerDashboardData([new DesignerSeasonDto("2027-SS", 1)],
            [Complete(7) with { LinesNoLco = 3 }], [], []);
        var (designer, user) = await SignInAsync("Designer");

        var dto = await designer.GetFromJsonAsync<DesignerDashboardDto>("/api/v1/dashboards/designer?season=2027-SS");
        Assert.Equal((user.UserName, "2027-SS"), factory.DesignerDashboard.LastCall);
        var style = Assert.Single(dto!.Styles);
        Assert.Equal(90, style.Score);
        Assert.Equal(DesignerChecks.Consumption, Assert.Single(style.Missing).Check);

        await designer.GetAsync("/api/v1/dashboards/designer?season=%20");
        Assert.Null(factory.DesignerDashboard.LastCall!.Value.Season);
        Assert.Equal(HttpStatusCode.BadRequest, (await designer.GetAsync("/api/v1/dashboards/designer?season=" + new string('X', 17))).StatusCode);

        var (factoryUser, _) = await SignInAsync("Factory");
        Assert.Equal(HttpStatusCode.Forbidden, (await factoryUser.GetAsync("/api/v1/dashboards/designer")).StatusCode);
    }

    [Fact]
    public async Task Designers_read_and_change_the_style_library()
    {
        var (designer, _) = await SignInAsync("Designer");
        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync("/api/v1/styles")).StatusCode);
        var created = await designer.PostAsJsonAsync("/api/v1/styles",
            new { customerCode = "ADI", seasonCode = "2027-SS", styleNo = "D1_SS27", modelName = "Model", gender = "FEMALE", businessUnitCode = "FTB" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }
}
