using System.Text.RegularExpressions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;

namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// Settings > Import pictures: style pictures from a folder of files named after the style (RC2606OW001_Img01.jpg).
/// Each file is analysed (styles found from its name, garment box suggested), checked by the admin, then saved: cut to
/// the box, made sharper when small, stored once and set on every style ticked.
/// </summary>
public static partial class StyleImageImportRules
{
    /// <summary>Files per analyse/save call are one at a time; this caps the size of each.</summary>
    public const int MaxFileBytes = ConceptStudioRules.MaxImageBytes;

    /// <summary>Match kinds ticked by default: the style itself and its suffixed versions (_H, -H, _L, _LH).</summary>
    public static readonly string[] TickedMatches = ["Exact", "Suffix"];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-.]{0,59}$")]
    private static partial Regex KeyRegex();

    /// <summary>
    /// The style number in a file name: the name without extension, up to its first "_" (the suffix such as _Img01 is
    /// ignored, as the Style Library does for style numbers). Null when that is not a plausible style number.
    /// </summary>
    public static string? FileKey(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? "")).Trim();
        var cut = name.IndexOf('_');
        var key = (cut >= 0 ? name[..cut] : name).Trim().ToUpperInvariant();
        return KeyRegex().IsMatch(key) ? key : null;
    }
}

/// <summary>Garment box in pixels of the uploaded (upright) picture.</summary>
public sealed record CropBoxDto(int X, int Y, int Width, int Height);

/// <summary>
/// Outcome: Cropped (a garment was found among other things; Box is it), Whole (the picture already is the garment),
/// NotFound (nothing garment-like: the admin draws the box or keeps the whole picture).
/// Method: Model (the image model chose between the candidates) or Rules (shape only: model missing or AI switched off).
/// Sure: the model is fairly sure (or the picture is kept whole); false = the screen asks the admin to check the box.
/// </summary>
public sealed record CropSuggestionDto(int Width, int Height, CropBoxDto? Box, string Outcome, string Method, bool Sure);

/// <summary>
/// Whether the picture is a sketch (Sketch: goes to the style's sketch) or a photo of the made garment (Photo: the
/// style's picture). Kind null = cannot tell. Sure false = close call, the screen asks the admin to check.
/// </summary>
public sealed record PictureKindDto(string? Kind, bool Sure, string Method);

public sealed record StyleImageMatchDto(
    int StyleId, string StyleNo, string SeasonCode, string? CustomerCode, string? ModelName, string? ImageUrl, string? SketchUrl, bool IsActive,
    string Match, bool Ticked);

/// <summary>What one file is: its key, the styles it belongs to and the suggested garment box.</summary>
public sealed record StyleImageAnalysisDto(
    string FileName, string? FileKey, CropSuggestionDto Crop, PictureKindDto Kind, IReadOnlyList<StyleImageMatchDto> Matches);

/// <summary>
/// "Check existing pictures": a style whose stored picture looks like a sketch, or sketch like a photo.
/// Action: MoveToSketch / MoveToPicture (the other place is empty), Swap (both are in the wrong place), or Conflict
/// (in the wrong place but the other place already holds the right kind: left alone, shown for a manual look).
/// Sure: every detection involved was a clear call. ImageUrl/SketchUrl are what the check saw; moving needs them unchanged.
/// </summary>
public sealed record ExistingPictureDto(
    int StyleId, string StyleNo, string SeasonCode, string? CustomerCode, string? ModelName, string? ImageUrl, string? SketchUrl,
    PictureKindDto? ImageKind, PictureKindDto? SketchKind, string Action, bool Sure);

public sealed record ExistingPicturesCheckDto(int StylesChecked, int FilesChecked, int FilesUnreadable, IReadOnlyList<ExistingPictureDto> Suggestions);

/// <summary>One style to fix, with the addresses the check saw (PascalCase: sent to SQL as JSON).</summary>
public sealed record ExistingPictureMove(int StyleId, string? ImageUrl, string? SketchUrl);

public sealed record ExistingPicturesMoveResultDto(IReadOnlyList<int> Moved, IReadOnlyList<int> Skipped);

public sealed record StyleImageSaveResultDto(string ImageUrl, int Width, int Height, bool Enhanced, IReadOnlyList<int> Updated, IReadOnlyList<int> Skipped);

