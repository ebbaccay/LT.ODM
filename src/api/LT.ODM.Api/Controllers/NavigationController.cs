using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>Sidebar menu for the signed-in user (nav.* tables, filtered by the user's roles).</summary>
[ApiController]
[Route("api/v1/navigation")]
public sealed class NavigationController(IMenuRepository menu) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MenuDto>> Get(CancellationToken ct)
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return int.TryParse(sub, out var userId) ? Ok(await menu.GetForUserAsync(userId, ct)) : Unauthorized();
    }
}
