using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>AI jobs: ai.usp_Job_* (db/procedures/ai.jobs.procedures.sql).</summary>
public sealed class AiJobRepository(IDbConnectionFactory connectionFactory) : IAiJobRepository
{
    public async Task<int> AddAsync(string kind, string lane, AiJobOwner owner, string? input, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("ai.usp_Job_Add", new
        {
            Kind = kind, Lane = lane, OwnerName = owner.Name, OwnerId = owner.Id, owner.TaskKey, owner.Label, owner.Url, Input = input,
        }, ct));
    }

    public async Task<bool> StartAsync(int jobId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<bool>(Proc("ai.usp_Job_Start", new { JobId = jobId }, ct));
    }

    public async Task FinishAsync(int jobId, string status, string? result, string? error, int? errorStatus, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("ai.usp_Job_Finish", new
        {
            JobId = jobId, Status = status, Result = result, Error = error is { Length: > 1000 } ? error[..1000] : error, ErrorStatus = (short?)errorStatus,
        }, ct));
    }

    public async Task<AiJobRow?> GetAsync(int jobId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QuerySingleOrDefaultAsync<Row>(Proc("ai.usp_Job_Get", new { JobId = jobId }, ct)))?.ToRow();
    }

    public async Task<IReadOnlyList<AiJobRow>> MineAsync(string ownerId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<Row>(Proc("ai.usp_Job_Mine", new { OwnerId = ownerId }, ct))).Select(r => r.ToRow()).ToList();
    }

    public async Task<(IReadOnlyList<AiJobRow> Items, int Total)> ListAsync(string? status, int skip, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<Row>(Proc("ai.usp_Job_List", new { Status = status, Skip = skip, Take = take }, ct))).ToList();
        return (rows.Select(r => r.ToRow()).ToList(), rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task SeenAsync(int jobId, string ownerId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("ai.usp_Job_Seen", new { JobId = jobId, OwnerId = ownerId }, ct));
    }

    public async Task<bool> CancelAsync(int jobId, string cancelledBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<bool>(Proc("ai.usp_Job_Cancel", new { JobId = jobId, CancelledBy = cancelledBy }, ct));
    }

    public async Task<int> InterruptAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("ai.usp_Job_Interrupt", null, ct));
    }

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QuerySingleAsync<(int Deleted, int KeepDays)>(Proc("ai.usp_Job_Purge", null, ct))).Deleted;
    }

    public async Task<int> KeepDaysAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("ai.usp_Job_KeepDays_Get", null, ct));
    }

    public async Task SaveKeepDaysAsync(int days, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            await conn.ExecuteAsync(Proc("ai.usp_Job_KeepDays_Save", new { Days = (short)days, ChangedBy = changedBy }, ct));
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number == 50404 ? 404 : 400, ex.Message);
        }
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed class Row
    {
        public int JobId { get; init; }
        public string Kind { get; init; } = "";
        public string Lane { get; init; } = "";
        public string Status { get; init; } = "";
        public string OwnerName { get; init; } = "";
        public string OwnerId { get; init; } = "";
        public string? TaskKey { get; init; }
        public string? Label { get; init; }
        public string? Url { get; init; }
        public string? Input { get; init; }
        public string? Result { get; init; }
        public string? Error { get; init; }
        public short? ErrorStatus { get; init; }
        public bool Seen { get; init; }
        public DateTime CreatedUtc { get; init; }
        public DateTime? StartedUtc { get; init; }
        public DateTime? FinishedUtc { get; init; }
        public string? CancelledBy { get; init; }
        public int TotalCount { get; init; }

        public AiJobRow ToRow() => new(JobId, Kind, Lane, Status, OwnerName, OwnerId, TaskKey, Label, Url, Input, Result, Error, ErrorStatus,
            Seen, Utc(CreatedUtc), StartedUtc is { } s ? Utc(s) : null, FinishedUtc is { } f ? Utc(f) : null, CancelledBy);
    }
}
