namespace LT.ODM.Application.Admin;

// ----- Sidebar menu (signed-in user) -----

public sealed record MenuItemDto(int ItemId, string Text, string Route, string Icon, int SortOrder);

/// <summary>Slot: "main" or "bottom" (pinned to the bottom of the sidebar).</summary>
public sealed record MenuGroupDto(int GroupId, string Text, string Icon, string Slot, int SortOrder, IReadOnlyList<MenuItemDto> Items);

public sealed record MenuDto(IReadOnlyList<MenuGroupDto> Groups);

// ----- Menu editor (Settings > Menu) -----

public sealed record MenuConfigItemDto(int ItemId, string Text, string Route, string Icon, int SortOrder, bool IsVisible, IReadOnlyList<string> AllowedRoles);

public sealed record MenuConfigGroupDto(int GroupId, string Text, string Icon, string Slot, int SortOrder, bool IsVisible, IReadOnlyList<MenuConfigItemDto> Items);

public sealed record SaveMenuGroupRequest(int GroupId, string Text, string Icon, string Slot, int SortOrder, bool IsVisible);

/// <summary>Roles: role names that may see the item; empty = every signed-in user.</summary>
public sealed record SaveMenuItemRequest(int ItemId, int GroupId, string Text, string Route, string Icon, int SortOrder, bool IsVisible, IReadOnlyList<string> Roles);

// ----- Roles and user access (Settings > Roles, User roles) -----

public sealed record RoleDto(int RoleId, string Name, string? DisplayName, string? Description, int UserCount);

public sealed record SaveRoleRequest(int RoleId, string Name, string? DisplayName, string? Description);

public sealed record UserAccessDto(
    int UserId, string UserName, string DisplayName, string Email, bool IsActive,
    string? UserGroup, string? Location, DateTime? LastLoginUtc, DateTime? LockoutEndUtc, IReadOnlyList<string> Roles);

/// <summary>UserGroup "FTY" = factory user (TMS); Location = factory / office code.</summary>
public sealed record SetUserAccessRequest(IReadOnlyList<string> Roles, string? UserGroup, string? Location);

/// <summary>Deactivate (signs the user out, blocks sign-in) or reactivate a user.</summary>
public sealed record SetUserActiveRequest(bool IsActive);

/// <summary>
/// New user from Settings > User roles. Without <see cref="TemporaryPassword"/> the user sets a password from an emailed
/// link; with it, no email is sent and the user must change the password at first sign-in.
/// </summary>
public sealed record CreateUserRequest(
    string UserName, string Email, string DisplayName, IReadOnlyList<string> Roles, string? UserGroup, string? Location,
    string? TemporaryPassword = null);

/// <summary>A temporary password an administrator gives a user; they must change it at first sign-in.</summary>
public sealed record SetTemporaryPasswordRequest(string Password);

/// <summary>A rule the database refused (duplicate route, Admin role, ...). Message is safe to show.</summary>
public sealed class AdminRuleException(string message) : Exception(message);
