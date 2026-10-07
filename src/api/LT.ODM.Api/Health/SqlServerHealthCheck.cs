using LT.ODM.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LT.ODM.Api.Health;

public sealed class SqlServerHealthCheck(IHealthRepository repository) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await repository.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy("SQL Server reachable");
        }
        catch (Exception ex)
        {
            // The /health response writer never returns exception detail.
            return HealthCheckResult.Unhealthy("SQL Server unreachable", ex);
        }
    }
}
