using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Business unit teams: style.usp_EditScope_Get (db/procedures/style.procedures.sql).</summary>
public sealed class StyleEditScopeRepository(IDbConnectionFactory connectionFactory) : IStyleEditScopeRepository
{
    public async Task<IReadOnlyList<string>> GetBusinessUnitsAsync(string userName, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<string>(new CommandDefinition("style.usp_EditScope_Get", new { UserName = userName },
            commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
    }
}
