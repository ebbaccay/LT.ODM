using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Suggest from picture: style.usp_PictureDescribe_Context (db/procedures/style.bom-suggest.procedures.sql).</summary>
public sealed class PictureDescribeRepository(IDbConnectionFactory connectionFactory) : IPictureDescribeRepository
{
    public async Task<(IReadOnlyList<PictureProductType> ProductTypes, IReadOnlyList<PictureNeighbourStyle> Neighbours)> ContextAsync(
        string? customerCode, IReadOnlyList<int> styleIds, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition("style.usp_PictureDescribe_Context",
            new { CustomerCode = customerCode, StyleIds = string.Join(',', styleIds) }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        var types = (await grid.ReadAsync<PictureProductType>()).ToList();
        // in the look-alike order
        var neighbours = (await grid.ReadAsync<PictureNeighbourStyle>()).OrderBy(n => styleIds.ToList().IndexOf(n.StyleId)).ToList();
        return (types, neighbours);
    }
}
