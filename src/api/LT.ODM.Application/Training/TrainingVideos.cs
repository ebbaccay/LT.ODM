using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LT.ODM.Application.Training;

/// <summary>
/// Help > Video tutorials: narrated MP4 manuals shipped next to the API, plus what Admins change on the page (hidden /
/// deleted marks in nav.TrainingVideoState; edited details and uploaded videos in nav.TrainingVideo).
/// </summary>
public sealed class TrainingVideoOptions
{
    public const string SectionName = "TrainingVideos";

    /// <summary>Folder with the MP4 files and videos.json. Blank = &lt;API folder&gt;/training-videos (filled by build-release.ps1).</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Roles that may watch, comma-separated; "*" (the default) or blank = every signed-in user.</summary>
    public string Roles { get; set; } = "*";
}

/// <summary>
/// One video as the page lists it. Filled only for Admins (other users never get hidden or deleted videos, nor the
/// management details): <c>State</c> (Hidden / Deleted, with who and when), <c>Roles</c> (who may watch; empty = everyone),
/// <c>Source</c> (Release, Uploaded = added on the page, Replaced = a release video whose file was replaced on the page)
/// and <c>Edited</c> (a release video whose details were changed on the page).
/// </summary>
public sealed record TrainingVideoDto(string File, string Title, string? Description, int? DurationSeconds, long SizeBytes,
    string? State = null, string? StateBy = null, DateTime? StateUtc = null,
    IReadOnlyList<string>? Roles = null, string? Source = null, bool? Edited = null);

/// <summary>
/// The list for the page; <c>Available</c> is false when there are no videos the user may see.
/// <c>CanManage</c>: the user may add, edit, replace, hide, delete and restore videos (Admin).
/// </summary>
public sealed record TrainingVideoListDto(bool Available, IReadOnlyList<TrainingVideoDto> Videos, bool CanManage = false);

/// <summary>What an Admin did to a video: Hidden (only Admins see it) or Deleted (gone from the list; Admins can restore it).</summary>
public sealed record TrainingVideoState(string FileName, string State, string ChangedBy, DateTime ChangedUtc);

/// <summary>Body of PUT /api/v1/training-videos/{file}/state: "Hidden", "Deleted", or null to show the video again.</summary>
public sealed record TrainingVideoStateRequest(string? State);

/// <summary>
/// A row of nav.TrainingVideo: details that replace the release's (null = keep the release's; "" description = none),
/// <c>Roles</c> comma-separated ("" or "*" = everyone; null = the release's), and <c>UploadFile</c>, the stored name of a
/// file uploaded on the page (a new video, or the replacement of a release video).
/// </summary>
public sealed record TrainingVideoDetails(string FileName, string? Title, string? Description, int? DurationSeconds, string? Roles,
    string? UploadFile, string CreatedBy, DateTime CreatedUtc, string? UpdatedBy = null, DateTime? UpdatedUtc = null);

/// <summary>Body of PUT /api/v1/training-videos/{file}: the details shown on the page. <c>Roles</c> empty = everyone.</summary>
public sealed record TrainingVideoEditRequest(string? Title, string? Description, IReadOnlyList<string>? Roles, int? DurationSeconds);

/// <summary>nav.usp_TrainingVideoState_* and nav.usp_TrainingVideo_* (db/procedures/admin.procedures.sql).</summary>
public interface ITrainingVideoRepository
{
    Task<IReadOnlyList<TrainingVideoState>> ListStatesAsync(CancellationToken ct = default);

    /// <summary><paramref name="state"/> null = shown as usual.</summary>
    Task SetStateAsync(string fileName, string? state, string changedBy, CancellationToken ct = default);

    Task<IReadOnlyList<TrainingVideoDetails>> ListDetailsAsync(CancellationToken ct = default);

    /// <summary>Saves the whole row; a row with nothing in it is removed.</summary>
    Task SaveDetailsAsync(string fileName, string? title, string? description, int? durationSeconds, string? roles, string? uploadFile,
        string changedBy, CancellationToken ct = default);

