using LT.ODM.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Serves client-side settings from configuration (user secrets / environment variables) so they stay out of source control.
/// Signed-in users only. The AG Grid key ends up in the browser anyway; this only keeps it out of the repository.
/// </summary>
[ApiController]
[Route("api/v1/client-config")]
public sealed class ClientConfigController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get()
        => Ok(new ClientConfigDto(configuration["AgGrid:LicenseKey"] ?? string.Empty));
}
