using System.Text.RegularExpressions;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Files;

/// <summary>Bound from "Files". Root defaults to App_Data/uploads under the API folder (outside the web root).</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "Files";

    /// <summary>Folder for uploaded files. On IIS the app pool identity needs modify rights on it.</summary>
    public string Root { get; set; } = "";
}

/// <summary>
/// Images on disk in one folder under Files:Root, named by a random id so names cannot be guessed or chosen.
/// Each image is made smaller before it is written (<see cref="ImageOptimizer"/>): longest side at most MaxDimension.
/// </summary>
public abstract partial class FileSystemImageStore(IOptions<FileStorageOptions> options, string folderName, int maxDimension) : IImageStore
{
    private readonly string _folder = Path.GetFullPath(Path.Combine(options.Value.Root, folderName));

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|gif|webp)$")]
    private static partial Regex FileNameRegex();

    public async Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default)
    {
        var (optimized, storedKind) = ImageOptimizer.Optimize(content, kind, maxDimension);
        Directory.CreateDirectory(_folder);
        var name = Guid.NewGuid().ToString("N") + ConceptStudioRules.Extension(storedKind);
        await File.WriteAllBytesAsync(Path.Combine(_folder, name), optimized, ct);
        return name;
    }

    public (Stream Content, string ContentType)? Open(string fileName)
    {
        if (!FileNameRegex().IsMatch(fileName)) return null;
        var path = Path.Combine(_folder, fileName);
        if (!File.Exists(path)) return null;
        return (File.OpenRead(path), ConceptStudioRules.ContentType(Path.GetExtension(fileName)));
    }

    /// <summary>
    /// Thumbnails live in &lt;folder&gt;/_thumbs/&lt;width&gt;/ under the original's name (the content may be PNG for a
    /// .jpg name when that is smaller, so the type is read from the bytes). Written to a temporary file and moved into
    /// place, so concurrent first requests never see half a file.
    /// </summary>
    public async Task<(Stream Content, string ContentType)?> OpenThumbnailAsync(string fileName, int width, CancellationToken ct = default)
    {
        if (!FileNameRegex().IsMatch(fileName) || !ImageRules.ThumbnailWidths.Contains(width)) return null;
        var original = Path.Combine(_folder, fileName);
        if (!File.Exists(original)) return null;
        if (width >= maxDimension) return Open(fileName);

        var folder = Path.Combine(_folder, "_thumbs", width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            var content = await File.ReadAllBytesAsync(original, ct);
            if (ConceptStudioRules.DetectImage(content) is not { } kind) return Open(fileName);
            byte[] small;
            try
            {
                (small, _) = ImageOptimizer.Optimize(content, kind, width);
            }
            catch (UnreadableImageException)
            {
                return Open(fileName);
            }
            Directory.CreateDirectory(folder);
            var temp = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temp, small, ct);
            File.Move(temp, path, overwrite: true);
        }
        var stream = File.OpenRead(path);
        var head = new byte[16];
        var read = await stream.ReadAsync(head, ct);
        stream.Position = 0;
        var type = ConceptStudioRules.DetectImage(head.AsSpan(0, read)) is { } k
            ? ConceptStudioRules.ContentType(ConceptStudioRules.Extension(k))
            : ConceptStudioRules.ContentType(Path.GetExtension(fileName));
        return (stream, type);
    }
}

// Size limits match what each screen already resizes to in the browser; the server applies them to anything that skips it.

/// <summary>Concept Studio inspiration images (Files:Root/concept-inspiration), at most 1200 px.</summary>
public sealed class FileSystemConceptImageStore(IOptions<FileStorageOptions> options)
    : FileSystemImageStore(options, "concept-inspiration", 1200), IConceptImageStore;

/// <summary>SBU product photos (Files:Root/sbu-products), at most 800 px.</summary>
public sealed class FileSystemProductImageStore(IOptions<FileStorageOptions> options)
    : FileSystemImageStore(options, "sbu-products", 800), IProductImageStore;

/// <summary>
/// Style Library sketches and photos of styles, colorways and BOM lines, and AI renders (Files:Root/style-library),
/// at most 1600 px: enough for the style page, proposals and printing, and for sketch details to stay readable.
/// </summary>
public sealed class FileSystemStyleImageStore(IOptions<FileStorageOptions> options)
    : FileSystemImageStore(options, "style-library", 1600), IStyleImageStore;
