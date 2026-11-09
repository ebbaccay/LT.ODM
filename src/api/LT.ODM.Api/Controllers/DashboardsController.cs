using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Dashboards;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Home page dashboards. <c>mine</c>: which dashboard the signed-in user opens with (from their roles, Settings > Roles)
/// and which they may switch to; every signed-in user. <c>designer</c>: the user's own styles and what they still miss;
/// Style Library readers. The library overview stays at GET /api/v1/styles/dashboard.
/// </summary>
[ApiController]
[Route("api/v1/dashboards")]
[Authorize]
public sealed class DashboardsController(IAccessRepository access, IDesignerDashboardRepository designer) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    [HttpGet("mine")]
    public async Task<DashboardChoiceDto> Mine(CancellationToken ct)
        => DashboardChoice.Resolve(User.IsInRole, await access.GetRolesAsync(ct));

    /// <param name="season">One season code; blank = all seasons.</param>
    [HttpGet("designer")]
    [Authorize(Roles = StyleRoles.Readers)]
    public async Task<IActionResult> Designer([FromQuery] string? season, CancellationToken ct)
    {
        season = string.IsNullOrWhiteSpace(season) ? null : season.Trim();
        if (season is { Length: > 16 })
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["season"] = ["Use at most 16 characters."] }));
        var data = await designer.GetAsync(CurrentUserName, season, ct);
        return Ok(DesignerDashboard.Build(season, data));
    }
}