/// <summary>Whether the helpers are there: the image model (choosing the garment) and the upscaler (sharper small pictures).</summary>
public sealed record StyleImageImportStatusDto(bool AiEnabled, bool CropModel, string? CropModelProblem, bool Upscaler, string? UpscalerProblem);

public interface IStyleImageImportRepository
{
    /// <summary>Styles per file key (style.usp_StyleImageImport_Match), Exact and Suffix first.</summary>
    Task<IReadOnlyList<(string FileKey, StyleImageMatchDto Match)>> MatchAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);

    /// <summary>Sets the picture (field Image) or sketch (Sketch); returns (styleId, updated) for each id given.</summary>
    Task<IReadOnlyList<(int StyleId, bool Updated)>> SetPictureAsync(IReadOnlyCollection<int> styleIds, string field, string imageUrl, bool overwrite, string changedBy, CancellationToken ct = default);

    /// <summary>Styles with a stored picture or sketch (style.usp_StyleImageImport_Existing); kinds and action not filled.</summary>
    Task<IReadOnlyList<ExistingPictureDto>> ExistingAsync(CancellationToken ct = default);

    /// <summary>Swaps picture and sketch where both still hold the addresses given; returns (styleId, moved).</summary>
    Task<IReadOnlyList<(int StyleId, bool Moved)>> MoveExistingAsync(IReadOnlyCollection<ExistingPictureMove> moves, string changedBy, CancellationToken ct = default);
}

/// <summary>Cuts the garment out of a picture and makes small results sharper. Runs inside the API; nothing is sent anywhere.</summary>
public interface IGarmentCropper
{
    UpscalerInfo Upscaler { get; }

    /// <summary>
    /// Suggests the garment box. <paramref name="useModel"/> lets the image model choose between candidates (when installed).
    /// </summary>
    /// <exception cref="UnreadableImageException">The content cannot be decoded.</exception>
    CropSuggestionDto Suggest(byte[] content, bool useModel);

    /// <summary>Sketch or photo, judged on the part inside <paramref name="box"/> (null = whole picture).</summary>
    /// <exception cref="UnreadableImageException">The content cannot be decoded.</exception>
    PictureKindDto Classify(byte[] content, CropBoxDto? box, bool useModel);

    /// <summary>
    /// The picture cut to <paramref name="box"/> (null = whole), upright, on white, as PNG; with <paramref name="enhance"/>
    /// small results are enlarged (AI upscaler when installed, else a smooth resize). Returns the PNG and its size.
    /// </summary>
    /// <exception cref="UnreadableImageException">The content cannot be decoded.</exception>
    (byte[] Png, int Width, int Height, bool Enhanced) Render(byte[] content, CropBoxDto? box, bool enhance, bool useModel);
}

public sealed record UpscalerInfo(bool IsAvailable, string? Problem);

public sealed class StyleImageImportException(string message) : Exception(message);

