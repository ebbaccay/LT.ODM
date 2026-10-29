using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Concepts;

// ----- Develop as style (roadmap 5.21): an LT original design becomes a style of the concept's customer -----

/// <summary>
/// SeasonCode and StyleNo: the new style's, typed by the user. BaseStyleId: one of the concept's inspiration styles to start the
/// BOM from (null = no BOM yet). ConceptImageId: the concept picture to use as its sketch (null = none).
/// </summary>
public sealed record DevelopStyleRequest(string? SeasonCode, string? StyleNo, int? BaseStyleId, int? ConceptImageId);

/// <summary>SameBrandBase: the BOM came whole from the customer's own brand (true), as know-how from another brand (false), or none (null).</summary>
public sealed record DevelopedStyleResultDto(int StyleId, bool? SameBrandBase, string? SketchUrl);

/// <summary>A style developed from the concept (an LT design). OriginStyleId: another brand's style its BOM know-how came from.</summary>
public sealed record DevelopedStyleDto(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? ModelName, string? ImageUrl, string? SketchUrl, int? OriginStyleId,
    string? OriginStyleNo, string CreatedBy, DateTime CreatedUtc, int Packages);

/// <summary>
/// Makes the new style: copies the chosen concept picture into the style image store (so it is the style's sketch everywhere:
/// style page, line sheets, share links, image search), creates the style (concept.usp_Concept_DevelopStyle), and reads the
/// sketch's look-alike vector at once when the image model can run, so image search and the brand closeness check see the new
/// design without waiting for indexing.
/// </summary>
public sealed class ConceptStyleDeveloper(
    IConceptRepository concepts, IConceptImageStore conceptImages, IStyleImageStore styleImages, IImageEmbeddingModel model,
    IAiConnectionResolver connections, IImageSearchRepository imageSearch)
{
    public const string StyleImagePrefix = "/api/v1/styles/images/";

    /// <exception cref="StyleRuleException">The concept or picture is gone (404), or a rule refused it (400 / 409).</exception>
    public async Task<DevelopedStyleResultDto> DevelopAsync(int conceptId, DevelopStyleRequest r, string changedBy, CancellationToken ct = default)
    {
        var detail = await concepts.GetAsync(conceptId, null, ct) ?? throw new StyleRuleException(404, "This concept no longer exists.");
        byte[]? picture = null;
        string? sketchUrl = null;
        if (r.ConceptImageId is { } imageId)
        {
            var image = detail.Images.FirstOrDefault(i => i.ConceptImageId == imageId)
                        ?? throw new StyleRuleException(404, "This picture is no longer on the concept.");
            picture = await ReadAsync(image.ImageUrl, ct) ?? throw new StyleRuleException(404, "The picture file was not found.");
            var kind = ConceptStudioRules.DetectImage(picture) ?? throw new StyleRuleException(400, "The picture could not be read.");
            sketchUrl = StyleImagePrefix + await styleImages.SaveAsync(picture, kind, ct);
        }

        var (styleId, sameBrand) = await concepts.DevelopStyleAsync(conceptId, r.SeasonCode!, r.StyleNo!, r.BaseStyleId, sketchUrl, changedBy, ct);

        if (sketchUrl is not null && model.Info.IsAvailable && await connections.AiEnabledAsync(ct)
            && styleImages.Open(sketchUrl[StyleImagePrefix.Length..]) is { } stored)
        {
            byte[] bytes;
            await using (var stream = stored.Content)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                bytes = buffer.ToArray();
            }
            float[]? vector = null;
            string? error = null;
            try { vector = await Task.Run(() => model.Embed(bytes), ct); }
            catch (UnreadableImageException) { error = "The picture could not be read."; }
            catch (AiServiceException ex) { error = ex.Message; }
            await imageSearch.SaveVectorsAsync([new ImageVectorReading(styleId, "Sketch", 0, sketchUrl, vector, error)], model.Info.Name, changedBy, ct);
        }
        return new DevelopedStyleResultDto(styleId, sameBrand, sketchUrl);
    }

    private async Task<byte[]?> ReadAsync(string imageUrl, CancellationToken ct)
    {
        if (!imageUrl.StartsWith(BrandClosenessService.ImagePrefix, StringComparison.Ordinal)
            || conceptImages.Open(imageUrl[BrandClosenessService.ImagePrefix.Length..]) is not { } file) return null;
        await using var stream = file.Content;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
