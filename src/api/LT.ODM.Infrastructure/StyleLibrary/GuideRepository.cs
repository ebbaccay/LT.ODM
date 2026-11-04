using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Library assistant how-to guides: ai.usp_Guide_* (db/procedures/ai.assistant.procedures.sql).</summary>
public sealed class GuideRepository(IDbConnectionFactory connectionFactory) : IGuideRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GuideDto>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition("ai.usp_Guide_List", commandType: CommandType.StoredProcedure, cancellationToken: ct));
        var rows = (await grid.ReadAsync<Row>()).ToList();
        var questions = (await grid.ReadAsync<(string? Code, string Question)>()).Where(q => q.Code is not null)
            .GroupBy(q => q.Code!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(q => q.Question).ToList(), StringComparer.OrdinalIgnoreCase);
        return rows.Select(r => new GuideDto(r.Code, r.Title, r.Who, r.Intro, Steps(r.Steps), r.IsActive, questions.GetValueOrDefault(r.Code) ?? [],
            r.ChangedBy, DateTime.SpecifyKind(r.ChangedUtc, DateTimeKind.Utc), r.ChangedHow)).ToList();
    }

    public async Task SaveAsync(GuideDto guide, IReadOnlyList<GuideQuestionKey> questions, string changedBy, CancellationToken ct = default)
        => await ExecAsync("ai.usp_Guide_Save", new
        {
            guide.Code, guide.Title, guide.Who, guide.Intro, Steps = JsonSerializer.Serialize(guide.Steps, Json), guide.IsActive,
            Questions = JsonSerializer.Serialize(questions, Json), ChangedBy = changedBy,
        }, ct);

    public Task DeleteAsync(string code, CancellationToken ct = default) => ExecAsync("ai.usp_Guide_Delete", new { Code = code }, ct);

    private static IReadOnlyList<GuideStepDto> Steps(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GuideStepDto>>(json, Json)?.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

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
        public string Code { get; init; } = "";
        public string Title { get; init; } = "";
        public string? Who { get; init; }
        public string? Intro { get; init; }
        public string Steps { get; init; } = "[]";
        public bool IsActive { get; init; }
        public string? ChangedBy { get; init; }
        public DateTime ChangedUtc { get; init; }
        public string? ChangedHow { get; init; }
    }
}
