using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Material colour suggestions: style.usp_ColourSuggest_Sources / usp_BomLineColour_Fill (style.bom-suggest.procedures.sql).</summary>
public sealed class ColourSuggestionRepository(IDbConnectionFactory connectionFactory) : IColourSuggestionRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(IReadOnlyList<ColourCell> Cells, IReadOnlyList<ColourByName> ByName, IReadOnlyList<ColourByMaterial> ByMaterial)?> SourcesAsync(
        int styleId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            using var grid = await conn.QueryMultipleAsync(Proc("style.usp_ColourSuggest_Sources", new { StyleId = styleId }, ct));
            return ((await grid.ReadAsync<ColourCell>()).ToList(), (await grid.ReadAsync<ColourByName>()).ToList(), (await grid.ReadAsync<ColourByMaterial>()).ToList());
        }
        catch (SqlException ex) when (ex.Number == 50404)
        {
            return null;
        }
    }

    public async Task<int> ApplyAsync(int styleId, IReadOnlyList<ColourCellRequest> cells, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("style.usp_BomLineColour_Fill",
            new { StyleId = styleId, Cells = JsonSerializer.Serialize(cells, Json), ChangedBy = changedBy }, ct));
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 120);
}
