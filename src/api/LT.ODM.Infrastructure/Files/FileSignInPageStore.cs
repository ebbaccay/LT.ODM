using System.Text.Json;
using System.Text.RegularExpressions;
using LT.ODM.Application.SignInPage;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Files;

/// <summary>
/// Sign-in page appearance in Files:Root/sign-in-page.json (no database change needed). Written to a temporary file
/// and moved into place, so a reader never sees half a file; kept in memory after the first read.
/// </summary>
public sealed class FileSignInAppearanceStore(IOptions<FileStorageOptions> options) : ISignInAppearanceStore
{
    private sealed record Saved(SignInAppearance Appearance, string? UpdatedBy, DateTime? UpdatedUtc);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(Path.Combine(options.Value.Root, "sign-in-page.json"));
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Saved? _cached;

    public async Task<(SignInAppearance Appearance, string? UpdatedBy, DateTime? UpdatedUtc)> GetAsync(CancellationToken ct = default)
    {
        var saved = _cached ??= await ReadAsync(ct);
        return (saved.Appearance, saved.UpdatedBy, saved.UpdatedUtc);
    }

    public async Task SaveAsync(SignInAppearance appearance, string updatedBy, CancellationToken ct = default)
    {
        var saved = new Saved(appearance, updatedBy, DateTime.UtcNow);
        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + $".{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(saved, Json), ct);
            File.Move(temp, _path, overwrite: true);
            _cached = saved;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The defaults when nothing was saved yet, or the file cannot be read (the page must always show).</summary>
    private async Task<Saved> ReadAsync(CancellationToken ct)
    {
        try
        {
            if (File.Exists(_path)
                && JsonSerializer.Deserialize<Saved>(await File.ReadAllTextAsync(_path, ct), Json) is { Appearance: not null } saved
                && SignInPageRules.Validate(saved.Appearance).Count == 0)
                return saved;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }
        return new Saved(SignInAppearance.Default, null, null);
    }
}

/// <summary>Sign-in background pictures (Files:Root/sign-in-images), at most 2560 px. Served to anyone.</summary>
public sealed partial class FileSystemSignInImageStore(IOptions<FileStorageOptions> options)
    : FileSystemImageStore(options, Folder, SignInPageRules.MaxImageDimension), ISignInImageStore
{
    private const string Folder = "sign-in-images";
    private readonly string _folder = Path.GetFullPath(Path.Combine(options.Value.Root, Folder));

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|gif|webp)$")]
    private static partial Regex FileNameRegex();

    public void DeleteAllExcept(IReadOnlyCollection<string> keep)
    {
        if (!Directory.Exists(_folder)) return;
        foreach (var path in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (keep.Contains(name) || !FileNameRegex().IsMatch(name)) continue;
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Being served right now; it goes at the next save.
            }
        }
    }
}
