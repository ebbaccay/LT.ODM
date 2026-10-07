using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Settings: roles, user access, the sidebar menu and reference lists. Admin role only.
/// Replaces the TMS /api/nav controller; who made a change comes from the sign-in token, not the request.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = AdminValidation.AdminRole)]
public sealed class AdminController(IAccessRepository access, IMenuRepository menu, IReferenceDataRepository refData, UserInviteService invites) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";
    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    // ----- Roles -----

    [HttpGet("roles")]
    public async Task<IReadOnlyList<RoleDto>> GetRoles(CancellationToken ct) => await access.GetRolesAsync(ct);

    [HttpPost("roles")]
    public Task<IActionResult> SaveRole(SaveRoleRequest request, CancellationToken ct)
        => Run(AdminValidation.Role(request), async () =>
        {
            var id = await access.SaveRoleAsync(request with { Name = request.Name.Trim() }, CurrentUserName, ct);
            return Ok(new { roleId = id });
        });

    [HttpDelete("roles/{name}")]
    public Task<IActionResult> DeleteRole(string name, CancellationToken ct)
        => Run([], async () =>
        {
            await access.DeleteRoleAsync(name, CurrentUserName, ct);
            return NoContent();
        });

    // ----- Users -----

    [HttpGet("users")]
    public async Task<IReadOnlyList<UserAccessDto>> GetUsers(CancellationToken ct) => await access.GetUsersAsync(ct);

    /// <summary>Creates a user and emails them a one-time link to set their password (no password is chosen here).</summary>
    [HttpPost("users")]
    public Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken ct)
        => Run(AdminValidation.NewUser(request), async () =>
        {
            var id = await invites.InviteAsync(request, CurrentUserName, ct);
            return Ok(new { userId = id });
        });

    /// <summary>Emails the user a new one-time "set your password" link; older links stop working.</summary>
    [HttpPost("users/{userId:int}/password-link")]
    public Task<IActionResult> SendPasswordLink(int userId, CancellationToken ct)
        => Run([], async () =>
        {
            await invites.SendPasswordLinkAsync(userId, CurrentUserName, ct);
            return NoContent();
        });

    /// <summary>Replaces the user's roles, user group and location. Applies at their next token refresh (≤ 15 min).</summary>
    [HttpPut("users/{userId:int}/access")]
    public Task<IActionResult> SetUserAccess(int userId, SetUserAccessRequest request, CancellationToken ct)
    {
        var errors = AdminValidation.Access(request);
        if (AdminValidation.RemovesOwnAdmin(userId, CurrentUserId, request.Roles))
            errors["roles"] = ["You cannot remove your own Admin role. Ask another administrator."];
        return Run(errors, async () =>
        {
            var normalized = request with { UserGroup = Blank(request.UserGroup), Location = Blank(request.Location) };
            await access.SetUserAccessAsync(userId, normalized, CurrentUserName, ct);
            return NoContent();
        });
    }

    // ----- Menu -----

    [HttpGet("menu")]
    public async Task<IReadOnlyList<MenuConfigGroupDto>> GetMenu(CancellationToken ct) => await menu.GetConfigAsync(ct);

    [HttpPost("menu/groups")]
    public Task<IActionResult> SaveGroup(SaveMenuGroupRequest request, CancellationToken ct)
        => Run(AdminValidation.Group(request), async () =>
        {
            var id = await menu.SaveGroupAsync(request with { Text = request.Text.Trim() }, CurrentUserName, ct);
            return Ok(new { groupId = id });
        });

    [HttpDelete("menu/groups/{groupId:int}")]
    public async Task<IActionResult> DeleteGroup(int groupId, CancellationToken ct)
    {
        await menu.DeleteGroupAsync(groupId, ct);
        return NoContent();
    }

    [HttpPost("menu/items")]
    public Task<IActionResult> SaveItem(SaveMenuItemRequest request, CancellationToken ct)
        => Run(AdminValidation.Item(request), async () =>
        {
            var normalized = request with { Text = request.Text.Trim(), Route = AdminValidation.NormalizeRoute(request.Route) };
            var id = await menu.SaveItemAsync(normalized, CurrentUserName, ct);
            return Ok(new { itemId = id });
        });

    [HttpDelete("menu/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int itemId, CancellationToken ct)
    {
        await menu.DeleteItemAsync(itemId, ct);
        return NoContent();
    }

    // ----- Reference lists -----

    /// <summary>The pick lists the page can maintain, with their code and name rules.</summary>
    [HttpGet("ref-lists")]
    public IReadOnlyList<RefListInfoDto> GetRefLists() => ReferenceLists.All.Select(ReferenceLists.Info).ToList();

    [HttpGet("ref-lists/{list}")]
    public async Task<IActionResult> GetRefList(string list, CancellationToken ct)
        => ReferenceLists.Find(list) is null ? NotFound() : Ok(await refData.GetAsync(list, ct));

    [HttpPost("ref-lists/{list}")]
    public Task<IActionResult> SaveRefListItem(string list, SaveRefListItemRequest request, CancellationToken ct)
    {
        if (ReferenceLists.Find(list) is not { } def) return Task.FromResult<IActionResult>(NotFound());
        return Run(ReferenceLists.Validate(def, request), async () =>
        {
            var normalized = ReferenceLists.Normalize(def, request);
            await refData.SaveAsync(list, normalized, CurrentUserName, ct);
            return Ok(new { code = normalized.Code });
        });
    }

    /// <summary>The code is a query parameter: codes typed in the Styles forms may contain '/'.</summary>
    [HttpDelete("ref-lists/{list}")]
    public Task<IActionResult> DeleteRefListItem(string list, [FromQuery] string code, CancellationToken ct)
    {
        if (ReferenceLists.Find(list) is null) return Task.FromResult<IActionResult>(NotFound());
        return Run([], async () =>
        {
            await refData.DeleteAsync(list, code, ct);
            return NoContent();
        });
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Validation errors -> 400 ValidationProblem; database rules (AdminRuleException) -> 400 Problem.</summary>
    private async Task<IActionResult> Run(Dictionary<string, string[]> errors, Func<Task<IActionResult>> action)
    {
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        try
        {
            return await action();
        }
        catch (AdminRuleException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }
}
