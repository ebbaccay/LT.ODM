using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;

namespace LT.ODM.Application.StyleLibrary;

/// <summary>Garment views of one colour in the style's sketch: the colour (#rrggbb), the box suggested for a colorway, and every view.</summary>
public sealed record ColourGroupDto(int Index, string Colour, CropBoxDto Box, IReadOnlyList<CropBoxDto> Views);

/// <summary>
/// A colorway with the part of the sketch suggested for it. NameColour/NameWord: the colour read from the colorway
/// name (null = no known colour, e.g. "multicolor 095A"). Group/Box: the suggested colour group and box (null = no
/// match). Sure false = close call: check it.
/// </summary>
public sealed record ColorwayPictureSuggestionDto(
    int ColorwayId, string ColorwayCode, string? ColorwayName, string? ImageUrl, string? NameColour, string? NameWord,
    int? Group, CropBoxDto? Box, double? Distance, bool Sure);

/// <summary>Source: Sketch or Image (which of the style's pictures was split); Width/Height of that picture.</summary>
public sealed record ColorwayPicturesDto(
    string Source, string SourceUrl, int Width, int Height, IReadOnlyList<ColourGroupDto> Groups, IReadOnlyList<ColorwayPictureSuggestionDto> Colorways);

/// <summary>What <see cref="IColorwayPictureFinder"/> found for one colorway name.</summary>
public sealed record ColourNameMatch(string? NameColour, string? NameWord, int? Group, double? Distance, bool Sure);

/// <summary>Splits a sketch into colour groups and matches colorway names to them by colour. Runs inside the API.</summary>
public interface IColorwayPictureFinder
{
    /// <exception cref="UnreadableImageException">The content cannot be decoded.</exception>
    (int Width, int Height, IReadOnlyList<ColourGroupDto> Groups, IReadOnlyList<ColourNameMatch> Matches) Find(byte[] content, IReadOnlyList<string?> colorwayNames);
}

public sealed record ColorwayPictureItem(int ColorwayId, byte[]? RowVer, int X, int Y, int Width, int Height);

public sealed record SaveColorwayPicturesRequest(string? SourceUrl, bool Enhance, IReadOnlyList<ColorwayPictureItem>? Items);

/// <summary>Per colorway: the new picture address, or why it was not set (e.g. someone changed the colorway meanwhile).</summary>
public sealed record ColorwayPictureSavedDto(int ColorwayId, string? ImageUrl, string? Problem);

/// <summary>
/// Style page > Colorway pictures: suggests which part of the style's sketch (or picture) shows each colorway, and
/// saves the parts the editor confirmed as the colorways' pictures (cut, made sharper when small, stored once each).
/// </summary>
public sealed class ColorwayPictureService(
    IStyleRepository styles, IStyleImageStore store, IColorwayPictureFinder finder, IGarmentCropper cropper, IAiConnectionResolver connections)
{
    private const string UrlPrefix = "/api/v1/styles/images/";

    /// <param name="source">Sketch or Image; null = the sketch when there is one, else the picture.</param>
    /// <returns>Null when the style does not exist.</returns>
    /// <exception cref="StyleImageImportException">The style has no stored picture to split.</exception>
    public async Task<ColorwayPicturesDto?> SuggestAsync(int styleId, string? source, CancellationToken ct = default)
    {
        if (await styles.GetAsync(styleId, ct) is not { } style) return null;
        var (kind, url) = source switch
        {
            "Image" => ("Image", style.Style.ImageUrl),
            "Sketch" => ("Sketch", style.Style.SketchUrl),
            _ => style.Style.SketchUrl is not null ? ("Sketch", style.Style.SketchUrl) : ("Image", style.Style.ImageUrl),
        };
        var content = await ReadAsync(url, ct) ?? throw new StyleImageImportException("This style has no stored sketch or picture to take colorway pictures from.");

        var colorways = style.Colorways;
        var (width, height, groups, matches) = finder.Find(content, colorways.Select(c => c.ColorwayName).ToList());
        var suggestions = colorways.Select((c, i) =>
        {
            var m = matches[i];
            var box = m.Group is { } g ? groups[g].Box : null;
            return new ColorwayPictureSuggestionDto(c.ColorwayId, c.ColorwayCode, c.ColorwayName, c.ImageUrl, m.NameColour, m.NameWord,
                m.Group, box, m.Distance is { } d ? Math.Round(d, 1) : null, m.Sure);
        }).ToList();
        return new ColorwayPicturesDto(kind, url!, width, height, groups, suggestions);
    }

    /// <returns>Null when the style does not exist.</returns>
    /// <exception cref="StyleImageImportException">Nothing to save, or the source is not one of the style's pictures.</exception>
    public async Task<IReadOnlyList<ColorwayPictureSavedDto>?> SaveAsync(int styleId, SaveColorwayPicturesRequest request, string changedBy, CancellationToken ct = default)
    {
        if (await styles.GetAsync(styleId, ct) is not { } style) return null;
        var items = (request.Items ?? []).Where(i => i.Width > 0 && i.Height > 0).DistinctBy(i => i.ColorwayId).ToList();
        if (items.Count == 0) throw new StyleImageImportException("Tick at least one colorway.");
        // Only the style's own sketch or picture can be cut (not any stored file).
        if (request.SourceUrl is null || (request.SourceUrl != style.Style.SketchUrl && request.SourceUrl != style.Style.ImageUrl))
            throw new StyleImageImportException("The style's sketch or picture changed. Reload and try again.");
        var content = await ReadAsync(request.SourceUrl, ct) ?? throw new StyleImageImportException("The style's sketch or picture file is missing.");
        var useModel = await connections.AiEnabledAsync(ct);
        var known = style.Colorways.ToDictionary(c => c.ColorwayId);

        var saved = new List<ColorwayPictureSavedDto>();
        foreach (var item in items)
        {
            if (!known.TryGetValue(item.ColorwayId, out var colorway))
            {
                saved.Add(new ColorwayPictureSavedDto(item.ColorwayId, null, "This colorway no longer exists."));
                continue;
            }
            var (png, _, _, _) = cropper.Render(content, new CropBoxDto(item.X, item.Y, item.Width, item.Height), request.Enhance, useModel);
            var url = UrlPrefix + await store.SaveAsync(png, ImageKind.Png, ct);
            try
            {
                await styles.SetColorwayImageAsync(styleId, item.ColorwayId, item.RowVer ?? colorway.RowVer, url, changedBy, ct);
                saved.Add(new ColorwayPictureSavedDto(item.ColorwayId, url, null));
            }
            catch (StyleRuleException ex)
            {
                saved.Add(new ColorwayPictureSavedDto(item.ColorwayId, null, ex.Message));
            }
        }
        return saved;
    }

    private async Task<byte[]?> ReadAsync(string? url, CancellationToken ct)
    {
        if (url is null || !url.StartsWith(UrlPrefix, StringComparison.Ordinal)) return null;
        if (store.Open(url[UrlPrefix.Length..]) is not { } file) return null;
        await using var stream = file.Content;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