    /// <summary>Removes the row and the hidden / deleted mark (an uploaded video deleted for good).</summary>
    Task DeleteAsync(string fileName, CancellationToken ct = default);
}

/// <summary>The files uploaded on the page (Files:Root/training-video-uploads), stored under random names.</summary>
public interface ITrainingVideoUploadStore
{
    /// <summary>Writes the video and returns its stored name ("&lt;32 hex&gt;.mp4").</summary>
    Task<string> SaveAsync(Stream content, CancellationToken ct = default);

    /// <summary>Full path of a stored video, or null when the name is not one of ours or the file is gone.</summary>
    string? PathOf(string? storedName);

    /// <summary>Removes a stored video; a file in use (being watched) is left for later, never an error.</summary>
    void Delete(string? storedName);
}

/// <summary>
/// Puts the page's list together from the release folder (<c>videos.json</c> for order, titles, descriptions, durations
/// and per video the roles that may watch; otherwise every MP4 by name), the files uploaded on the page and the details
/// Admins changed. A video with roles is listed and served only to those roles. A manifest that cannot be read hides
/// every release video (fail closed: a broken file never opens a limited video to everyone); videos uploaded on the page
/// still show. Only plain MP4 names are ever served, so a request cannot reach other files.
/// </summary>
public static partial class TrainingVideoCatalog
{
    public const string ManifestName = "videos.json";

    /// <summary>The role that may add, edit, replace, hide, delete and restore videos.</summary>
    public const string ManagerRole = "Admin";

    public const string Hidden = "Hidden", Deleted = "Deleted";
    public const string Release = "Release", Uploaded = "Uploaded", Replaced = "Replaced";

    /// <summary>Largest video that can be uploaded on the page (IIS takes requests up to about 105 MB).</summary>
    public const long MaxUploadBytes = 100L * 1024 * 1024;

