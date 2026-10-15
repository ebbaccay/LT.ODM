using System.Text.RegularExpressions;
using LT.ODM.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Files;

/// <summary>
/// Uploaded Style Library workbooks kept with their import (Files:Root/style-imports), so the columns can be mapped
/// later or again. Named by a random id; never served to browsers. Deleted with the import, on commit, or after 7 days.
/// </summary>
public sealed partial class FileSystemImportFileStore(IOptions<FileStorageOptions> options) : IImportFileStore
{
    private readonly string _folder = Path.GetFullPath(Path.Combine(options.Value.Root, "style-imports"));

    [GeneratedRegex(@"^[0-9a-f]{32}\.xlsx$")]
    private static partial Regex FileNameRegex();

    public async Task<string> SaveAsync(Stream content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_folder);
        var name = Guid.NewGuid().ToString("N") + ".xlsx";
        await using var file = File.Create(Path.Combine(_folder, name));
        await content.CopyToAsync(file, ct);
        return name;
    }

    public Stream? Open(string name)
    {
        if (!FileNameRegex().IsMatch(name)) return null;
        var path = Path.Combine(_folder, name);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(string name)
    {
        if (!FileNameRegex().IsMatch(name)) return;
        var path = Path.Combine(_folder, name);
        if (File.Exists(path)) File.Delete(path);
    }
}
