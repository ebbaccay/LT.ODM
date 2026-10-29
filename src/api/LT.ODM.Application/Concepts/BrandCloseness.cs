using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleAi;
using Microsoft.Extensions.Options;

namespace LT.ODM.Application.Concepts;

// ----- Brand closeness check (roadmap 5.2, ODM pitch guardrail) -----

/// <summary>
/// Settings section "BrandCloseness": how alike two pictures must look (cosine similarity of the look-alike vectors of image
/// search, 0-1) to be flagged. Close (default 0.90): looks like the same design. Watch (default 0.85): similar, have a look.
/// Set on the laptop library (143 pictures, 2026-10-28): different garments scored at most 0.93 and rarely over 0.90, while
/// copies and colour variants of one design scored about 1.00. Environment variables BrandCloseness__Close / __Watch.
/// </summary>
public sealed class BrandClosenessOptions
{
    public const string SectionName = "BrandCloseness";
    public decimal Close { get; set; } = 0.90m;
    public decimal Watch { get; set; } = 0.85m;
}

/// <summary>Levels: Close (looks like that design), Check (similar), Clear (below Watch).</summary>
public static class ClosenessLevels
{
    public const string Close = "Close";
    public const string Check = "Check";
    public const string Clear = "Clear";
    /// <summary>Nothing could be compared: no picture, not read yet, or the image model is off.</summary>
    public const string NotChecked = "NotChecked";

    public static string Of(decimal similarity, decimal close, decimal watch) => similarity >= close ? Close : similarity >= watch ? Check : Clear;

    public static int Rank(string level) => level switch { Close => 3, Check => 2, Clear => 1, _ => 0 };
}

/// <summary>
/// The thresholds in use and whether pictures can be read: ModelReady = the image model built into the API is installed and
/// AI is switched on (Settings > AI connections); Model = its name (vectors of other models are not compared).
/// </summary>
public sealed record ClosenessThresholdsDto(decimal Close, decimal Watch, bool ModelReady, string Model);

/// <summary>
/// A library style that looks like a concept picture. SameBrand: the concept customer's own or its brand's (null = the concept
/// has no customer, so every brand counts as another); Level is Clear for the customer's own brand (a brand's own styles are fine).
/// </summary>
public sealed record ClosenessMatchDto(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? BrandCode, string? ModelName, string? ProductTypeName, string Source,
    string ImageUrl, bool? SameBrand, decimal Similarity, string Level);

/// <summary>Status: Checked, NotRead (the image model is off or not installed) or Failed (Error says why). Level: the worst match.</summary>
public sealed record ConceptImageClosenessDto(int ConceptImageId, string Status, string? Error, string Level, IReadOnlyList<ClosenessMatchDto> Matches);

/// <summary>Level: the worst over the pictures (NotChecked when no picture could be compared).</summary>
public sealed record ConceptClosenessDto(ClosenessThresholdsDto Thresholds, string Level, IReadOnlyList<ConceptImageClosenessDto> Images);

/// <summary>The closest other-brand style to a concept picture, as read from concept.usp_Concept_Closeness.</summary>
public sealed record ConceptClosenessRow(
    int ConceptImageId, int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? BrandCode, string? ModelName, string? ProductTypeName,
    string Source, string ImageUrl, bool? SameBrand, decimal Similarity);

/// <summary>
/// A copied style in a package (another brand's style offered as a new style, roadmap 5.12) or an LT design (developed from a
/// concept, 5.21; OriginStyleId null unless its BOM came from another brand) and the closest picture of another brand's style. Level: Close / Check / Clear, or NoPicture (the copy has no picture yet: copies leave the original's pictures
/// behind) / NotIndexed (its pictures are not read by image search indexing yet).
/// </summary>
public sealed record PackageClosenessItemDto(
    int PackageItemId, int StyleId, int? OriginStyleId, int Pictures, int Indexed, int? MatchStyleId, string? MatchStyleNo, string? MatchSeasonCode,
    string? MatchCustomerCode, string? MatchImageUrl, decimal? Similarity, string Level);

public sealed record PackageClosenessDto(ClosenessThresholdsDto Thresholds, IReadOnlyList<PackageClosenessItemDto> Items);

