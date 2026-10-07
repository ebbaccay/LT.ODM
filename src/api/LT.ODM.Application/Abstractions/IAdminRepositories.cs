using LT.ODM.Application.Admin;
using LT.ODM.Application.Auth;

namespace LT.ODM.Application.Abstractions;

/// <summary>Sidebar menu (nav.* procedures).</summary>
public interface IMenuRepository
{
    Task<MenuDto> GetForUserAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyList<MenuConfigGroupDto>> GetConfigAsync(CancellationToken ct = default);
    Task<int> SaveGroupAsync(SaveMenuGroupRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteGroupAsync(int groupId, CancellationToken ct = default);
    Task<int> SaveItemAsync(SaveMenuItemRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteItemAsync(int itemId, CancellationToken ct = default);
}

/// <summary>Pick lists maintained in Settings > Reference lists (ref.usp_RefList_*). List = a ReferenceLists key.</summary>
public interface IReferenceDataRepository
{
    Task<IReadOnlyList<RefListItemDto>> GetAsync(string list, CancellationToken ct = default);
    Task SaveAsync(string list, SaveRefListItemRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteAsync(string list, string code, CancellationToken ct = default);
}

/// <summary>Roles and user access (auth.usp_Role_* / auth.usp_User_List / auth.usp_User_SetAccess).</summary>
public interface IAccessRepository
{
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default);
    Task<int> SaveRoleAsync(SaveRoleRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteRoleAsync(string name, string changedBy, CancellationToken ct = default);
    Task<IReadOnlyList<UserAccessDto>> GetUsersAsync(CancellationToken ct = default);
    Task SetUserAccessAsync(int userId, SetUserAccessRequest request, string changedBy, CancellationToken ct = default);

    /// <summary>Creates the user with an unusable password and a one-time "set your password" token (auth.usp_User_Invite).</summary>
    Task<int> InviteUserAsync(CreateUserRequest request, string unusablePasswordHash, byte[] tokenHash, DateTime expiresUtc, string createdBy, CancellationToken ct = default);

    /// <summary>Replaces the user's open reset links with a new one and returns who to email (auth.usp_User_CreatePasswordLink).</summary>
    Task<ResetUserRecord> CreatePasswordLinkAsync(int userId, byte[] tokenHash, DateTime expiresUtc, string requestedBy, CancellationToken ct = default);
}
