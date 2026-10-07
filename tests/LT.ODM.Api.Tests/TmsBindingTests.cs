using System.Security.Claims;
using System.Text.Json;
using LT.ODM.Infrastructure.Tms;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>Per-procedure bindings in tms-procedures.json (ProcedureBoundParameters).</summary>
public sealed class TmsBindingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private TmsProcedureCatalog Catalog => factory.Services.GetRequiredService<TmsProcedureCatalog>();

    private static ClaimsPrincipal Office(string location = "HKG")
        => new(new ClaimsIdentity([new Claim("preferred_username", "mchan"), new Claim("location", location)], "test", "preferred_username", "role"));

    private static ClaimsPrincipal Factory(string? location = "DG01")
        => new(new ClaimsIdentity(
            [new Claim("preferred_username", "fty.dg01"), new Claim("user_group", "FTY"), .. location is null ? Array.Empty<Claim>() : [new Claim("location", location)]],
            "test", "preferred_username", "role"));

    private List<SqlParameter> Bind(string proc, string json, string[] declared, ClaimsPrincipal user)
    {
        using var doc = JsonDocument.Parse(json);
        return TmsParameterBinder.Bind(Catalog.Resolve(proc, user)!, declared, doc.RootElement, user);
    }

    private static object Value(List<SqlParameter> ps, string name) => ps.Single(p => p.ParameterName == name).Value;

    [Fact]
    public void Factory_users_only_get_their_own_factory_products_whatever_the_browser_sends()
    {
        // Factory user asking for another factory, or for everything.
        Assert.Equal("DG01", Value(Bind("web_rd_get_sbu_products", """{ "location": "SZ02" }""", ["@location"], Factory()), "@location"));
        Assert.Equal("DG01", Value(Bind("web_rd_get_gq_approved_products", "{}", ["@factory_id"], Factory()), "@factory_id"));
        // Factory user without a location sees nothing rather than everything.
        Assert.Equal("", Value(Bind("web_rd_get_sbu_products", "{}", ["@location"], Factory(location: null)), "@location"));
        // Office users are not filtered, even if they have a location or the browser sends one.
        Assert.Equal(DBNull.Value, Value(Bind("web_rd_get_sbu_products", """{ "location": "SZ02" }""", ["@location"], Office()), "@location"));
        Assert.Equal(DBNull.Value, Value(Bind("web_rd_get_gq_approved_products", "{}", ["@factory_id"], Office()), "@factory_id"));
    }

    [Fact]
    public void Submission_insert_takes_location_and_group_from_the_token_but_updates_do_not_overwrite_them()
    {
        string[] declared = ["@concept_recid", "@username", "@location", "@user_group", "@created_by"];
        var insert = Bind("web_rd_ins_sbu_submission", """{ "concept_recid": 1, "location": "SZ02", "user_group": "ADM" }""", declared, Factory());
        Assert.Equal("DG01", Value(insert, "@location"));
        Assert.Equal("FTY", Value(insert, "@user_group"));
        Assert.Equal("fty.dg01", Value(insert, "@created_by"));

        // web_rd_upd_sbu_submission overwrites location when one is passed: it must stay unbound (the screen sends none).
        var update = Bind("web_rd_upd_sbu_submission", """{ "recid": 5, "new_status": "Accepted" }""", ["@recid", "@new_status", "@username", "@location"], Office());
        Assert.DoesNotContain(update, p => p.ParameterName == "@location");
        Assert.Equal("mchan", Value(update, "@username"));
    }

    [Fact]
    public void Factory_proposals_are_recorded_under_the_signed_in_user()
        => Assert.Equal("fty.dg01",
            Value(Bind("web_rd_co_fty_submit_proposal", """{ "recid": 5, "submitted_by": "someone.else" }""", ["@recid", "@submitted_by"], Factory()), "@submitted_by"));

    [Fact]
    public void Collection_items_record_who_added_them_but_keep_the_original_submitter()
    {
        // @username falls back to the submission's author for the item's created_by, so it stays unbound (TMS sent none).
        var add = Bind("web_rd_ins_collection_builder_item", """{ "collection_recid": 3, "submission_recid": 9, "added_by": "someone.else" }""",
            ["@collection_recid", "@submission_recid", "@added_by", "@username", "@modified_by"], Office());
        Assert.Equal("mchan", Value(add, "@added_by"));
        Assert.Equal("mchan", Value(add, "@modified_by"));
        Assert.DoesNotContain(add, p => p.ParameterName == "@username");
    }

    private static ClaimsPrincipal Admin()
        => new(new ClaimsIdentity([new Claim("preferred_username", "boss"), new Claim("role", "Admin")], "test", "preferred_username", "role"));

    [Fact]
    public void Concept_list_team_comes_from_the_token_and_the_screen_only_picks_the_scope()
    {
        // The browser cannot pose as someone else or another team, nor claim to be an Admin.
        string[] declared = ["@username", "@location", "@user_group", "@is_admin", "@scope"];
        var office = Bind("web_rd_get_concept_studio_results",
            """{ "username": "boss", "location": "SZ02", "user_group": "FTY", "is_admin": true, "scope": "all" }""", declared, Office());
        Assert.Equal("mchan", Value(office, "@username"));
        Assert.Equal("HKG", Value(office, "@location"));
        Assert.Equal("", Value(office, "@user_group"));
        Assert.Equal(false, Value(office, "@is_admin"));
        Assert.Equal("all", Value(office, "@scope")); // the procedure downgrades 'all' to 'team' for non-Admins

        Assert.Equal(true, Value(Bind("web_rd_get_concept_studio_results", "{}", declared, Admin()), "@is_admin"));
        Assert.Equal("FTY", Value(Bind("web_rd_get_concept_studio_results", "{}", declared, Factory()), "@user_group"));

        // Writes to concepts still take the user from the token.
        var insert = Bind("web_rd_ins_concept_studio_result", """{ "username": "someone.else" }""", ["@username"], Office());
        Assert.Equal("mchan", Value(insert, "@username"));
    }

    [Fact]
    public void Only_the_token_says_who_may_change_a_concept()
    {
        var update = Bind("web_rd_upd_concept_studio_result", """{ "recid": 5, "is_admin": true, "username": "boss" }""",
            ["@recid", "@username", "@modified_by", "@is_admin"], Office());
        Assert.Equal(false, Value(update, "@is_admin"));
        Assert.Equal("mchan", Value(update, "@username"));

        var delete = Bind("web_rd_del_concept_studio_result", """{ "recid": 5, "modified_by": "boss" }""", ["@recid", "@modified_by", "@is_admin"], Admin());
        Assert.Equal(true, Value(delete, "@is_admin"));
        Assert.Equal("boss", Value(delete, "@modified_by"));
    }

    [Fact]
    public void Sbu_products_keep_their_factory_on_save()
    {
        string[] declared = ["@product_name", "@style_code", "@location", "@user_group", "@created_by", "@username"];
        var insert = Bind("web_rd_ins_sbu_product", """{ "product_name": "Tee", "style_code": "T1", "location": "SZ02", "user_group": "ADM" }""", declared, Factory());
        Assert.Equal("DG01", Value(insert, "@location"));
        Assert.Equal("FTY", Value(insert, "@user_group"));
        Assert.Equal("fty.dg01", Value(insert, "@created_by"));

        // A factory user cannot move a product to another factory; an office edit leaves the factory as it is (NULL = keep).
        string[] updDeclared = ["@recid", "@product_name", "@location", "@user_group", "@username"];
        var fty = Bind("web_rd_upd_sbu_product", """{ "recid": 5, "location": "SZ02", "user_group": "ADM" }""", updDeclared, Factory());
        Assert.Equal("DG01", Value(fty, "@location"));
        Assert.Equal("FTY", Value(fty, "@user_group"));
        var office = Bind("web_rd_upd_sbu_product", """{ "recid": 5, "location": "SZ02" }""", updDeclared, Office());
        Assert.Equal(DBNull.Value, Value(office, "@location"));
        Assert.Equal(DBNull.Value, Value(office, "@user_group"));
        Assert.Equal("mchan", Value(office, "@username"));
    }

    [Fact]
    public void Sbu_performance_is_for_office_roles_only()
    {
        ClaimsPrincipal WithRole(string role) => new(new ClaimsIdentity(
            [new Claim("preferred_username", "u"), new Claim("role", role)], "test", "preferred_username", "role"));
        Assert.NotNull(Catalog.Resolve("web_rd_get_sbu_performance", WithRole("Merchandiser")));
        Assert.NotNull(Catalog.Resolve("web_rd_get_sbu_performance", WithRole("Admin")));
        Assert.Null(Catalog.Resolve("web_rd_get_sbu_performance", WithRole("Factory")));
        Assert.Null(Catalog.Resolve("web_rd_get_sbu_performance", WithRole("Viewer")));
        // The product list stays open to factory users (filtered to their factory).
        Assert.NotNull(Catalog.Resolve("web_rd_get_sbu_products", Factory()));
    }
}
