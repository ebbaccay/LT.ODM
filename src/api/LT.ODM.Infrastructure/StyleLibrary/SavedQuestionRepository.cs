using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Library assistant saved questions: ai.usp_SavedQuestion_* (db/procedures/ai.assistant.procedures.sql).</summary>
public sealed class SavedQuestionRepository(IDbConnectionFactory connectionFactory) : ISavedQuestionRepository
{
    public async Task<SavedRuleDto?> UseRuleAsync(string questionKey, string? patternKey = null, string? patternKey2 = null, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var r = await conn.QuerySingleOrDefaultAsync<Row>(Proc("ai.usp_SavedQuestion_UseRule",
            new { QuestionKey = questionKey, PatternKey = patternKey, PatternKey2 = patternKey2 }, ct));
        return r is null ? null : new SavedRuleDto(r.SavedQuestionId, r.Question, r.Tool, Args(r.Arguments), r.IsPattern, r.QuestionKey);
    }

    public async Task<int> RecordAsync(string questionKey, string question, string tool, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("ai.usp_SavedQuestion_Record", new
        {
            QuestionKey = questionKey, Question = question, Tool = tool, Arguments = JsonSerializer.Serialize(arguments),
        }, ct));
    }

    public Task FeedbackAsync(int savedQuestionId, bool helpful, string tool, IReadOnlyDictionary<string, string> arguments, string changedBy, CancellationToken ct = default)
        => ExecAsync("ai.usp_SavedQuestion_Feedback", new
        {
            SavedQuestionId = savedQuestionId, Helpful = helpful, Tool = tool, Arguments = JsonSerializer.Serialize(arguments), ChangedBy = changedBy,
        }, ct);

    public async Task<PagedResult<SavedQuestionDto>> ListAsync(string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<Row>(Proc("ai.usp_SavedQuestion_List", new { Status = status, Search = search, Skip = skip, Take = take }, ct))).ToList();
        return new PagedResult<SavedQuestionDto>(rows.Select(r => new SavedQuestionDto(r.SavedQuestionId, r.Question, r.Tool, Args(r.Arguments), r.Status, r.AskCount,
            r.RuleHits, Utc(r.LastAskedUtc), Utc(r.CreatedUtc), r.ChangedBy, r.ChangedUtc is { } c ? Utc(c) : null, r.ChangedHow, r.IsPattern)).ToList(),
            rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<IReadOnlyList<string>> PopularAsync(int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<string>(Proc("ai.usp_SavedQuestion_Popular", new { Take = take }, ct))).ToList();
    }

    public Task SaveAsync(int savedQuestionId, string status, IReadOnlyDictionary<string, string> arguments, string changedBy, CancellationToken ct = default)
        => ExecAsync("ai.usp_SavedQuestion_Save", new
        {
            SavedQuestionId = savedQuestionId, Status = status, Arguments = JsonSerializer.Serialize(arguments), ChangedBy = changedBy,
        }, ct);

    public Task DeleteAsync(int savedQuestionId, CancellationToken ct = default)
        => ExecAsync("ai.usp_SavedQuestion_Delete", new { SavedQuestionId = savedQuestionId }, ct);

    /// <summary>THROW 50400 / 50404 -> StyleRuleException with that HTTP status.</summary>
    private async Task ExecAsync(string proc, object parameters, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            await conn.ExecuteAsync(Proc(proc, parameters, ct));
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number == 50404 ? 404 : 400, ex.Message);
        }
    }

    private static IReadOnlyDictionary<string, string> Args(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed class Row
    {
        public int SavedQuestionId { get; init; }
        public string Question { get; init; } = "";
        public string Tool { get; init; } = "";
        public string Arguments { get; init; } = "{}";
        public string Status { get; init; } = "";
        public int AskCount { get; init; }
        public int RuleHits { get; init; }
        public DateTime LastAskedUtc { get; init; }
        public DateTime CreatedUtc { get; init; }
        public string? ChangedBy { get; init; }
        public DateTime? ChangedUtc { get; init; }
        public string? ChangedHow { get; init; }
        public bool IsPattern { get; init; }
        public string? QuestionKey { get; init; }
        public int TotalCount { get; init; }
    }
}
