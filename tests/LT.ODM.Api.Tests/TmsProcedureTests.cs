using System.Net;
using System.Security.Claims;
using System.Text.Json;
using LT.ODM.Infrastructure.Tms;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class TmsProcedureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static ClaimsPrincipal User(string userName, params string[] roles)
        => new(new ClaimsIdentity(
            [new Claim("preferred_username", userName), .. roles.Select(r => new Claim("role", r))],
            "test", "preferred_username", "role"));

    private TmsProcedureCatalog Catalog => factory.Services.GetRequiredService<TmsProcedureCatalog>();

    [Fact]
    public void Allowlist_is_loaded_from_tms_procedures_json()
        => Assert.True(Catalog.Count > 50);

    [Theory]
    [InlineData("web_rd_gq_dashboard_summary")]
    [InlineData("iplex_srcdb01.dbo.web_rd_gq_dashboard_summary")]   // TMS clients prefix the database
    [InlineData("[dbo].[web_rd_gq_dashboard_summary]")]
    public void Allowlisted_procedure_resolves_to_the_configured_schema_whatever_prefix_the_client_sends(string requested)
    {
        var proc = Catalog.Resolve(requested, User("jdoe"));
        Assert.NotNull(proc);
        Assert.Equal("[dbo].[web_rd_gq_dashboard_summary]", proc.QualifiedName);
    }

    [Theory]
    [InlineData("web_rd_not_in_allowlist")]
    [InlineData("sp_configure")]
    [InlineData("xp_cmdshell")]
    [InlineData("web_rd_gq_dashboard_summary; DROP TABLE x")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_not_allowlisted_is_refused(string? requested)
        => Assert.Null(Catalog.Resolve(requested, User("jdoe")));

    [Fact]
    public void Identity_parameters_come_from_the_token_not_the_client()
    {
        var proc = Catalog.Resolve("web_rd_gq_dashboard_summary", User("jdoe"))!;
        using var json = JsonDocument.Parse("""{ "username": "someone.else", "stale_days": 3, "season_id": null }""");

        var bound = TmsParameterBinder.Bind(proc, ["@username", "@stale_days", "@season_id"], json.RootElement, User("jdoe"));

        Assert.Equal("jdoe", bound.Single(p => p.ParameterName == "@username").Value);
        // Same as TMS: JSON numbers are sent as decimal (C# ternary int/decimal); SQL Server converts for int parameters.
        Assert.Equal(3m, bound.Single(p => p.ParameterName == "@stale_days").Value);
        Assert.Equal(DBNull.Value, bound.Single(p => p.ParameterName == "@season_id").Value);
    }

    [Fact]
    public void Parameters_the_procedure_does_not_declare_are_refused()
    {
        var proc = Catalog.Resolve("web_rd_gq_dashboard_summary", User("jdoe"))!;
        using var json = JsonDocument.Parse("""{ "stale_days": 3, "extra": "x" }""");

        var ex = Assert.Throws<TmsRequestException>(() => TmsParameterBinder.Bind(proc, ["@stale_days"], json.RootElement, User("jdoe")));
        Assert.Contains("@extra", ex.Message);
    }

    [Fact]
    public async Task Hub_requires_sign_in()
    {
        var response = await factory.CreateHttpsClient().PostAsync("/hubs/sp/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class TmsNotificationRulesTests
{
    [Theory]
    [InlineData(null, null, "TMS")]
    [InlineData("TMS", null, "TMS")]
    [InlineData("FTY", "F001", "FTY_F001")]
    [InlineData("fty", "F002", "FTY_F002")]
    [InlineData("FTY", "", null)]
    public void Group_comes_from_user_group_and_location(string? userGroup, string? location, string? expected)
        => Assert.Equal(expected, TmsNotificationRules.GroupFor(userGroup, location));

    [Theory]
    [InlineData("TMS", "FTY_F001", "F001", true)]    // merchandiser -> any factory
    [InlineData("TMS", "TMS", "F001", true)]
    [InlineData("TMS", "FTY_", "", false)]
    [InlineData("FTY_F001", "TMS", "F001", true)]   // factory -> merchandisers, own factory only
    [InlineData("FTY_F001", "TMS", "F002", false)]
    [InlineData("FTY_F001", "FTY_F002", "F001", false)] // factories cannot message each other
    public void Recipients_are_limited_by_the_sender_group(string sender, string recipient, string factoryId, bool allowed)
        => Assert.Equal(allowed, TmsNotificationRules.CanSend(sender, recipient, factoryId));
}