public sealed class StyleImageImportService(
    IStyleImageImportRepository repo, IGarmentCropper cropper, IImageEmbeddingModel model, IStyleImageStore store, IAiConnectionResolver connections)
{
    public async Task<StyleImageImportStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var ai = await connections.AiEnabledAsync(ct);
        var cropModel = model.TextInfo;
        var up = cropper.Upscaler;
        return new StyleImageImportStatusDto(ai, cropModel.IsAvailable, cropModel.Problem, up.IsAvailable, up.Problem);
    }

    public async Task<StyleImageAnalysisDto> AnalyseAsync(string fileName, byte[] content, CancellationToken ct = default)
    {
        var key = StyleImageImportRules.FileKey(fileName);
        var useModel = await connections.AiEnabledAsync(ct);
        var crop = cropper.Suggest(content, useModel);
        var kind = cropper.Classify(content, crop.Box, useModel);
        IReadOnlyList<StyleImageMatchDto> matches = [];
        if (key is not null)
            matches = (await repo.MatchAsync([key], ct)).Where(m => m.FileKey == key).Select(m => m.Match).ToList();
        return new StyleImageAnalysisDto(Path.GetFileName(fileName), key, crop, kind, matches);
    }

    /// <summary>Renders the result as the admin would save it (PNG), so it can be checked first.</summary>
    public async Task<(byte[] Png, bool Enhanced)> PreviewAsync(byte[] content, CropBoxDto? box, bool enhance, CancellationToken ct = default)
    {
        var (png, _, _, enhanced) = cropper.Render(content, box, enhance, await connections.AiEnabledAsync(ct));
        return (png, enhanced);
    }

    public async Task<StyleImageSaveResultDto> SaveAsync(
        byte[] content, CropBoxDto? box, bool enhance, string field, bool overwrite, IReadOnlyCollection<int> styleIds, string changedBy, CancellationToken ct = default)
    {
        if (field is not ("Image" or "Sketch")) throw new StyleImageImportException("Choose whether the file is the style's picture or its sketch.");
        if (styleIds.Count == 0) throw new StyleImageImportException("Tick at least one style.");
        var (png, width, height, enhanced) = cropper.Render(content, box, enhance, await connections.AiEnabledAsync(ct));
        var name = await store.SaveAsync(png, ImageKind.Png, ct);
        var url = StyleImageUrl(name);
        var result = await repo.SetPictureAsync(styleIds, field, url, overwrite, changedBy, ct);
        return new StyleImageSaveResultDto(url, width, height, enhanced,
            result.Where(r => r.Updated).Select(r => r.StyleId).ToList(), result.Where(r => !r.Updated).Select(r => r.StyleId).ToList());
    }

    /// <summary>
    /// Checks every stored style picture and sketch: sketch or photo (each file once, however many styles share it), and
    /// suggests moving the ones in the wrong place. Nothing is changed.
    /// </summary>
    public async Task<ExistingPicturesCheckDto> CheckExistingAsync(CancellationToken ct = default)
    {
        var styles = await repo.ExistingAsync(ct);
        var useModel = await connections.AiEnabledAsync(ct);
        var kinds = new Dictionary<string, PictureKindDto?>(StringComparer.Ordinal);
        var unreadable = 0;
        PictureKindDto? KindOf(string? url)
        {
            if (url is null || !url.StartsWith(UrlPrefix, StringComparison.Ordinal)) return null;
            if (kinds.TryGetValue(url, out var known)) return known;
            PictureKindDto? kind = null;
            if (store.Open(url[UrlPrefix.Length..]) is { } file)
            {
                using (file.Content)
                using (var buffer = new MemoryStream())
                {
                    file.Content.CopyTo(buffer);
                    try { kind = cropper.Classify(buffer.ToArray(), null, useModel); }
                    catch (UnreadableImageException) { }
                }
            }
            if (kind is null) unreadable++;
            return kinds[url] = kind;
        }

        var suggestions = new List<ExistingPictureDto>();
        foreach (var s in styles)
        {
            ct.ThrowIfCancellationRequested();
            var image = KindOf(s.ImageUrl);
            var sketch = KindOf(s.SketchUrl);
            var imageWrong = image?.Kind == "Sketch";
            var sketchWrong = sketch?.Kind == "Photo";
            var action = (imageWrong, sketchWrong) switch
            {
                (true, true) => "Swap",
                (true, false) when s.SketchUrl is null => "MoveToSketch",
                (false, true) when s.ImageUrl is null => "MoveToPicture",
                (true, _) or (_, true) => "Conflict",
                _ => null,
            };
            if (action is null) continue;
            var sure = (!imageWrong || image!.Sure) && (!sketchWrong || sketch!.Sure);
            suggestions.Add(s with { ImageKind = image, SketchKind = sketch, Action = action, Sure = sure && action != "Conflict" });
        }
        return new ExistingPicturesCheckDto(styles.Count, kinds.Count, unreadable, suggestions);
    }

    public async Task<ExistingPicturesMoveResultDto> MoveExistingAsync(IReadOnlyCollection<ExistingPictureMove> moves, string changedBy, CancellationToken ct = default)
    {
        if (moves.Count == 0) throw new StyleImageImportException("Tick at least one style.");
        var result = await repo.MoveExistingAsync(moves, changedBy, ct);
        return new ExistingPicturesMoveResultDto(
            result.Where(r => r.Moved).Select(r => r.StyleId).ToList(), result.Where(r => !r.Moved).Select(r => r.StyleId).ToList());
    }

    private const string UrlPrefix = "/api/v1/styles/images/";

    /// <summary>Address stored in style.Style (served by StylesController).</summary>
    public static string StyleImageUrl(string fileName) => UrlPrefix + fileName;
}
