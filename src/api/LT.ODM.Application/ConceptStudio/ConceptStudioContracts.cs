using LT.ODM.Application.Ai;

namespace LT.ODM.Application.ConceptStudio;

/// <summary>Concept Studio inputs sent to the AI (all optional, like the TMS form).</summary>
public sealed record ConceptDraftRequest(
    string? Name, string? Client, string? Season, string? TargetMarket, string? TargetPrice, IReadOnlyList<string>? Trends);

/// <summary>AI concept brief: summary text plus suggested products ("Item - $Price"), fabric direction and sustainability notes.</summary>
public sealed record ConceptDraftDto(string Summary, IReadOnlyList<string> Products, IReadOnlyList<string> Fabrics, IReadOnlyList<string> Notes);

/// <summary>Writes a concept brief with an AI model (the Text job in Settings > AI connections; Gemini in TMS).</summary>
public interface IConceptDraftAi
{
    /// <summary>False when no API key is configured; the endpoint then answers 503.</summary>
    /// <summary>False when the Text job has no usable AI service (Settings > AI connections, else appsettings).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <exception cref="AiServiceException">The AI service failed or returned something unusable.</exception>
    Task<ConceptDraftDto> DraftAsync(ConceptDraftRequest request, CancellationToken ct = default);
}

/// <summary>The image could not be decoded (damaged, or not really an image).</summary>
public sealed class UnreadableImageException() : Exception("This image could not be read. Try another file.");

/// <summary>Uploaded images, stored outside the web root and served to signed-in users only.</summary>
public interface IImageStore
{
    /// <summary>
    /// Saves a validated image, made smaller first (scaled to the store's size limit, re-encoded, metadata removed),
    /// and returns its file name (32 hex characters + extension; the extension may differ from the input's).
    /// </summary>
    /// <exception cref="UnreadableImageException">The content cannot be decoded.</exception>
    Task<string> SaveAsync(byte[] content, ImageKind kind, CancellationToken ct = default);

    /// <summary>Opens a stored image, or null when the name is invalid or the file does not exist.</summary>
    (Stream Content, string ContentType)? Open(string fileName);
}

/// <summary>Inspiration-board images uploaded in Concept Studio.</summary>
public interface IConceptImageStore : IImageStore;

/// <summary>Product photos uploaded in the SBU overview (Products Offered).</summary>
public interface IProductImageStore : IImageStore;

public enum ImageKind { Jpeg, Png, Gif, WebP }

public static class ConceptStudioRules
{
    public const int MaxImageBytes = 10 * 1024 * 1024;

    public static Dictionary<string, string[]> Draft(ConceptDraftRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        void Max(string field, string? value, int max)
        {
            if ((value?.Length ?? 0) > max) errors[field] = [$"Use at most {max} characters."];
        }
        Max("name", r.Name, 200);
        Max("client", r.Client, 200);
        Max("season", r.Season, 100);
        Max("targetMarket", r.TargetMarket, 100);
        Max("targetPrice", r.TargetPrice, 50);
        if (r.Trends is { Count: > 30 } || r.Trends?.Any(t => t is null || t.Length > 64) == true)
            errors["trends"] = ["Use at most 30 trend tags of up to 64 characters each."];
        if (string.IsNullOrWhiteSpace(r.Name) && string.IsNullOrWhiteSpace(r.Client) && (r.Trends?.Count ?? 0) == 0)
            errors["name"] = ["Enter a concept name, a customer or at least one trend tag."];
        return errors;
    }

    /// <summary>Detects the image type from the file's first bytes (not from the name or the browser's content type).</summary>
    public static ImageKind? DetectImage(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ImageKind.Jpeg;
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ImageKind.Png;
        if (b.Length >= 6 && (b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8))) return ImageKind.Gif;
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return ImageKind.WebP;
        return null;
    }

    public static string Extension(ImageKind kind) => kind switch
    {
        ImageKind.Jpeg => ".jpg",
        ImageKind.Png => ".png",
        ImageKind.Gif => ".gif",
        _ => ".webp",
    };

    public static string ContentType(string extension) => extension switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        _ => "image/webp",
    };
}
