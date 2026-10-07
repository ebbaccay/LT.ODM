using LT.ODM.Application.Dtos;

namespace LT.ODM.Application.Abstractions;

public interface IHealthRepository
{
    Task<HealthPingDto> PingAsync(CancellationToken cancellationToken = default);
}
