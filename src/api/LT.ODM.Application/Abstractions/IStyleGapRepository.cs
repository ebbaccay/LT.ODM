using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Abstractions;

/// <summary>
/// Fill the gaps and Similar styles (style.usp_Gaps_*, usp_Lco_*, usp_Style_Similar). Writes clear the home dashboard and
/// throw StyleRuleException for refused changes.
/// </summary>
public interface IStyleGapRepository
{
    Task<StyleGapSummaryDto> SummaryAsync(bool withSuggestions, CancellationToken ct = default);
    Task<GapPageDto> ListAsync(GapListQuery query, CancellationToken ct = default);
    Task<int> AcceptAsync(AcceptGapsRequest request, string field, string changedBy, CancellationToken ct = default);
    Task<int> DismissAsync(string field, IReadOnlyList<GapValueDto> items, string changedBy, CancellationToken ct = default);

    /// <summary>The next styles to ask the AI about (no rule suggestion, not asked before).</summary>
    Task<IReadOnlyList<GapStyleToAsk>> ToAskAsync(string field, string? customer, string? season, int take, CancellationToken ct = default);
    Task<int> SaveAiAsync(string field, IReadOnlyList<GapAiAnswer> answers, string provider, string model, string createdBy, CancellationToken ct = default);

    Task<IReadOnlyList<GapSeasonDto>> SeasonsAsync(CancellationToken ct = default);
    Task<MergeSeasonResultDto> MergeSeasonAsync(string from, string to, string changedBy, CancellationToken ct = default);

    Task<IReadOnlyList<LcoLineDto>> LcoForStyleAsync(int styleId, bool withAllowance, CancellationToken ct = default);
    Task<IReadOnlyList<LcoGroupDto>> LcoSummaryAsync(bool withAllowance, CancellationToken ct = default);
    Task<ApplyLcoResultDto> ApplyLcoAsync(ApplyLcoRequest request, string changedBy, CancellationToken ct = default);

    Task<IReadOnlyList<SimilarStyleDto>> SimilarAsync(int styleId, int top, CancellationToken ct = default);
}