/// <summary>
/// The brand closeness check. Concept pictures are read by the image model built into the API (the same model and vectors as
/// image search, nothing leaves the server) and compared with every library picture; package copies are compared by their stored
/// image search vectors. A brand's own styles are never flagged. Reads pictures only while AI is switched on, like image search.
/// </summary>
public sealed class BrandClosenessService(
    IImageEmbeddingModel model, IAiConnectionResolver connections, IConceptImageStore images, IConceptRepository concepts, IPackageRepository packages,
    IOptions<BrandClosenessOptions> options)
{
    public const string ImagePrefix = "/api/v1/concepts/images/";
    private const int MatchesPerPicture = 6;

    public async Task<ClosenessThresholdsDto> ThresholdsAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var watch = Math.Min(o.Watch, o.Close);
        return new ClosenessThresholdsDto(o.Close, watch, model.Info.IsAvailable && await connections.AiEnabledAsync(ct), model.Info.Name);
    }

    /// <summary>A stored concept picture's vector; Model null when the image model cannot run (nothing to store then).</summary>
    public async Task<PictureVector> ReadAsync(string fileName, CancellationToken ct = default)
    {
        if (!(await ThresholdsAsync(ct)).ModelReady) return new PictureVector(null, null, null);
        if (images.Open(fileName) is not { } file) return new PictureVector(model.Info.Name, null, "The picture file was not found.");
        byte[] bytes;
        await using (var stream = file.Content)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        try
        {
            return new PictureVector(model.Info.Name, await Task.Run(() => model.Embed(bytes), ct), null);
        }
        catch (UnreadableImageException)
        {
            return new PictureVector(model.Info.Name, null, "The picture could not be read.");
        }
        catch (AiServiceException ex)
        {
            return new PictureVector(null, null, ex.Message);
        }
    }

    /// <summary>
    /// Reads the concept's pictures not read by the current model yet, then compares every picture with the library. Null when
    /// the concept does not exist.
    /// </summary>
    public async Task<ConceptClosenessDto?> CheckConceptAsync(int conceptId, CancellationToken ct = default)
    {
        var t = await ThresholdsAsync(ct);
        if (t.ModelReady)
            foreach (var p in await concepts.ImagesToReadAsync(conceptId, t.Model, ct))
            {
                if (!p.ImageUrl.StartsWith(ImagePrefix, StringComparison.Ordinal)) continue;
                var v = await ReadAsync(p.ImageUrl[ImagePrefix.Length..], ct);
                if (v.Model is not null) await concepts.SetImageVectorAsync(p.ConceptImageId, v.Model, v.Vector, v.Error, ct);
            }

        if (await concepts.GetAsync(conceptId, t.Model, ct) is not { } concept) return null;
        var rows = (await concepts.ClosenessAsync(conceptId, t.Model, MatchesPerPicture, ct)).ToLookup(r => r.ConceptImageId);
        var result = concept.Images.Select(i =>
        {
            if (!i.HasVector)
                return new ConceptImageClosenessDto(i.ConceptImageId, i.VectorError is null ? "NotRead" : "Failed", i.VectorError, ClosenessLevels.NotChecked, []);
            var matches = rows[i.ConceptImageId].Select(r => new ClosenessMatchDto(r.StyleId, r.StyleNo, r.SeasonCode, r.CustomerCode, r.BrandCode, r.ModelName,
                r.ProductTypeName, r.Source, r.ImageUrl, r.SameBrand, r.Similarity,
                r.SameBrand == true ? ClosenessLevels.Clear : ClosenessLevels.Of(r.Similarity, t.Close, t.Watch))).ToList();
            var level = matches.Select(m => m.Level).DefaultIfEmpty(ClosenessLevels.Clear).MaxBy(ClosenessLevels.Rank)!;
            return new ConceptImageClosenessDto(i.ConceptImageId, "Checked", null, level, matches);
        }).ToList();
        return new ConceptClosenessDto(t, Worst(result.Select(r => r.Level)), result);
    }

    /// <summary>The package's copied styles against other brands' pictures (stored vectors only, no model run).</summary>
    public async Task<PackageClosenessDto> CheckPackageAsync(int packageId, CancellationToken ct = default)
    {
        var t = await ThresholdsAsync(ct);
        var items = (await packages.ClosenessAsync(packageId, t.Model, ct))
            .Select(r => r with
            {
                Level = r.Pictures == 0 ? "NoPicture" : r.Indexed == 0 ? "NotIndexed"
                    : r.Similarity is { } s ? ClosenessLevels.Of(s, t.Close, t.Watch) : ClosenessLevels.Clear,
            })
            .ToList();
        return new PackageClosenessDto(t, items);
    }

    private static string Worst(IEnumerable<string> levels)
        => levels.Where(l => l != ClosenessLevels.NotChecked).DefaultIfEmpty(ClosenessLevels.NotChecked).MaxBy(ClosenessLevels.Rank)!;
}
