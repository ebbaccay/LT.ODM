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

/// <summary>Images on disk in one folder under Files:Root, named by a random id so names cannot be guessed or chosen.</summary>
public abstract partial class FileSystemImageStore(IOptions<FileStorageOptions> options, string folderName) : IImageStore
{
    private readonly string _folder = Path.GetFullPath(Path.Combine(options.Value.Root, folderName));

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|gif|webp)$")]
    private static partial Regex FileNameRegex();

    public async Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_folder);
        var name = Guid.NewGuid().ToString("N") + ConceptStudioRules.Extension(kind);
        await File.WriteAllBytesAsync(Path.Combine(_folder, name), content, ct);
        return name;
    }

    public (Stream Content, string ContentType)? Open(string fileName)
    {
        if (!FileNameRegex().IsMatch(fileName)) return null;
        var path = Path.Combine(_folder, fileName);
        if (!File.Exists(path)) return null;
        return (File.OpenRead(path), ConceptStudioRules.ContentType(Path.GetExtension(fileName)));
    }
}

/// <summary>Concept Studio inspiration images (Files:Root/concept-inspiration).</summary>
public sealed class FileSystemConceptImageStore(IOptions<FileStorageOptions> options)
    : FileSystemImageStore(options, "concept-inspiration"), IConceptImageStore;

/// <summary>SBU product photos (Files:Root/sbu-products).</summary>
public sealed class FileSystemProductImageStore(IOptions<FileStorageOptions> options)
    : FileSystemImageStore(options, "sbu-products"), IProductImageStore;

/// <summary>Style Library sketches and photos of styles, colorways and BOM lines (Files:Root/style-library).</summary>
public sealed class FileSystemStyleImageStore(IOptions<FileStorageOptions> options)
    : FileSystemImageStore(options, "style-library"), IStyleImageStore;
