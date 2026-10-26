using System.Text.Json;
using LT.ODM.Api.Jobs;
using LT.ODM.Application.Admin;
using LT.ODM.Application.Ai;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// AI jobs (slow AI work run in the background, AiJobQueue). Owners follow, cancel and dismiss their own jobs; Admins see
/// and cancel everyone's (Settings > AI connections > Jobs) and set how long finished jobs are kept.
/// </summary>
[ApiController]
[Authorize]
public sealed class AiJobsController(IAiJobRepository jobs, AiJobQueue queue, LT.ODM.Infrastructure.Ai.AiCallLimiter limiter) : ControllerBase
{
    private bool IsAdmin => User.IsInRole(AdminValidation.AdminRole);

    /// <summary>The caller's jobs for the AI task tray: running, or finished and not yet shown (last 2 days).</summary>
    [HttpGet("api/v1/ai/jobs")]
    public async Task<IReadOnlyList<AiJobDto>> Mine(CancellationToken ct)
        => (await jobs.MineAsync(this.UserId(), ct)).Select(Dto).ToList();

    /// <summary>One job with its answer (owner or Admin). Running text jobs carry how far the answer has got.</summary>
    [HttpGet("api/v1/ai/jobs/{jobId:int}")]
    public async Task<IActionResult> Get(int jobId, CancellationToken ct)
        => await Visible(jobId, ct) is { } row ? Ok(Dto(row)) : NotFound();

    /// <summary>Stops a queued or running job (owner or Admin). 409 when it has already finished.</summary>
    [HttpPost("api/v1/ai/jobs/{jobId:int}/cancel")]
    public async Task<IActionResult> Cancel(int jobId, CancellationToken ct)
    {
        if (await Visible(jobId, ct) is null) return NotFound();
        if (!await jobs.CancelAsync(jobId, this.UserName(), ct)) return Problem(statusCode: StatusCodes.Status409Conflict, title: "The job has already finished.");
        queue.Stop(jobId);
        return NoContent();
    }

    /// <summary>The owner's page has shown the answer (or the tray dismissed it): it is not offered again after a reload.</summary>
    [HttpPost("api/v1/ai/jobs/{jobId:int}/seen")]
    public async Task<IActionResult> Seen(int jobId, CancellationToken ct)
    {
        await jobs.SeenAsync(jobId, this.UserId(), ct);
        return NoContent();
    }

    /// <summary>Admin: every job, newest first; status = Active (queued or running) or one status; and each AI server's load now.</summary>
    [HttpGet("api/v1/admin/ai/jobs")]
    [Authorize(Roles = AdminValidation.AdminRole)]
    public async Task<AiJobPageDto> List(string? status, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var filter = status == AiJobStatuses.Active ? status : AiJobStatuses.All.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
        var (items, total) = await jobs.ListAsync(filter, Math.Max(0, skip), Math.Clamp(take, 1, 200), ct);
        return new AiJobPageDto(items.Select(Dto).ToList(), total, await jobs.KeepDaysAsync(ct), limiter.Load());
    }

    /// <summary>Admin: how many days finished jobs are kept (1-365, default 7).</summary>
    [HttpPut("api/v1/admin/ai/jobs/keep-days")]
    [Authorize(Roles = AdminValidation.AdminRole)]
    public async Task<IActionResult> SaveKeepDays(SaveJobKeepDaysRequest request, CancellationToken ct)
    {
        if (request.Days is < 1 or > 365)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["days"] = ["Keep finished jobs for 1 to 365 days."] }));
        try
        {
            await jobs.SaveKeepDaysAsync(request.Days, this.UserName(), ct);
            return NoContent();
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
    }

    /// <summary>The job when the caller owns it or is an Admin; null otherwise (answered as 404, so ids reveal nothing).</summary>
    private async Task<AiJobRow?> Visible(int jobId, CancellationToken ct)
        => await jobs.GetAsync(jobId, ct) is { } row && (row.OwnerId == this.UserId() || IsAdmin) ? row : null;

    private AiJobDto Dto(AiJobRow r)
    {
        JsonElement? result = null;
        if (r.Result is { Length: > 0 } json)
        {
            using var doc = JsonDocument.Parse(json);
            result = doc.RootElement.Clone();
        }
        return new AiJobDto(r.JobId, r.Kind, r.Lane, r.Status, r.OwnerName, r.TaskKey, r.Label, r.Url, r.Error, r.ErrorStatus, r.Seen,
            r.CreatedUtc, r.StartedUtc, r.FinishedUtc, r.CancelledBy, result, r.Status == AiJobStatuses.Running ? queue.Progress(r.JobId) : null);
    }
}
