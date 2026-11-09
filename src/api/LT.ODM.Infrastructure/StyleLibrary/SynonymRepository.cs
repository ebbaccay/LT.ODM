using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Library assistant synonyms: ai.usp_Synonym_* (db/procedures/ai.assistant.procedures.sql).</summary>
public sealed class SynonymRepository(IDbConnectionFactory connectionFactory) : ISynonymRepository
{
    public async Task<IReadOnlyList<SynonymDto>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition("ai.usp_Synonym_List", commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(r => new SynonymDto(r.SynonymId, r.Word, r.Kind, r.Value, r.ChangedBy, DateTime.SpecifyKind(r.ChangedUtc, DateTimeKind.Utc))).ToList();
    }

    public Task SaveAsync(string word, string kind, string value, string changedBy, CancellationToken ct = default)
        => ExecAsync("ai.usp_Synonym_Save", new { Word = word, Kind = kind, Value = value, ChangedBy = changedBy }, ct);

    public Task DeleteAsync(int synonymId, CancellationToken ct = default) => ExecAsync("ai.usp_Synonym_Delete", new { SynonymId = synonymId }, ct);

    /// <summary>THROW 50400 / 50404 -> StyleRuleException with that HTTP status.</summary>
    private async Task ExecAsync(string proc, object parameters, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(proc, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number == 50404 ? 404 : 400, ex.Message);
        }
    }

    private sealed class Row
    {
        public int SynonymId { get; init; }
        public string Word { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Value { get; init; } = "";
        public string? ChangedBy { get; init; }
        public DateTime ChangedUtc { get; init; }
    }
}
