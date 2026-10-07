using LT.ODM.Application.Admin;

namespace LT.ODM.Api.Tests;

public sealed class AdminValidationTests
{
    [Theory]
    [InlineData("Merchandiser", true)]
    [InlineData("QA_Lead2", true)]
    [InlineData("a", false)]          // too short
    [InlineData("2ndShift", false)]   // must start with a letter
    [InlineData("bad name", false)]
    [InlineData("Admin;DROP", false)]
    public void Role_names_follow_the_database_rule(string name, bool valid)
        => Assert.Equal(valid, AdminValidation.Role(new SaveRoleRequest(0, name, null, null)).Count == 0);

    [Fact]
    public void The_name_of_an_existing_role_is_not_checked_because_it_cannot_change()
        => Assert.Empty(AdminValidation.Role(new SaveRoleRequest(5, "", "Display", null)));

    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData(" /settings/roles/ ", "settings/roles")]
    [InlineData("garment-quotation", "garment-quotation")]
    public void Routes_are_stored_without_slashes(string input, string stored)
        => Assert.Equal(stored, AdminValidation.NormalizeRoute(input));

    [Theory]
    [InlineData("", true)]                 // home
    [InlineData("settings/user-roles", true)]
    [InlineData("Styles", false)]          // lower case only
    [InlineData("styles?x=1", false)]
    [InlineData("../admin", false)]
    public void Item_routes_are_validated(string route, bool valid)
    {
        var errors = AdminValidation.Item(new SaveMenuItemRequest(0, 1, "Page", route, "lucideImages", 1, true, []));
        Assert.Equal(valid, !errors.ContainsKey("route"));
    }

    [Theory]
    [InlineData("lucideImages", true)]
    [InlineData("images", false)]
    [InlineData("lucide-images", false)]
    public void Icons_must_be_lucide_names(string icon, bool valid)
        => Assert.Equal(valid, !AdminValidation.Group(new SaveMenuGroupRequest(0, "Group", icon, "main", 1, true)).ContainsKey("icon"));

    [Fact]
    public void Group_slot_is_main_or_bottom()
        => Assert.True(AdminValidation.Group(new SaveMenuGroupRequest(0, "Group", "lucideFolder", "fixedItems", 1, true)).ContainsKey("slot"));

    [Fact]
    public void User_group_and_location_fit_their_columns()
    {
        var errors = AdminValidation.Access(new SetUserAccessRequest([], new string('G', 17), new string('L', 33)));
        Assert.Contains("userGroup", errors.Keys);
        Assert.Contains("location", errors.Keys);
        Assert.Empty(AdminValidation.Access(new SetUserAccessRequest(["Factory"], "FTY", "F001")));
    }

    [Theory]
    [InlineData(1, 1, new[] { "Viewer" }, true)]
    [InlineData(1, 1, new[] { "Admin", "Viewer" }, false)]
    [InlineData(2, 1, new[] { "Viewer" }, false)]   // removing someone else's Admin role is allowed
    [InlineData(1, 1, new[] { "admin" }, true)]     // role names are case-sensitive, like the token
    public void Admins_cannot_remove_their_own_Admin_role(int edited, int current, string[] roles, bool blocked)
        => Assert.Equal(blocked, AdminValidation.RemovesOwnAdmin(edited, current, roles));

    [Theory]
    [InlineData("jdoe", "jane.doe@company.com", "Jane Doe", true)]
    [InlineData("fty.dg01", "dg01@factory.example", "Dongguan 01", true)]
    [InlineData("jd", "jane.doe@company.com", "Jane", false)]          // user name too short
    [InlineData("j doe", "jane.doe@company.com", "Jane", false)]
    [InlineData("jdoe", "jane.doe@company", "Jane", false)]            // no top-level domain
    [InlineData("jdoe", "jane doe@company.com", "Jane", false)]
    [InlineData("jdoe", "jane.doe@company.com", "  ", false)]          // name required
    public void New_users_follow_the_database_rules(string userName, string email, string name, bool valid)
        => Assert.Equal(valid, AdminValidation.NewUser(new CreateUserRequest(userName, email, name, [], null, null)).Count == 0);
}
