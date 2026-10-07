using System.Text.RegularExpressions;

namespace LT.ODM.Application.Admin;

/// <summary>Input rules for Settings (mirrors the CHECK constraints in db/tables/nav.tables.sql and auth.tables.sql).</summary>
public static partial class AdminValidation
{
    public const string AdminRole = "Admin";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{1,63}$")]
    private static partial Regex RoleNameRegex();

    [GeneratedRegex("^lucide[A-Za-z0-9]+$")]
    private static partial Regex IconRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9/_-]*$")]
    private static partial Regex RouteRegex();

    public static Dictionary<string, string[]> Role(SaveRoleRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.RoleId == 0 && !RoleNameRegex().IsMatch(r.Name ?? ""))
            errors["name"] = ["Use 2-64 letters, digits or underscores, starting with a letter (e.g. Merchandiser)."];
        if ((r.DisplayName?.Length ?? 0) > 128) errors["displayName"] = ["Use at most 128 characters."];
        if ((r.Description?.Length ?? 0) > 256) errors["description"] = ["Use at most 256 characters."];
        return errors;
    }

    public static Dictionary<string, string[]> Group(SaveMenuGroupRequest g)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(g.Text) || g.Text.Length > 100) errors["text"] = ["Enter a name (at most 100 characters)."];
        if (!IconRegex().IsMatch(g.Icon ?? "")) errors["icon"] = ["Choose an icon."];
        if (g.Slot is not ("main" or "bottom")) errors["slot"] = ["Choose main or bottom."];
        return errors;
    }

    public static Dictionary<string, string[]> Item(SaveMenuItemRequest i)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(i.Text) || i.Text.Length > 100) errors["text"] = ["Enter a name (at most 100 characters)."];
        var route = NormalizeRoute(i.Route);
        if (route.Length > 200 || (route.Length > 0 && !RouteRegex().IsMatch(route)))
            errors["route"] = ["Use lower-case letters, digits, '-', '_' and '/' (e.g. garment-quotation or settings/roles)."];
        if (!IconRegex().IsMatch(i.Icon ?? "")) errors["icon"] = ["Choose an icon."];
        if (i.GroupId <= 0) errors["groupId"] = ["Choose a group."];
        return errors;
    }

    public static Dictionary<string, string[]> Access(SetUserAccessRequest a)
    {
        var errors = new Dictionary<string, string[]>();
        if (a.Roles is null) errors["roles"] = ["Send the list of roles (it can be empty)."];
        if ((a.UserGroup?.Trim().Length ?? 0) > 16) errors["userGroup"] = ["Use at most 16 characters."];
        if ((a.Location?.Trim().Length ?? 0) > 32) errors["location"] = ["Use at most 32 characters."];
        return errors;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{3,64}$")]
    private static partial Regex UserNameRegex();

    /// <summary>Same pattern as CK_auth_Users_Email.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._%+'-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$")]
    private static partial Regex EmailRegex();

    public static Dictionary<string, string[]> NewUser(CreateUserRequest u)
    {
        var errors = Access(new SetUserAccessRequest(u.Roles, u.UserGroup, u.Location));
        if (!UserNameRegex().IsMatch(u.UserName?.Trim() ?? ""))
            errors["userName"] = ["Use 3-64 letters, digits, '.', '_' or '-' (e.g. jdoe or fty.dg01)."];
        var email = u.Email?.Trim() ?? "";
        if (email.Length > 256 || !EmailRegex().IsMatch(email)) errors["email"] = ["Enter a valid email address."];
        if (string.IsNullOrWhiteSpace(u.DisplayName) || u.DisplayName.Trim().Length > 128) errors["displayName"] = ["Enter the user's name (at most 128 characters)."];
        return errors;
    }

    /// <summary>Routes are stored without the leading slash ('/' = home = '').</summary>
    public static string NormalizeRoute(string? route) => (route ?? "").Trim().Trim('/');

    /// <summary>An administrator must not remove their own Admin role (they would lock themselves out of Settings).</summary>
    public static bool RemovesOwnAdmin(int editedUserId, int currentUserId, IEnumerable<string> newRoles)
        => editedUserId == currentUserId && !(newRoles ?? []).Contains(AdminRole, StringComparer.Ordinal);
}
