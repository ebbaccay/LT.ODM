namespace LT.ODM.Application.Ai;

// AI jobs (ai.Job, db/procedures/ai.jobs.procedures.sql): slow AI work the API runs in the background instead of inside the
// web request, so a dropped connection, a browser limit or leaving the page loses nothing. The page gets a job id at once
// and follows it; the AI task tray shows the owner's jobs again after a reload; Admins see and cancel every job.

public static class AiJobStatuses
{
    public const string Queued = "Queued", Running = "Running", Done = "Done", Failed = "Failed", Cancelled = "Cancelled", Interrupted = "Interrupted";
    public static readonly string[] All = [Queued, Running, Done, Failed, Cancelled, Interrupted];
    /// <summary>List filter for Queued or Running.</summary>
    public const string Active = "Active";
}

/// <summary>
/// Text and image tasks are started separately (AiJobs:TextWorkers / ImageWorkers at a time). How many calls an AI server
/// then gets at once is the AI call limit (AiCallLimiter), shared with the calls made by web requests.
/// </summary>
public static class AiJobLanes
{
    public const string Text = "text", Image = "image";
}

public static class AiJobKinds
{
    public const string Render = "render", RenderPrompt = "renderPrompt", BomExplain = "bomExplain", CompareSummary = "compareSummary",
        MaterialRead = "materialRead", GapsAskAi = "gapsAskAi", ImportCodes = "importCodes";
}

/// <summary>
/// Settings section "AiJobs". Workers: AI tasks in progress at once per lane (1-16). They then wait their turn at the AI
/// server (AI call limits), so 4 lets several users' tasks take turns there; with the limits off, it is how many reach the
/// server at once.
/// </summary>
public sealed class AiJobOptions
{
    public const string SectionName = "AiJobs";
    public int TextWorkers { get; set; } = 4;
    public int ImageWorkers { get; set; } = 4;
}

/// <summary>A user already has as many AI tasks queued or running as the AI call limits allow (answered 429).</summary>
public sealed class AiJobLimitException(int limit)
    : Exception($"You already have {limit} AI tasks waiting or running. Wait for one to finish, or cancel one, then try again.")
{
    public int Limit { get; } = limit;
}

/// <summary>
/// A job. Result is the answer as JSON (what the request used to return); Error and ErrorStatus the problem it would have
/// answered with (e.g. 502 for an AI failure). Progress: how far a running text job's answer has got (null otherwise).
/// </summary>
public sealed record AiJobDto(
    int JobId, string Kind, string Lane, string Status, string OwnerName, string? TaskKey, string? Label, string? Url, string? Error,
    int? ErrorStatus, bool Seen, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? FinishedUtc, string? CancelledBy,
    System.Text.Json.JsonElement? Result = null, AiProgressDto? Progress = null);

/// <summary>Who asked for a job and what the page needs to show it again (tray key, label, page address).</summary>
public sealed record AiJobOwner(string Name, string Id, string? TaskKey, string? Label, string? Url);

/// <summary>A job as stored (Input / Result as JSON text).</summary>
public sealed record AiJobRow(
    int JobId, string Kind, string Lane, string Status, string OwnerName, string OwnerId, string? TaskKey, string? Label, string? Url,
    string? Input, string? Result, string? Error, int? ErrorStatus, bool Seen, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? FinishedUtc,
    string? CancelledBy);

/// <summary>Servers: what each AI server is doing now (AI call limits); empty when the limits are off or nothing was called yet.</summary>
public sealed record AiJobPageDto(IReadOnlyList<AiJobDto> Items, int Total, int KeepDays, IReadOnlyList<AiServerLoadDto>? Servers = null);

public sealed record SaveJobKeepDaysRequest(int Days);

/// <summary>ai.usp_Job_* (db/procedures/ai.jobs.procedures.sql).</summary>
public interface IAiJobRepository
{
    Task<int> AddAsync(string kind, string lane, AiJobOwner owner, string? input, CancellationToken ct = default);
    /// <summary>False when the job was cancelled before it started.</summary>
    Task<bool> StartAsync(int jobId, CancellationToken ct = default);
    Task FinishAsync(int jobId, string status, string? result, string? error, int? errorStatus, CancellationToken ct = default);
    Task<AiJobRow?> GetAsync(int jobId, CancellationToken ct = default);
    Task<IReadOnlyList<AiJobRow>> MineAsync(string ownerId, CancellationToken ct = default);
    Task<(IReadOnlyList<AiJobRow> Items, int Total)> ListAsync(string? status, int skip, int take, CancellationToken ct = default);
    Task SeenAsync(int jobId, string ownerId, CancellationToken ct = default);
    Task<bool> CancelAsync(int jobId, string cancelledBy, CancellationToken ct = default);
    Task<int> InterruptAsync(CancellationToken ct = default);
    Task<int> PurgeAsync(CancellationToken ct = default);
    Task<int> KeepDaysAsync(CancellationToken ct = default);
    Task SaveKeepDaysAsync(int days, string changedBy, CancellationToken ct = default);
}
