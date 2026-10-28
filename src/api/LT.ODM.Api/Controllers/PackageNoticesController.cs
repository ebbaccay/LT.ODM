using LT.ODM.Api.Hubs;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// The signed-in user's Style Packages notices (the bell): clients answering through share links of packages they own or
/// links they made. Each user sees only their own.
/// </summary>
[ApiController]
[Route("api/v1/package-notices")]
[Authorize(Roles = StyleRoles.Readers)]
public sealed class PackageNoticesController(IPackageRepository packages) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    [HttpGet]
    public Task<IReadOnlyList<PackageNoticeDto>> List(CancellationToken ct) => packages.ListNoticesAsync(CurrentUserName, ct);

    [HttpPost("{noticeId:int}/read")]
    public async Task<IActionResult> Read(int noticeId, CancellationToken ct)
    {
        await packages.ReadNoticesAsync(CurrentUserName, noticeId, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await packages.ReadNoticesAsync(CurrentUserName, null, ct);
        return NoContent();
    }
}

/// <summary>Share link secrets encrypted with ASP.NET Data Protection (key ring set up in Program.cs), like AI API keys.</summary>
public sealed class DataProtectionShareTokenProtector(IDataProtectionProvider provider) : IShareTokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("LT.ODM.Packages.ShareLinks.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string? Unprotect(string protectedToken)
    {
        try { return _protector.Unprotect(protectedToken); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}

/// <summary>Pushes package notices to the users' open app pages through the notifications hub (event "PackageNotice").</summary>
public sealed class HubPackageNotifier(IHubContext<NotificationsHub> hub) : IPackageNotifier
{
    public async Task NotifyAsync(IReadOnlyList<PackageNoticeFor> notices, CancellationToken ct = default)
    {
        foreach (var n in notices)
            await hub.Clients.Group(NotificationsHub.UserGroup(n.UserName)).SendAsync("PackageNotice", n.Notice, ct);
    }
}
