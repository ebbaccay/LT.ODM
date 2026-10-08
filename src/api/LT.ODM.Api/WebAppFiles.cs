using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace LT.ODM.Api;

/// <summary>Serving the Angular build (wwwroot): content types and cache rules.</summary>
internal static partial class WebAppFiles
{
    public static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var p = new FileExtensionContentTypeProvider();
        p.Mappings[".webmanifest"] = "application/manifest+json";
        return p;
    }

    /// <summary>Angular puts a content hash in bundle names (main-ABCD1234.js, chunk-ABCD1234.js, styles-ABCD1234.css).</summary>
    [GeneratedRegex(@"-[A-Z0-9]{8}\.(js|css)$")]
    private static partial Regex HashedBundle();

    /// <summary>Hashed bundles never change: cache for a year. Everything else (index.html, ngsw.json, i18n, icons): re-check each time.</summary>
    public static string CacheControl(string fileName)
        => HashedBundle().IsMatch(fileName) ? "public, max-age=31536000, immutable" : "no-cache";
}
