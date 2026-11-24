using System.Text.RegularExpressions;
using LT.ODM.Application.Training;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Files;

/// <summary>
/// Videos uploaded on Help > Video tutorials, in Files:Root/training-video-uploads (outside the API folder, so a release
/// never overwrites them), each under a random name. A replaced video gets a new file, so a file being watched is never
/// overwritten; the old one is removed when it is no longer in use.
/// </summary>
public sealed partial class FileSystemTrainingVideoStore(IOptions<FileStorageOptions> options) : ITrainingVideoUploadStore
{
    public const string FolderName = "training-video-uploads";

    private readonly string _folder = Path.GetFullPath(Path.Combine(options.Value.Root, FolderName));

    [GeneratedRegex(@"^[0-9a-f]{32}\.mp4$")]
    private static partial Regex StoredName();

    public async Task<string> SaveAsync(Stream content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_folder);
        var name = Guid.NewGuid().ToString("N") + ".mp4";
        var path = Path.Combine(_folder, name);
        var temp = path + ".part";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await content.CopyToAsync(file, ct);
            File.Move(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
        return name;
    }

    public string? PathOf(string? storedName)
    {
        if (storedName is null || !StoredName().IsMatch(storedName)) return null;
        var path = Path.Combine(_folder, storedName);
        return File.Exists(path) ? path : null;
    }

    public void Delete(string? storedName)
    {
        if (PathOf(storedName) is not { } path) return;
        try { File.Delete(path); }
        catch (IOException) { }                    // being watched right now: an orphan file, harmless
        catch (UnauthorizedAccessException) { }
    }
}
