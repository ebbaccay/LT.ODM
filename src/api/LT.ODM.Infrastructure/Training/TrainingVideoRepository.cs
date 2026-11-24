using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Application.Training;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Training;

/// <summary>
/// Help > Video tutorials: hidden / deleted marks (nav.usp_TrainingVideoState_*) and edited details / uploaded videos
/// (nav.usp_TrainingVideo_*), db/procedures/admin.procedures.sql.
/// </summary>
public sealed class TrainingVideoRepository(IDbConnectionFactory connectionFactory) : ITrainingVideoRepository
{
    public async Task<IReadOnlyList<TrainingVideoState>> ListStatesAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<StateRow>(new CommandDefinition("nav.usp_TrainingVideoState_List", commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(r => new TrainingVideoState(r.FileName, r.State, r.ChangedBy, Utc(r.ChangedUtc))).ToList();
    }

    public Task SetStateAsync(string fileName, string? state, string changedBy, CancellationToken ct = default)
        => ExecAsync("nav.usp_TrainingVideoState_Set", new { FileName = fileName, State = state, ChangedBy = changedBy }, ct);

    public async Task<IReadOnlyList<TrainingVideoDetails>> ListDetailsAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<DetailsRow>(new CommandDefinition("nav.usp_TrainingVideo_List", commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(r => new TrainingVideoDetails(r.FileName, r.Title, r.Description, r.DurationSeconds, r.Roles, r.UploadFile, r.CreatedBy,
            Utc(r.CreatedUtc), r.UpdatedBy, r.UpdatedUtc is { } u ? Utc(u) : null)).ToList();
    }

    public Task SaveDetailsAsync(string fileName, string? title, string? description, int? durationSeconds, string? roles, string? uploadFile,
        string changedBy, CancellationToken ct = default)
        => ExecAsync("nav.usp_TrainingVideo_Save", new
        {
            FileName = fileName, Title = title, Description = description, DurationSeconds = durationSeconds, Roles = roles, UploadFile = uploadFile,
            ChangedBy = changedBy,
        }, ct);

    public Task DeleteAsync(string fileName, CancellationToken ct = default) => ExecAsync("nav.usp_TrainingVideo_Delete", new { FileName = fileName }, ct);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

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

    private sealed class StateRow
    {
        public string FileName { get; init; } = "";
        public string State { get; init; } = "";
        public string ChangedBy { get; init; } = "";
        public DateTime ChangedUtc { get; init; }
    }

    private sealed class DetailsRow
    {
        public string FileName { get; init; } = "";
        public string? Title { get; init; }
        public string? Description { get; init; }
        public int? DurationSeconds { get; init; }
        public string? Roles { get; init; }
        public string? UploadFile { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
    }
}
