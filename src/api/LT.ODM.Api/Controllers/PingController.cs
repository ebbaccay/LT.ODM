using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LT.ODM.Api.Controllers;

[ApiController]
[Route("api/v1/ping")]
public sealed class PingController(IHealthRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HealthPingDto>> Get(CancellationToken cancellationToken)
        => Ok(await repository.PingAsync(cancellationToken));
}
