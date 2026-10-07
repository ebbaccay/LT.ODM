using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Auth;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Repositories;

public sealed class MenuRepository(IDbConnectionFactory connectionFactory) : IMenuRepository
{
    private sealed class Row
    {
        public int GroupId { get; init; }
        public string GroupText { get; init; } = "";
        public string GroupIcon { get; init; } = "";
        public string GroupSlot { get; init; } = "main";
        public int GroupSortOrder { get; init; }
        public bool GroupIsVisible { get; init; } = true;
        public int? ItemId { get; init; }
        public string? ItemText { get; init; }
        public string? ItemRoute { get; init; }
        public string? ItemIcon { get; init; }
        public int? ItemSortOrder { get; init; }
        public bool? ItemIsVisible { get; init; }
        public string? AllowedRoles { get; init; }
    }

    public async Task<MenuDto> GetForUserAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(Proc("nav.usp_Menu_GetForUser", new { UserId = userId }, ct));
        var groups = rows.GroupBy(r => r.GroupId).Select(g =>
        {
            var f = g.First();
            return new MenuGroupDto(f.GroupId, f.GroupText, f.GroupIcon, f.GroupSlot, f.GroupSortOrder,
                g.Where(r => r.ItemId is not null)
                 .Select(r => new MenuItemDto(r.ItemId!.Value, r.ItemText!, r.ItemRoute!, r.ItemIcon!, r.ItemSortOrder ?? 0))
                 .ToList());
        }).ToList();
        return new MenuDto(groups);
    }

    public async Task<IReadOnlyList<MenuConfigGroupDto>> GetConfigAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(Proc("nav.usp_Config_Get", null, ct));
        return rows.GroupBy(r => r.GroupId).Select(g =>
        {
            var f = g.First();
            return new MenuConfigGroupDto(f.GroupId, f.GroupText, f.GroupIcon, f.GroupSlot, f.GroupSortOrder, f.GroupIsVisible,
                g.Where(r => r.ItemId is not null)
                 .Select(r => new MenuConfigItemDto(r.ItemId!.Value, r.ItemText!, r.ItemRoute!, r.ItemIcon!, r.ItemSortOrder ?? 0,
                     r.ItemIsVisible ?? true, Split(r.AllowedRoles)))
                 .ToList());
        }).ToList();
    }

    public async Task<int> SaveGroupAsync(SaveMenuGroupRequest r, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await RuleErrors(() => conn.QuerySingleAsync<int>(Proc("nav.usp_Group_Upsert", new
        {
            r.GroupId, r.Text, r.Icon, r.Slot, r.SortOrder, r.IsVisible, UpdatedBy = changedBy,
        }, ct)));
    }

    public async Task DeleteGroupAsync(int groupId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("nav.usp_Group_Delete", new { GroupId = groupId }, ct));
    }

    public async Task<int> SaveItemAsync(SaveMenuItemRequest r, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await RuleErrors(() => conn.QuerySingleAsync<int>(Proc("nav.usp_Item_Save", new
        {
            r.ItemId, r.GroupId, r.Text, r.Route, r.Icon, r.SortOrder, r.IsVisible,
            RoleNames = string.Join(',', r.Roles), UpdatedBy = changedBy,
        }, ct)));
    }

    public async Task DeleteItemAsync(int itemId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("nav.usp_Item_Delete", new { ItemId = itemId }, ct));
    }

    internal static IReadOnlyList<string> Split(string? csv)
        => string.IsNullOrEmpty(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    /// <summary>THROW 50000+ from the admin procedures = a rule the user can fix (duplicate route, Admin role, ...).</summary>
    internal static async Task<T> RuleErrors<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new AdminRuleException(ex.Message);
        }
    }
}

public sealed class AccessRepository(IDbConnectionFactory connectionFactory) : IAccessRepository
{
    private sealed class RoleRow
    {
        public int RoleId { get; init; }
        public string Name { get; init; } = "";
        public string? DisplayName { get; init; }
        public string? Description { get; init; }
        public int UserCount { get; init; }
    }