    public const int MaxTitle = 200, MaxDescription = 1000, MaxRoles = 400;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,120}\.mp4$")]
    private static partial Regex SafeName();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotSlug();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record ManifestEntry(string? File, string? Title, string? Description, int? DurationSeconds, string[]? Roles);

    /// <summary>One video with everything known about it, before the user's rights are applied.</summary>
    private sealed record Entry(string File, string Path, string Title, string? Description, int? DurationSeconds, IReadOnlyList<string> Roles,
        string Source, bool Edited);

    public static bool IsSafeFileName(string? name) => name is not null && SafeName().IsMatch(name);

    /// <summary>"hidden" -> "Hidden"; null for anything else.</summary>
    public static string? ParseState(string? state)
        => string.Equals(state?.Trim(), Hidden, StringComparison.OrdinalIgnoreCase) ? Hidden
         : string.Equals(state?.Trim(), Deleted, StringComparison.OrdinalIgnoreCase) ? Deleted : null;

    /// <summary>True when the user has one of the configured roles; "*" or a blank setting = every signed-in user.</summary>
    public static bool CanWatch(string? roles, Func<string, bool> isInRole)
    {
        var list = SplitRoles(roles);
        return list.Count == 0 || list.Any(isInRole);
    }

    /// <summary>"Admin, Costing" -> [Admin, Costing]; "*" or blank = [] (everyone).</summary>
    public static IReadOnlyList<string> SplitRoles(string? roles)
    {
        var list = (roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Contains("*") ? [] : list;
    }

    /// <summary>[Admin, Costing] -> "Admin,Costing"; none -> "" (everyone).</summary>
    public static string JoinRoles(IEnumerable<string>? roles)
        => string.Join(',', SplitRoles(string.Join(',', roles ?? [])));

    private static bool MayWatch(IReadOnlyList<string> roles, Func<string, bool> isInRole) => roles.Count == 0 || roles.Any(isInRole);

    /// <summary>
    /// The videos the user may see. Hidden and deleted videos are left out, except for a manager (<paramref name="canManage"/>),
    /// who gets every video with its state, roles and source (also the ones limited to roles the manager does not have).
    /// </summary>
    public static TrainingVideoListDto List(string folder, Func<string, bool> isInRole,
        IReadOnlyCollection<TrainingVideoState>? states = null, bool canManage = false,
        IReadOnlyCollection<TrainingVideoDetails>? details = null, ITrainingVideoUploadStore? uploads = null)
    {
        var byFile = StatesByFile(states);
        var videos = new List<TrainingVideoDto>();
        foreach (var e in Entries(folder, details, uploads))
        {
            byFile.TryGetValue(e.File, out var st);
            if (!canManage && (st is not null || !MayWatch(e.Roles, isInRole))) continue;
            var size = new FileInfo(e.Path).Length;
            videos.Add(canManage
                ? new(e.File, e.Title, e.Description, e.DurationSeconds, size, st?.State, st?.ChangedBy, st?.ChangedUtc, e.Roles, e.Source, e.Edited)
                : new(e.File, e.Title, e.Description, e.DurationSeconds, size));
        }
        return new(videos.Count > 0, videos, canManage);
    }

    /// <summary>
    /// Full path of a video to stream, or null when it is not a video of the list, it is limited to roles the user does
    /// not have, or (for anyone but a manager) it is hidden or deleted.
    /// </summary>
    public static string? Resolve(string folder, string? fileName, Func<string, bool> isInRole,
        IReadOnlyCollection<TrainingVideoState>? states = null, bool canManage = false,
        IReadOnlyCollection<TrainingVideoDetails>? details = null, ITrainingVideoUploadStore? uploads = null)
    {
        if (!IsSafeFileName(fileName)) return null;
        var e = Entries(folder, details, uploads).FirstOrDefault(x => string.Equals(x.File, fileName, StringComparison.OrdinalIgnoreCase));
        if (e is null) return null;
        if (!canManage && (StatesByFile(states).ContainsKey(e.File) || !MayWatch(e.Roles, isInRole))) return null;
        return e.Path;
    }

    /// <summary>The source of a video in the list (Release, Uploaded, Replaced), or null when it is not one.</summary>
    public static string? SourceOf(string folder, string? fileName, IReadOnlyCollection<TrainingVideoDetails>? details = null,
        ITrainingVideoUploadStore? uploads = null)
        => IsSafeFileName(fileName)
            ? Entries(folder, details, uploads).FirstOrDefault(x => string.Equals(x.File, fileName, StringComparison.OrdinalIgnoreCase))?.Source
            : null;

    /// <summary>
    /// The release's own details of a video (title, description, length, roles), before any change on the page; null for
    /// a video added on the page (or not in the release).
    /// </summary>
    public static TrainingVideoDto? ReleaseDetails(string folder, string? fileName)
    {
        if (!IsSafeFileName(fileName)) return null;
        var e = Entries(folder, null, null).FirstOrDefault(x => string.Equals(x.File, fileName, StringComparison.OrdinalIgnoreCase));
        return e is null ? null : new(e.File, e.Title, e.Description, e.DurationSeconds, 0, Roles: e.Roles, Source: Release, Edited: false);
    }

    /// <summary>True when a release video file with this name is in the folder (whatever the manifest says).</summary>
    public static bool InReleaseFolder(string folder, string? fileName)
        => IsSafeFileName(fileName) && Directory.Exists(folder) && File.Exists(Path.Combine(folder, fileName!));

    /// <summary>
    /// A new file name for a video uploaded on the page, from its title ("Sewing room tour" -> "sewing-room-tour-3fa2c1.mp4"),
    /// never one already taken by a release file or another video.
    /// </summary>
    public static string NewFileName(string title, Func<string, bool> taken)
    {
        var slug = NotSlug().Replace(RemoveAccents(title).ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        if (slug.Length == 0) slug = "video";
        while (true)
        {
            var name = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}.mp4";
            if (!taken(name)) return name;
        }
    }

    /// <summary>True when the bytes start like an MP4 file (an "ftyp" box at offset 4).</summary>
    public static bool LooksLikeMp4(ReadOnlySpan<byte> head) => head.Length >= 8 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p';

    /// <summary>"LT-ODM-Sketch-to-Offer.mp4" -> "LT ODM Sketch to Offer".</summary>
    public static string TitleFromName(string fileName)
        => Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ').Replace('_', ' ').Trim();

    private static string RemoveAccents(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }

    private static Dictionary<string, TrainingVideoState> StatesByFile(IReadOnlyCollection<TrainingVideoState>? states)
    {
        var map = new Dictionary<string, TrainingVideoState>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in states ?? []) if (ParseState(st.State) is not null) map[st.FileName] = st;
        return map;
    }

    /// <summary>
    /// Every video, in the page's order: the manifest's, then release files the manifest does not list (by name), then
    /// videos uploaded on the page (oldest first). An uploaded file replaces the release file of the same name.
    /// </summary>
    private static List<Entry> Entries(string folder, IReadOnlyCollection<TrainingVideoDetails>? details, ITrainingVideoUploadStore? uploads)
    {
        var rows = new Dictionary<string, TrainingVideoDetails>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in details ?? []) if (IsSafeFileName(d.FileName)) rows[d.FileName] = d;
        string? UploadPath(string file) => rows.TryGetValue(file, out var d) && uploads?.PathOf(d.UploadFile) is { } p ? p : null;

        var release = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ManifestEntry> manifest = [];
        var manifestOk = Directory.Exists(folder) && TryReadManifest(folder, out manifest);
        if (!manifestOk) manifest = [];
        if (manifestOk)
            foreach (var path in Directory.GetFiles(folder, "*.mp4"))
                if (Path.GetFileName(path) is { } n && IsSafeFileName(n)) release[n] = path;

        var list = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string file, ManifestEntry? m)
        {
            if (!seen.Add(file)) return;
            var upload = UploadPath(file);
            var releasePath = release.GetValueOrDefault(file);
            if (upload is null && releasePath is null) return;   // listed but missing: skip
            rows.TryGetValue(file, out var d);
            var title = d?.Title is { Length: > 0 } t ? t : !string.IsNullOrWhiteSpace(m?.Title) ? m!.Title!.Trim() : TitleFromName(file);
            var description = d?.Description is not null ? NullIfBlank(d.Description) : NullIfBlank(m?.Description);
            var duration = d?.DurationSeconds is > 0 ? d.DurationSeconds : m?.DurationSeconds is > 0 ? m.DurationSeconds : null;
            var roles = d?.Roles is not null ? SplitRoles(d.Roles) : SplitRoles(string.Join(',', m?.Roles ?? []));
            var source = releasePath is null ? Uploaded : upload is null ? Release : Replaced;
            // A replaced file's length belongs to the new file, not an edit of the release's details.
            var edited = source != Uploaded && d is not null
                && (d.Title is not null || d.Description is not null || d.Roles is not null || (d.DurationSeconds is not null && source == Release));
            list.Add(new(file, upload ?? releasePath!, title, description, duration, roles, source, edited));
        }

        foreach (var m in manifest) if (IsSafeFileName(m.File)) Add(m.File!, m);
        foreach (var file in release.Keys.Order(StringComparer.OrdinalIgnoreCase)) Add(file, null);
        // Uploaded on the page: new videos (never a release file the broken manifest hides, so its roles cannot be skipped).
        foreach (var d in rows.Values.Where(d => d.UploadFile is not null).OrderBy(d => d.CreatedUtc))
            if (manifestOk || !InReleaseFolder(folder, d.FileName)) Add(d.FileName, null);
        return list;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>No manifest = an empty one (every MP4 by name); false when it exists but cannot be read.</summary>
    private static bool TryReadManifest(string folder, out IReadOnlyList<ManifestEntry> manifest)
    {
        manifest = [];
        var path = Path.Combine(folder, ManifestName);
        if (!File.Exists(path)) return true;
        try
        {
            manifest = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path), Json) ?? [];
            return true;
        }
        catch (JsonException) { return false; }   // fail closed: never open a limited video because the file is broken
    }
}
