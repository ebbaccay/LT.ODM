using LT.ODM.Application.Admin;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Dashboards;

/// <summary>
/// Home page dashboards. Each role may name one in Settings > Roles (auth.Roles.DashboardKey); NULL = the library overview.
/// Both need the Style Library readers' roles; other users (factories) get the welcome page only. Users see only their
/// roles' dashboard; Admins can switch between all of them. To add one: see docs/components/dashboard.md (Adding a dashboard).
/// </summary>
public static class DashboardKeys
{
    /// <summary>The Style Library at a glance (style.usp_Dashboard_Get).</summary>
    public const string Library = "library";

    /// <summary>The signed-in user's own styles and how complete they are (style.usp_Dashboard_Designer).</summary>
    public const string Designer = "designer";

    /// <summary>Every dashboard, in the order the home page offers them.</summary>
    public static readonly string[] All = [Library, Designer];

    public static bool IsKnown(string? key) => key is not null && All.Contains(key, StringComparer.Ordinal);

    /// <summary>Blank and the library overview are both stored as NULL (the default).</summary>
    public static string? Normalize(string? key)
    {
        var k = key?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(k) || k == Library ? null : k;
    }
}

/// <summary>
/// DefaultKey: the dashboard the home page opens with (null = welcome only). Available: the dashboards the user may open:
/// only DefaultKey for most users, every dashboard for Admins (they look after all of them).
/// </summary>
public sealed record DashboardChoiceDto(string? DefaultKey, IReadOnlyList<string> Available);

public static class DashboardChoice
{
    private static readonly string[] LibraryReaders = StyleRoles.Readers.Split(',');

    /// <summary>
    /// The user's home dashboard from their roles. A role that chose a dashboard other than the library overview wins over
    /// roles that kept the default (an Admin who is also a Designer opens on the designer dashboard); when roles chose
    /// different ones, the first in <see cref="DashboardKeys.All"/> order wins. Unknown keys are ignored.
    /// Users other than Admins see only that one dashboard; Admins may switch to any.
    /// </summary>
    public static DashboardChoiceDto Resolve(Func<string, bool> isInRole, IEnumerable<RoleDto> roles)
    {
        if (!LibraryReaders.Any(isInRole)) return new DashboardChoiceDto(null, []);
        var chosen = roles
            .Where(r => isInRole(r.Name))
            .Select(r => DashboardKeys.Normalize(r.DashboardKey))
            .Where(k => k is not null && DashboardKeys.IsKnown(k))
            .ToHashSet(StringComparer.Ordinal);
        var key = DashboardKeys.All.FirstOrDefault(chosen.Contains) ?? DashboardKeys.Library;
        return new DashboardChoiceDto(key, isInRole(AdminValidation.AdminRole) ? DashboardKeys.All : [key]);
    }
}
