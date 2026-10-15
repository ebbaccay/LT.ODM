using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakeMenuRepository : IMenuRepository
{
    public ConcurrentDictionary<int, int> MenuRequestsByUser { get; } = new();
    public List<SaveMenuItemRequest> SavedItems { get; } = [];

    public Task<MenuDto> GetForUserAsync(int userId, CancellationToken ct = default)
    {
        MenuRequestsByUser.AddOrUpdate(userId, 1, (_, n) => n + 1);
        return Task.FromResult(new MenuDto([new MenuGroupDto(1, "Home", "lucideLayoutDashboard", "main", 1, [new MenuItemDto(1, "Dashboard", "", "lucideLayoutDashboard", 10)])]));
    }

    public Task<IReadOnlyList<MenuConfigGroupDto>> GetConfigAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MenuConfigGroupDto>>([]);

    public Task<int> SaveGroupAsync(SaveMenuGroupRequest request, string changedBy, CancellationToken ct = default) => Task.FromResult(7);

    public Task DeleteGroupAsync(int groupId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> SaveItemAsync(SaveMenuItemRequest request, string changedBy, CancellationToken ct = default)
    {
        if (request.Route == "taken") throw new AdminRuleException("Another menu item already uses this route.");
        SavedItems.Add(request);
        return Task.FromResult(11);
    }

    public Task DeleteItemAsync(int itemId, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeReferenceDataRepository : IReferenceDataRepository
{
    public List<(string List, SaveRefListItemRequest Request, string ChangedBy)> Saved { get; } = [];
    public List<(string List, string Code)> Deleted { get; } = [];

    public Task<IReadOnlyList<RefListItemDto>> GetAsync(string list, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RefListItemDto>>([new RefListItemDto("FAB", "Fabric", 1, null, 3)]);

    public Task SaveAsync(string list, SaveRefListItemRequest request, string changedBy, CancellationToken ct = default)
    {
        if (request.IsNew && request.Code == "FAB") throw new AdminRuleException("This code already exists in the list.");
        Saved.Add((list, request, changedBy));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string list, string code, CancellationToken ct = default)
    {
        if (code == "FAB") throw new AdminRuleException("This code is still used.");
        Deleted.Add((list, code));
        return Task.CompletedTask;
    }
}

public sealed class FakeAccessRepository : IAccessRepository
{
    public List<(int UserId, SetUserAccessRequest Request, string ChangedBy)> AccessChanges { get; } = [];

    public Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RoleDto>>([new RoleDto(1, "Admin", "Administrator", null, 1)]);

    public Task<int> SaveRoleAsync(SaveRoleRequest request, string changedBy, CancellationToken ct = default) => Task.FromResult(5);

    public Task DeleteRoleAsync(string name, string changedBy, CancellationToken ct = default)
        => name == "Admin" ? throw new AdminRuleException("The Admin role cannot be deleted.") : Task.CompletedTask;

    public Task<IReadOnlyList<UserAccessDto>> GetUsersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UserAccessDto>>([]);

    public Task SetUserAccessAsync(int userId, SetUserAccessRequest request, string changedBy, CancellationToken ct = default)
    {
        AccessChanges.Add((userId, request, changedBy));
        return Task.CompletedTask;
    }

    public List<(CreateUserRequest Request, string PasswordHash, byte[]? TokenHash, DateTime? ExpiresUtc, string CreatedBy)> Invites { get; } = [];
    public List<(int UserId, byte[] TokenHash, string RequestedBy)> PasswordLinks { get; } = [];

    public Task<int> InviteUserAsync(CreateUserRequest request, string passwordHash, byte[]? tokenHash, DateTime? expiresUtc, string createdBy, CancellationToken ct = default)
    {
        if (request.UserName == "taken") throw new AdminRuleException("A user with this user name already exists.");
        Invites.Add((request, passwordHash, tokenHash, expiresUtc, createdBy));
        return Task.FromResult(500 + Invites.Count);
    }

    public Task<ResetUserRecord> CreatePasswordLinkAsync(int userId, byte[] tokenHash, DateTime expiresUtc, string requestedBy, CancellationToken ct = default)
    {
        if (userId == 404) throw new AdminRuleException("User not found or inactive.");
        PasswordLinks.Add((userId, tokenHash, requestedBy));
        return Task.FromResult(new ResetUserRecord { UserId = userId, UserName = "mchan", Email = "mei.chan@company.test", DisplayName = "Mei Chan" });
    }

    public List<(int UserId, string PasswordHash, string ChangedBy)> TemporaryPasswords { get; } = [];

    public Task SetTemporaryPasswordAsync(int userId, string passwordHash, int historyToKeep, string changedBy, CancellationToken ct = default)
    {
        TemporaryPasswords.Add((userId, passwordHash, changedBy));
        return Task.CompletedTask;
    }

    public List<(int UserId, bool IsActive, string ChangedBy)> ActiveChanges { get; } = [];
    public List<(int UserId, string ChangedBy)> Deletes { get; } = [];

    public Task SetUserActiveAsync(int userId, bool isActive, string changedBy, CancellationToken ct = default)
    {
        if (userId == 404) throw new AdminRuleException("User not found. Reload the page.");
        ActiveChanges.Add((userId, isActive, changedBy));
        return Task.CompletedTask;
    }

    public Task DeleteUserAsync(int userId, string changedBy, CancellationToken ct = default)
    {
        if (userId == 404) throw new AdminRuleException("User not found. Reload the page.");
        Deletes.Add((userId, changedBy));
        return Task.CompletedTask;
    }
}

public sealed class AdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    /// <summary>Creates a user with the given roles and returns an HTTPS client signed in as them.</summary>
    private async Task<(HttpClient Client, UserProfileDto User)> SignInAsync(params string[] roles)
    {
        var name = "adm" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, login.User);
    }

    [Fact]
    public async Task Settings_need_the_Admin_role()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync("/api/v1/admin/roles")).StatusCode);

        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/admin/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/admin/users/1/access", new { roles = new[] { "Admin" } })).StatusCode);

        var (admin, _) = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/roles")).StatusCode);
    }

    [Fact]
    public async Task Role_names_are_validated()
    {
        var (admin, _) = await SignInAsync("Admin");
        var bad = await admin.PostAsJsonAsync("/api/v1/admin/roles", new { roleId = 0, name = "bad name!", displayName = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("\"name\"", await bad.Content.ReadAsStringAsync());

        var ok = await admin.PostAsJsonAsync("/api/v1/admin/roles", new { roleId = 0, name = "Sampling", displayName = "Sampling room" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Database_rules_come_back_as_readable_errors()
    {
        var (admin, _) = await SignInAsync("Admin");
        var response = await admin.DeleteAsync("/api/v1/admin/roles/Admin");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cannot be deleted", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admins_cannot_remove_their_own_Admin_role()
    {
        var (admin, me) = await SignInAsync("Admin");
        var response = await admin.PutAsJsonAsync($"/api/v1/admin/users/{me.UserId}/access", new { roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("own Admin role", await response.Content.ReadAsStringAsync());

        var other = await admin.PutAsJsonAsync("/api/v1/admin/users/999/access", new { roles = new[] { "Factory" }, userGroup = "FTY", location = "F001" });
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
        var change = factory.Access.AccessChanges.Last();
        Assert.Equal(999, change.UserId);
        Assert.Equal(me.UserName, change.ChangedBy); // from the token, not the request
    }

    [Fact]
    public async Task Menu_items_are_validated_and_routes_normalised()
    {
        var (admin, _) = await SignInAsync("Admin");
        var bad = await admin.PostAsJsonAsync("/api/v1/admin/menu/items",
            new { itemId = 0, groupId = 1, text = "X", route = "Bad Route", icon = "lucideX", sortOrder = 1, isVisible = true, roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await admin.PostAsJsonAsync("/api/v1/admin/menu/items",
            new { itemId = 0, groupId = 1, text = "Styles", route = "/styles/", icon = "lucideImages", sortOrder = 1, isVisible = true, roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("styles", factory.Menu.SavedItems.Last().Route);

        var taken = await admin.PostAsJsonAsync("/api/v1/admin/menu/items",
            new { itemId = 0, groupId = 1, text = "Dup", route = "taken", icon = "lucideImages", sortOrder = 1, isVisible = true, roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("already uses this route", await taken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reference_lists_are_whitelisted_validated_and_normalised()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/admin/ref-lists/contentClasses")).StatusCode);

        var (admin, me) = await SignInAsync("Admin");
        var lists = await admin.GetFromJsonAsync<RefListInfoDto[]>("/api/v1/admin/ref-lists");
        Assert.Equal(11, lists!.Length);   // brands added 2026-10-15
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/admin/ref-lists/users")).StatusCode);
        Assert.Equal("FAB", Assert.Single((await admin.GetFromJsonAsync<RefListItemDto[]>("/api/v1/admin/ref-lists/contentClasses"))!).Code);

        var bad = await admin.PostAsJsonAsync("/api/v1/admin/ref-lists/contentClasses", new { isNew = true, code = "TOO-LONG-CODE", name = " ", sortOrder = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        Assert.Contains("\"code\"", body);
        Assert.Contains("\"name\"", body);
        Assert.Contains("\"sortOrder\"", body);
        var badSeason = await admin.PostAsJsonAsync("/api/v1/admin/ref-lists/seasons", new { isNew = true, code = "SS27", name = "" });
        Assert.Contains("2027-SS", await badSeason.Content.ReadAsStringAsync());

        // Case rules per list; SortOrder / IsActive dropped where the list has none.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/admin/ref-lists/contentClasses", new { isNew = true, code = " pkg ", name = " Packaging ", sortOrder = 6, isActive = false })).StatusCode);
        var cc = factory.RefData.Saved.Last();
        Assert.Equal(("contentClasses", "PKG", "Packaging", (int?)6, (bool?)null, me.UserName), (cc.List, cc.Request.Code, cc.Request.Name, cc.Request.SortOrder, cc.Request.IsActive, cc.ChangedBy));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/admin/ref-lists/uoms", new { isNew = true, code = "PCS", name = "Pieces", sortOrder = 3 })).StatusCode);
        Assert.Equal(("PCS".ToLowerInvariant(), (int?)null), (factory.RefData.Saved.Last().Request.Code, factory.RefData.Saved.Last().Request.SortOrder));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/admin/ref-lists/seasons", new { isNew = true, code = "2028-fw", name = "" })).StatusCode);
        Assert.Equal(("2028-FW", (bool?)true), (factory.RefData.Saved.Last().Request.Code, factory.RefData.Saved.Last().Request.IsActive));

        var dup = await admin.PostAsJsonAsync("/api/v1/admin/ref-lists/contentClasses", new { isNew = true, code = "FAB", name = "Fabric", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("already exists", await dup.Content.ReadAsStringAsync());

        var used = await admin.DeleteAsync("/api/v1/admin/ref-lists/contentClasses?code=FAB");
        Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
        Assert.Contains("still used", await used.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/admin/ref-lists/productTypes?code=" + Uri.EscapeDataString("TOP/BTM"))).StatusCode);
        Assert.Equal(("productTypes", "TOP/BTM"), factory.RefData.Deleted.Last());
    }

    [Fact]
    public async Task Creating_a_user_emails_a_one_time_link_and_never_takes_a_password()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/admin/users", new { userName = "x1", email = "x1@company.test", displayName = "X", roles = Array.Empty<string>() })).StatusCode);

        var (admin, me) = await SignInAsync("Admin");
        var bad = await admin.PostAsJsonAsync("/api/v1/admin/users", new { userName = "a b", email = "not-an-email", displayName = " ", roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        Assert.Contains("userName", body);
        Assert.Contains("email", body);
        Assert.Contains("displayName", body);

        var response = await admin.PostAsJsonAsync("/api/v1/admin/users", new
        {
            userName = " fty.dg02 ", email = "dg02@factory.test", displayName = "Dongguan 02", roles = new[] { "Factory" }, userGroup = "FTY", location = " DG02 ",
            password = "ignored-if-sent",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var invite = factory.Access.Invites.Last();
        Assert.Equal("fty.dg02", invite.Request.UserName);
        Assert.Equal("DG02", invite.Request.Location);
        Assert.Equal(me.UserName, invite.CreatedBy);
        Assert.Equal(32, invite.TokenHash!.Length);
        Assert.InRange(invite.ExpiresUtc!.Value, DateTime.UtcNow.AddHours(71), DateTime.UtcNow.AddHours(73));

        // The emailed link carries the token whose SHA-256 hash was stored, and is marked as a welcome link.
        var mail = factory.Emails.Sent.Last(m => m.To == "dg02@factory.test");
        var token = System.Text.RegularExpressions.Regex.Match(mail.TextBody, @"token=([^&\s]+)&welcome=1").Groups[1].Value;
        Assert.NotEmpty(token);
        Assert.Equal(invite.TokenHash, System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(token))));
        Assert.Contains("fty.dg02", mail.TextBody);

        var taken = await admin.PostAsJsonAsync("/api/v1/admin/users", new { userName = "taken", email = "t@company.test", displayName = "T", roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("already exists", await taken.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admins_can_send_a_new_password_link()
    {
        var (admin, me) = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/v1/admin/users/2/password-link", null)).StatusCode);
        Assert.Equal((2, me.UserName), (factory.Access.PasswordLinks.Last().UserId, factory.Access.PasswordLinks.Last().RequestedBy));
        Assert.Contains("reset-password?token=", factory.Emails.Sent.Last(m => m.To == "mei.chan@company.test").TextBody);

        var missing = await admin.PostAsync("/api/v1/admin/users/404/password-link", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Admins_can_create_a_user_with_a_temporary_password_instead_of_a_link()
    {
        var (admin, me) = await SignInAsync("Admin");
        var emailsBefore = factory.Emails.Sent.Count;

        var weak = await admin.PostAsJsonAsync("/api/v1/admin/users", new
        {
            userName = "temp.weak", email = "temp.weak@company.test", displayName = "Temp Weak", roles = new[] { "Viewer" }, temporaryPassword = "short",
        });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("temporaryPassword", await weak.Content.ReadAsStringAsync());

        var response = await admin.PostAsJsonAsync("/api/v1/admin/users", new
        {
            userName = "temp.user", email = "temp.user@company.test", displayName = "Temp User", roles = new[] { "Viewer" }, temporaryPassword = Password,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var invite = factory.Access.Invites.Last();
        Assert.Equal("temp.user", invite.Request.UserName);
        Assert.Null(invite.Request.TemporaryPassword);
        Assert.Null(invite.TokenHash);
        Assert.True(factory.Services.GetRequiredService<IPasswordHasher>().Verify(invite.PasswordHash, Password));
        Assert.Equal(emailsBefore, factory.Emails.Sent.Count);
        Assert.Equal(me.UserName, invite.CreatedBy);
    }

    [Fact]
    public async Task Admins_can_set_a_temporary_password_for_another_user_but_not_themselves()
    {
        var (_, other) = await SignInAsync("Viewer");
        var (admin, me) = await SignInAsync("Admin");

        var weak = await admin.PutAsJsonAsync($"/api/v1/admin/users/{other.UserId}/password", new { password = "password" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.DoesNotContain(factory.Access.TemporaryPasswords, t => t.UserId == other.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/admin/users/{other.UserId}/password", new { password = Password })).StatusCode);
        var set = factory.Access.TemporaryPasswords.Last();
        Assert.Equal((other.UserId, me.UserName), (set.UserId, set.ChangedBy));
        Assert.True(factory.Services.GetRequiredService<IPasswordHasher>().Verify(set.PasswordHash, Password));

        var self = await admin.PutAsJsonAsync($"/api/v1/admin/users/{me.UserId}/password", new { password = Password });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/users/99999/password", new { password = Password })).StatusCode);
    }

    [Fact]
    public async Task Admins_can_deactivate_reactivate_and_remove_other_users_but_not_themselves()
    {
        var (admin, me) = await SignInAsync("Admin");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/admin/users/2/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/admin/users/2/active", new { isActive = true })).StatusCode);
        Assert.Equal([(2, false, me.UserName), (2, true, me.UserName)], factory.Access.ActiveChanges.Where(c => c.UserId == 2).TakeLast(2));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/admin/users/3")).StatusCode);
        Assert.Contains((3, me.UserName), factory.Access.Deletes);

        var deactivateSelf = await admin.PutAsJsonAsync($"/api/v1/admin/users/{me.UserId}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.BadRequest, deactivateSelf.StatusCode);
        Assert.Contains("your own account", await deactivateSelf.Content.ReadAsStringAsync());
        var removeSelf = await admin.DeleteAsync($"/api/v1/admin/users/{me.UserId}");
        Assert.Equal(HttpStatusCode.BadRequest, removeSelf.StatusCode);
        Assert.DoesNotContain(factory.Access.Deletes, d => d.UserId == me.UserId);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/v1/admin/users/404")).StatusCode);

        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync("/api/v1/admin/users/2")).StatusCode);
    }

    [Fact]
    public async Task Navigation_returns_the_menu_for_the_signed_in_user()
    {
        var (client, me) = await SignInAsync("Viewer");
        var menu = await client.GetFromJsonAsync<MenuDto>("/api/v1/navigation");
        Assert.Single(menu!.Groups);
        Assert.True(factory.Menu.MenuRequestsByUser.ContainsKey(me.UserId));
    }
}
