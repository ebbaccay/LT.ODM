using LT.ODM.Application.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Help > Video tutorials. Who may watch is the <c>TrainingVideos:Roles</c> setting (default "*" = every signed-in user), so
/// the videos can be limited to some roles without a release; the menu item's roles are set in Settings > Menu. Admins can
/// add videos (uploaded to Files:Root, kept across releases), edit any video's title, description and who may watch,
/// replace a video's file, hide a video (only Admins still see it), delete it from the list (Admins can restore it) and
/// delete an uploaded video for good. The release's own files are never touched.
/// </summary>
[ApiController]
[Route("api/v1/training-videos")]
[Authorize]
public sealed class TrainingVideosController(IOptions<TrainingVideoOptions> options, IWebHostEnvironment env, ITrainingVideoRepository videos,
    ITrainingVideoUploadStore uploads) : ControllerBase
{
    private const string Manager = TrainingVideoCatalog.ManagerRole;

    private string Folder => options.Value.Folder is { Length: > 0 } f
        ? Path.GetFullPath(Path.IsPathRooted(f) ? f : Path.Combine(env.ContentRootPath, f))
        : Path.Combine(env.ContentRootPath, "training-videos");

    private bool CanWatch => TrainingVideoCatalog.CanWatch(options.Value.Roles, User.IsInRole);

    private bool CanManage => User.IsInRole(Manager);

    private string UserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<TrainingVideoListDto>> List(CancellationToken ct)
    {
        if (!CanWatch && !CanManage) return Forbid();
        return TrainingVideoCatalog.List(Folder, User.IsInRole, await videos.ListStatesAsync(ct), CanManage, await videos.ListDetailsAsync(ct), uploads);
    }

    /// <summary>The MP4 itself; supports Range requests so players can seek. A video limited to other roles is 404.</summary>
    [HttpGet("{fileName}")]
    [DisableRateLimiting]   // players ask for the file in several ranges
    public async Task<IActionResult> Get(string fileName, CancellationToken ct)
    {
        if (!CanWatch && !CanManage) return Forbid();
        var path = TrainingVideoCatalog.Resolve(Folder, fileName, User.IsInRole, await videos.ListStatesAsync(ct), CanManage,
            await videos.ListDetailsAsync(ct), uploads);
        return path is null ? NotFound() : PhysicalFile(path, "video/mp4", enableRangeProcessing: true);
    }

    /// <summary>Admins: hide ("Hidden"), delete from the list ("Deleted") or show again (null / blank) a video of the list.</summary>
    [HttpPut("{fileName}/state")]
    [Authorize(Roles = Manager)]
    public async Task<IActionResult> SetState(string fileName, TrainingVideoStateRequest request, CancellationToken ct)
    {
        if (TrainingVideoCatalog.SourceOf(Folder, fileName, await videos.ListDetailsAsync(ct), uploads) is null) return NotFound();
        var state = TrainingVideoCatalog.ParseState(request.State);
        if (state is null && !string.IsNullOrWhiteSpace(request.State)) return Invalid("state", "Use Hidden, Deleted or none.");
        await videos.SetStateAsync(fileName, state, UserName, ct);
        return NoContent();
    }

    /// <summary>
    /// Admins: a new video (multipart: <c>file</c> MP4 up to 100 MB, <c>title</c>, <c>description</c>, <c>roles</c>
    /// comma-separated (blank = everyone), <c>durationSeconds</c> as the browser measured it). Returns the new file name.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Manager)]
    [DisableRateLimiting]
    [RequestSizeLimit(TrainingVideoCatalog.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = TrainingVideoCatalog.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> Add(IFormFile? file, [FromForm] string? title, [FromForm] string? description, [FromForm] string? roles,
        [FromForm] int? durationSeconds, CancellationToken ct)
    {
        if (CheckDetails(title, description, roles) is { } bad) return bad;
        if (await CheckFileAsync(file, ct) is { } badFile) return badFile;

        var details = await videos.ListDetailsAsync(ct);
        var name = TrainingVideoCatalog.NewFileName(title!.Trim(), n => TrainingVideoCatalog.InReleaseFolder(Folder, n)
            || details.Any(d => string.Equals(d.FileName, n, StringComparison.OrdinalIgnoreCase)));
        string stored;
        await using (var stream = file!.OpenReadStream()) stored = await uploads.SaveAsync(stream, ct);
        try
        {
            await videos.SaveDetailsAsync(name, title.Trim(), description?.Trim() ?? "", Duration(durationSeconds), TrainingVideoCatalog.JoinRoles([roles ?? ""]),
                stored, UserName, ct);
        }
        catch
        {
            uploads.Delete(stored);
            throw;
        }
        return Created($"/api/v1/training-videos/{name}", new { file = name });
    }

    /// <summary>Admins: the title, description, who may watch (empty = everyone) and length of any video of the list.</summary>
    [HttpPut("{fileName}")]
    [Authorize(Roles = Manager)]
    public async Task<IActionResult> Edit(string fileName, TrainingVideoEditRequest request, CancellationToken ct)
    {
        var details = await videos.ListDetailsAsync(ct);
        if (TrainingVideoCatalog.SourceOf(Folder, fileName, details, uploads) is null) return NotFound();
        var roles = TrainingVideoCatalog.JoinRoles(request.Roles);
        if (CheckDetails(request.Title, request.Description, roles) is { } bad) return bad;
        var row = Row(details, fileName);
        string? title = request.Title!.Trim();
        string? description = request.Description?.Trim() ?? "";
        string? roleList = roles;
        var duration = Duration(request.DurationSeconds) ?? row?.DurationSeconds;
        // A release video keeps only what differs from the release, so it is not "edited" when nothing changed.
        if (TrainingVideoCatalog.ReleaseDetails(Folder, fileName) is { } original)
        {
            if (title == original.Title) title = null;
            if (description == (original.Description ?? "")) description = null;
            if (duration == original.DurationSeconds) duration = null;
            if (roles == TrainingVideoCatalog.JoinRoles(original.Roles)) roleList = null;
        }
        await videos.SaveDetailsAsync(fileName, title, description, duration, roleList, row?.UploadFile, UserName, ct);
        return NoContent();
    }

    /// <summary>Admins: back to the release's title, description, length and roles (a release or replaced video).</summary>
    [HttpDelete("{fileName}/details")]
    [Authorize(Roles = Manager)]
    public async Task<IActionResult> ResetDetails(string fileName, CancellationToken ct)
    {
        var details = await videos.ListDetailsAsync(ct);
        var source = TrainingVideoCatalog.SourceOf(Folder, fileName, details, uploads);
        if (source is null) return NotFound();
        if (source == TrainingVideoCatalog.Uploaded) return Invalid("file", "A video added on the page has no release details to go back to.");
        var row = Row(details, fileName);
        // A replaced file keeps the length measured for it; the release's would not fit.
        await videos.SaveDetailsAsync(fileName, null, null, source == TrainingVideoCatalog.Replaced ? row?.DurationSeconds : null, null, row?.UploadFile, UserName, ct);
        return NoContent();
    }

    /// <summary>Admins: a new file for a video of the list (multipart: <c>file</c>, <c>durationSeconds</c>); the details stay.</summary>
    [HttpPost("{fileName}/file")]
    [Authorize(Roles = Manager)]
    [DisableRateLimiting]
    [RequestSizeLimit(TrainingVideoCatalog.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = TrainingVideoCatalog.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> Replace(string fileName, IFormFile? file, [FromForm] int? durationSeconds, CancellationToken ct)
    {
        var details = await videos.ListDetailsAsync(ct);
        if (TrainingVideoCatalog.SourceOf(Folder, fileName, details, uploads) is null) return NotFound();
        if (await CheckFileAsync(file, ct) is { } badFile) return badFile;
        var row = Row(details, fileName);
        string stored;
        await using (var stream = file!.OpenReadStream()) stored = await uploads.SaveAsync(stream, ct);
        try
        {
            // The release's length no longer fits the new file: keep the one measured for it (or none).
            await videos.SaveDetailsAsync(fileName, row?.Title, row?.Description, Duration(durationSeconds), row?.Roles, stored, UserName, ct);
        }
        catch
        {
            uploads.Delete(stored);
            throw;
        }
        uploads.Delete(row?.UploadFile);
        return NoContent();
    }

    /// <summary>Admins: drop the replacement of a release video, so the release's file plays again.</summary>
    [HttpDelete("{fileName}/file")]
    [Authorize(Roles = Manager)]
    public async Task<IActionResult> UndoReplace(string fileName, CancellationToken ct)
    {
        var details = await videos.ListDetailsAsync(ct);
        var source = TrainingVideoCatalog.SourceOf(Folder, fileName, details, uploads);
        if (source is null) return NotFound();
        if (source != TrainingVideoCatalog.Replaced) return Invalid("file", "Only a replaced release video can go back to its original file.");
        var row = Row(details, fileName)!;
        await videos.SaveDetailsAsync(fileName, row.Title, row.Description, null, row.Roles, null, UserName, ct);
        uploads.Delete(row.UploadFile);
        return NoContent();
    }

    /// <summary>Admins: delete a video added on the page for good (its file and everything kept for it). Release videos cannot.</summary>
    [HttpDelete("{fileName}")]
    [Authorize(Roles = Manager)]
    public async Task<IActionResult> DeleteForGood(string fileName, CancellationToken ct)
    {
        var details = await videos.ListDetailsAsync(ct);
        var source = TrainingVideoCatalog.SourceOf(Folder, fileName, details, uploads);
        if (source is null) return NotFound();
        if (source != TrainingVideoCatalog.Uploaded)
            return Invalid("file", "Release videos come back with every release: delete them from the list instead (they can be restored).");
        await videos.DeleteAsync(fileName, ct);
        uploads.Delete(Row(details, fileName)?.UploadFile);
        return NoContent();
    }

    private static TrainingVideoDetails? Row(IReadOnlyList<TrainingVideoDetails> details, string fileName)
        => details.FirstOrDefault(d => string.Equals(d.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    private static int? Duration(int? seconds) => seconds is > 0 and < 24 * 3600 ? seconds : null;

    private IActionResult? CheckDetails(string? title, string? description, string? roles)
    {
        if (string.IsNullOrWhiteSpace(title)) return Invalid("title", "Give the video a title.");
        if (title.Trim().Length > TrainingVideoCatalog.MaxTitle) return Invalid("title", "Keep the title under 200 characters.");
        if (description?.Trim().Length > TrainingVideoCatalog.MaxDescription) return Invalid("description", "Keep the description under 1,000 characters.");
        if (TrainingVideoCatalog.JoinRoles([roles ?? ""]).Length > TrainingVideoCatalog.MaxRoles) return Invalid("roles", "Too many roles.");
        return null;
    }

    private async Task<IActionResult?> CheckFileAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Invalid("file", "Choose the video file (.mp4).");
        if (file.Length > TrainingVideoCatalog.MaxUploadBytes) return Invalid("file", "The video must be 100 MB or smaller.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".mp4", StringComparison.OrdinalIgnoreCase)) return Invalid("file", "Only MP4 videos (.mp4) can be added.");
        var head = new byte[12];
        await using (var s = file.OpenReadStream()) await s.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        return TrainingVideoCatalog.LooksLikeMp4(head) ? null : Invalid("file", "This file is not an MP4 video.");
    }

    private IActionResult Invalid(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));
}
