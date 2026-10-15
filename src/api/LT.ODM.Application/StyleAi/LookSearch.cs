using LT.ODM.Application.Ai;

namespace LT.ODM.Application.StyleAi;

/// <summary>How words compare with every current picture: count, average and spread (population standard deviation) of similarity.</summary>
public sealed record ImageVectorStatsDto(int Pictures, double Average, double StdDev);

/// <summary>
/// Smart search's second step, between the phrase list and the AI server: the request searched against the style pictures
/// with the image model built into the API (Marqo-FashionSigLIP, milliseconds, nothing sent). The model always returns a
/// nearest picture, even for "styles from vendor ABC", so its matches are used only when the best one clearly stands out
/// from all the others: at least <see cref="MinStandOut"/> standard deviations above the average picture, and a similarity
/// of at least <see cref="MinSimilarity"/>. Measured on 139 pictures (October 2026): requests about looks stood out
/// 2.9-3.9 with similarity 0.09-0.13; others (materials, vendors, changes, prices, things no picture shows) 2.6 or less, or
/// below 0.03. The gap is small: re-check both thresholds as more pictures are indexed.
/// </summary>
public sealed class LookSearch(IImageEmbeddingModel model, IImageSearchRepository repo, IAiConnectionResolver connections)
{
    /// <summary>The best picture must be this many standard deviations above the average picture.</summary>
    public const double MinStandOut = 2.8;
    /// <summary>The best picture's similarity must be at least this (word-to-picture scores of right matches were 0.09 or more).</summary>
    public const decimal MinSimilarity = 0.05m;
    /// <summary>Matches shown: those at least this far above the average (the best one always).</summary>
    public const double MinShown = 2.0;
    /// <summary>Below this many pictures the spread says little: the step is skipped.</summary>
    public const int MinPictures = 20;

    /// <summary>The pictures that stand out for these words, best first; null when the step cannot run or nothing stands out.</summary>
    public async Task<IReadOnlyList<ImageMatchDto>?> FindAsync(string words, string? customer, CancellationToken ct = default)
    {
        if (!model.TextInfo.IsAvailable || !await connections.AiEnabledAsync(ct)) return null;
        var vector = await Task.Run(() => model.EmbedText(words), ct);
        var stats = await repo.VectorStatsAsync(model.Info.Name, vector, ct);
        if (stats.Pictures < MinPictures || stats.StdDev <= 0) return null;

        var matches = await repo.SearchVectorsAsync(model.Info.Name, vector, null, customer, ImageSearchRules.MaxResults, ct);
        double StandOut(ImageMatchDto m) => ((double)m.Similarity - stats.Average) / stats.StdDev;
        if (matches.Count == 0 || matches[0].Similarity < MinSimilarity || StandOut(matches[0]) < MinStandOut) return null;
        return matches.Where((m, i) => i == 0 || StandOut(m) >= MinShown).ToList();
    }
}
