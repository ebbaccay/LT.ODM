using System.Collections.Concurrent;
using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>BOM suggestions: style.usp_BomSuggest_* (db/procedures/style.bom-suggest.procedures.sql).</summary>
public sealed class BomSuggestionRepository(IDbConnectionFactory connectionFactory, BomSuggestFabricCache fabrics) : IBomSuggestionRepository
{
    public async Task<(BomSuggestTarget Target, IReadOnlyList<BomSuggestCandidate> Candidates)?> CandidatesAsync(int styleId, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            using var grid = await conn.QueryMultipleAsync(Proc("style.usp_BomSuggest_Candidates", new { StyleId = styleId, Take = take }, ct));
            var target = await grid.ReadSingleAsync<BomSuggestTarget>();
            return (target, (await grid.ReadAsync<BomSuggestCandidate>()).ToList());
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 50404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<BomSuggestSourceLine>> LinesAsync(IReadOnlyList<int> styleIds, CancellationToken ct = default)
    {
        if (styleIds.Count == 0) return [];
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<BomSuggestSourceLine>(Proc("style.usp_BomSuggest_Lines", new { StyleIds = string.Join(',', styleIds) }, ct))).ToList();
    }

    public Task<IReadOnlyList<BomSuggestFabric>> FabricsAsync(string customerCode, CancellationToken ct = default)
        => fabrics.GetAsync(customerCode, async token =>
        {
            await using var conn = await connectionFactory.OpenAsync(token);
            return (IReadOnlyList<BomSuggestFabric>)(await conn.QueryAsync<BomSuggestFabric>(
                Proc("style.usp_BomSuggest_Fabrics", new { CustomerCode = customerCode }, token))).ToList();
        }, ct);

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 120);
}

/// <summary>
/// The library's fabrics per customer for a few minutes (about 0.6 s to read; they change only when BOMs do). A
/// suggestion made within the window may miss a fabric added in it, which does not matter for a suggestion.
/// </summary>
public sealed class BomSuggestFabricCache(TimeProvider clock)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (IReadOnlyList<BomSuggestFabric> Value, DateTimeOffset LoadedAt)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<BomSuggestFabric>> GetAsync(string customerCode, Func<CancellationToken, Task<IReadOnlyList<BomSuggestFabric>>> load,
        CancellationToken ct)
    {
        if (_entries.TryGetValue(customerCode, out var hit) && clock.GetUtcNow() - hit.LoadedAt < CacheFor) return hit.Value;
        var value = await load(ct);
        _entries[customerCode] = (value, clock.GetUtcNow());
        return value;
    }
}
