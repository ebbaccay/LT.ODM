using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Dtos;

namespace LT.ODM.Infrastructure.Repositories;

public sealed class HealthRepository(IDbConnectionFactory connectionFactory) : IHealthRepository
{
    public async Task<HealthPingDto> PingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            "dbo.usp_Health_Ping",
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<HealthPingDto>(command);
    }
}