    private sealed class UserRow
    {
        public int UserId { get; init; }
        public string UserName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Email { get; init; } = "";
        public bool IsActive { get; init; }
        public string? UserGroup { get; init; }
        public string? Location { get; init; }
        public DateTime? LastLoginUtc { get; init; }
        public DateTime? LockoutEndUtc { get; init; }
        public string? Roles { get; init; }
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<RoleRow>(MenuRepository.Proc("auth.usp_Role_List", null, ct));
        return rows.Select(r => new RoleDto(r.RoleId, r.Name, r.DisplayName, r.Description, r.UserCount)).ToList();
    }

    public async Task<int> SaveRoleAsync(SaveRoleRequest r, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await MenuRepository.RuleErrors(() => conn.QuerySingleAsync<int>(MenuRepository.Proc("auth.usp_Role_Save", new
        {
            r.RoleId, r.Name, r.DisplayName, r.Description, ChangedBy = changedBy,
        }, ct)));
    }

    public async Task DeleteRoleAsync(string name, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await MenuRepository.RuleErrors(() => conn.ExecuteAsync(MenuRepository.Proc("auth.usp_Role_Delete", new { Name = name, ChangedBy = changedBy }, ct)));
    }

    public async Task<IReadOnlyList<UserAccessDto>> GetUsersAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<UserRow>(MenuRepository.Proc("auth.usp_User_List", null, ct));
        return rows.Select(r => new UserAccessDto(r.UserId, r.UserName, r.DisplayName, r.Email, r.IsActive, r.UserGroup, r.Location,
            r.LastLoginUtc is { } l ? DateTime.SpecifyKind(l, DateTimeKind.Utc) : null,
            r.LockoutEndUtc is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
            MenuRepository.Split(r.Roles))).ToList();
    }

    public async Task SetUserAccessAsync(int userId, SetUserAccessRequest r, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await MenuRepository.RuleErrors(() => conn.ExecuteAsync(MenuRepository.Proc("auth.usp_User_SetAccess", new
        {
            UserId = userId, RoleNames = string.Join(',', r.Roles), r.UserGroup, r.Location, ChangedBy = changedBy,
        }, ct)));
    }

    public async Task<int> InviteUserAsync(CreateUserRequest r, string unusablePasswordHash, byte[] tokenHash, DateTime expiresUtc, string createdBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await MenuRepository.RuleErrors(() => conn.QuerySingleAsync<int>(MenuRepository.Proc("auth.usp_User_Invite", new
        {
            r.UserName, r.Email, r.DisplayName, PasswordHash = unusablePasswordHash, RoleNames = string.Join(',', r.Roles),
            r.UserGroup, r.Location, TokenHash = tokenHash, ExpiresUtc = expiresUtc, CreatedBy = createdBy,
        }, ct)));
    }

    public async Task<ResetUserRecord> CreatePasswordLinkAsync(int userId, byte[] tokenHash, DateTime expiresUtc, string requestedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await MenuRepository.RuleErrors(() => conn.QuerySingleAsync<ResetUserRecord>(MenuRepository.Proc("auth.usp_User_CreatePasswordLink", new
        {
            UserId = userId, TokenHash = tokenHash, ExpiresUtc = expiresUtc, RequestedBy = requestedBy,
        }, ct)));
    }
}

public sealed class ReferenceDataRepository(IDbConnectionFactory connectionFactory) : IReferenceDataRepository
{
    public async Task<IReadOnlyList<RefListItemDto>> GetAsync(string list, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await MenuRepository.RuleErrors(() => conn.QueryAsync<RefListItemDto>(MenuRepository.Proc("ref.usp_RefList_Get", new { List = list }, ct)));
        return rows.ToList();
    }

    public async Task SaveAsync(string list, SaveRefListItemRequest r, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await MenuRepository.RuleErrors(() => conn.ExecuteAsync(MenuRepository.Proc("ref.usp_RefList_Save", new
        {
            List = list, r.IsNew, r.Code, r.Name, r.SortOrder, r.IsActive, ChangedBy = changedBy,
        }, ct)));
    }

    public async Task DeleteAsync(string list, string code, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await MenuRepository.RuleErrors(() => conn.ExecuteAsync(MenuRepository.Proc("ref.usp_RefList_Delete", new { List = list, Code = code }, ct)));
    }
}
