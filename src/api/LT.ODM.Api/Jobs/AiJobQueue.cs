using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.Options;

namespace LT.ODM.Api.Jobs;

/// <summary>
/// The work of a job: the same code the endpoint ran inside the request, given its own services (a new scope) and the job's
/// cancellation. Returns the answer the endpoint used to return.
/// </summary>
public delegate Task<object?> AiJobWork(IServiceProvider services, CancellationToken ct);

/// <summary>
/// Runs slow AI work in the background (one API server): an endpoint queues the work and answers 202 with the job id at
/// once; AiJobQueue runs it on its lane (AiJobs:TextWorkers / ImageWorkers at a time, default 4 each; the AI call limits
/// then decide how many reach each AI server), records the result in ai.Job and keeps how far a text answer has got. While
/// the AI call limits are on, a user may have at most ai.Policy.MaxTasksPerUser tasks queued or running. The work is
/// held in memory: a job still queued or running when the API stops is marked Interrupted at the next start, never re-run.
/// Finished jobs are deleted after ai.Policy.JobKeepDays (checked at start and every 6 hours).
/// </summary>
public sealed class AiJobQueue(IServiceScopeFactory scopes, IOptions<AiJobOptions> options, ILogger<AiJobQueue> logger) : BackgroundService
{
    private sealed record Pending(int JobId, string OwnerId, AiJobWork Work);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, Channel<Pending>> _lanes = new()
    {
        [AiJobLanes.Text] = Channel.CreateUnbounded<Pending>(),
        [AiJobLanes.Image] = Channel.CreateUnbounded<Pending>(),
    };
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<int, AiProgressEntry> _progress = new();
    /// <summary>Each owner's tasks queued or running here (the per-user limit).</summary>
    private readonly Dictionary<string, int> _active = new(StringComparer.Ordinal);

    /// <summary>
    /// Records the job (Queued) and hands the work to its lane. AiJobLimitException when the AI call limits are on and the
    /// owner already has MaxTasksPerUser tasks queued or running.
    /// </summary>
    public async Task<int> QueueAsync(string kind, string lane, AiJobOwner owner, object? input, AiJobWork work, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var policy = await scope.ServiceProvider.GetRequiredService<IAiConnectionResolver>().PolicyAsync(ct);
        lock (_active)
        {
            var mine = _active.GetValueOrDefault(owner.Id);
            if (policy.LimitCalls && mine >= policy.MaxTasksPerUser) throw new AiJobLimitException(policy.MaxTasksPerUser);
            _active[owner.Id] = mine + 1;
        }
        try
        {
            var jobs = scope.ServiceProvider.GetRequiredService<IAiJobRepository>();
            var id = await jobs.AddAsync(kind, lane, owner, input is null ? null : JsonSerializer.Serialize(input, Json), ct);
            await _lanes[lane].Writer.WriteAsync(new Pending(id, owner.Id, work), ct);
            return id;
        }
        catch
        {
            Done(owner.Id);
            throw;
        }
    }

    /// <summary>Stops a running job's work (its row is marked Cancelled by the caller). False when it is not running here.</summary>
    public bool Stop(int jobId)
    {
        if (!_running.TryGetValue(jobId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    /// <summary>How far a running text job's answer has got (null when not running or no text streamed yet).</summary>
    public AiProgressDto? Progress(int jobId) => _progress.TryGetValue(jobId, out var p) ? p.Snapshot() : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IAiJobRepository>();
            var interrupted = await jobs.InterruptAsync(stoppingToken);
            if (interrupted > 0) logger.LogWarning("{Count} AI jobs were left unfinished by the last run and are marked Interrupted", interrupted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An older database without ai.Job: the endpoints that queue jobs fail with a clear error instead.
            logger.LogError(ex, "AI jobs could not be checked at start (is db/procedures/ai.jobs.procedures.sql deployed?)");
        }

        var o = options.Value;
        var workers = new List<Task> { PurgeLoopAsync(stoppingToken) };
        workers.AddRange(Enumerable.Range(0, Math.Clamp(o.TextWorkers, 1, 16)).Select(_ => LaneAsync(_lanes[AiJobLanes.Text], stoppingToken)));
        workers.AddRange(Enumerable.Range(0, Math.Clamp(o.ImageWorkers, 1, 16)).Select(_ => LaneAsync(_lanes[AiJobLanes.Image], stoppingToken)));
        await Task.WhenAll(workers);
    }

    private async Task LaneAsync(Channel<Pending> lane, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in lane.Reader.ReadAllAsync(stoppingToken)) await RunAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunAsync(Pending job, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var scope = scopes.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IAiJobRepository>();
        string status, error = "";
        string? result = null;
        int? errorStatus = null;
        try
        {
            if (!await jobs.StartAsync(job.JobId, stoppingToken)) return;   // cancelled while it waited
            _running[job.JobId] = cts;
            var progress = new AiProgressEntry();
            _progress[job.JobId] = progress;
            AiProgress.Follow(progress);   // text answers report how far they have got, and the AI server's queue its place
            AiCallContext.Set(new AiCaller(job.OwnerId, AiCallPriority.Background));
            var answer = await job.Work(scope.ServiceProvider, cts.Token);
            result = answer is null ? null : JsonSerializer.Serialize(answer, answer.GetType(), Json);
            status = AiJobStatuses.Done;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;   // the API is stopping: Interrupted at the next start
        }
        catch (OperationCanceledException)
        {
            status = AiJobStatuses.Cancelled;
        }
        catch (Exception ex)
        {
            (status, errorStatus, error) = ex switch
            {
                StyleRuleException r => (AiJobStatuses.Failed, r.StatusCode, r.Message),
                AiServiceException a => (AiJobStatuses.Failed, StatusCodes.Status502BadGateway, a.Message),
                _ => (AiJobStatuses.Failed, StatusCodes.Status500InternalServerError, "The job failed on the server. Try again; if it fails again, tell IT."),
            };
            if (errorStatus == StatusCodes.Status500InternalServerError) logger.LogError(ex, "AI job {JobId} failed", job.JobId);
        }
        finally
        {
            AiProgress.Follow(null);
            AiCallContext.Set(null);
            _running.TryRemove(job.JobId, out _);
            _progress.TryRemove(job.JobId, out _);
            Done(job.OwnerId);
        }

        try
        {
            await jobs.FinishAsync(job.JobId, status, result, error.Length == 0 ? null : error, errorStatus, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI job {JobId} finished ({Status}) but could not be recorded", job.JobId, status);
        }
    }

    private void Done(string ownerId)
    {
        lock (_active)
        {
            if (_active.TryGetValue(ownerId, out var n) && n > 1) _active[ownerId] = n - 1;
            else _active.Remove(ownerId);
        }
    }

    private async Task PurgeLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<IAiJobRepository>().PurgeAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Deleted {Count} finished AI jobs past their keep time", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Old AI jobs could not be deleted");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
