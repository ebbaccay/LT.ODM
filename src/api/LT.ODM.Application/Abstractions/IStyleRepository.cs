using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Abstractions;

/// <summary>Styles, colorways, BOM lines and style history (style.usp_* procedures). Writes throw StyleRuleException for refused changes.</summary>
public interface IStyleRepository
{
    Task<StyleLookupsDto> GetLookupsAsync(CancellationToken ct = default);
    Task<PagedResult<StyleListItemDto>> ListAsync(StyleListQuery query, CancellationToken ct = default);
    Task<StyleDetailDto?> GetAsync(int styleId, CancellationToken ct = default);
    Task<StyleDashboardDto> GetDashboardAsync(CancellationToken ct = default);

    /// <summary>Creates (styleId null) or updates a style; returns its id.</summary>
    Task<int> SaveStyleAsync(int? styleId, SaveStyleRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteStyleAsync(int styleId, byte[] rowVer, string changedBy, CancellationToken ct = default);

    /// <summary>Copies a style with its colorways and BOM to another season / number and links it as reused; returns the new id.</summary>
    Task<int> CopyStyleAsync(int styleId, CopyStyleRequest request, string changedBy, CancellationToken ct = default);

    Task<int> SaveColorwayAsync(int styleId, int? colorwayId, SaveColorwayRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteColorwayAsync(int colorwayId, byte[] rowVer, string changedBy, CancellationToken ct = default);

    Task<int> SaveBomLineAsync(int styleId, int? bomLineId, SaveBomLineRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteBomLineAsync(int bomLineId, byte[] rowVer, string changedBy, CancellationToken ct = default);

    Task SetHistoryAsync(int styleId, SetStyleHistoryRequest request, string changedBy, CancellationToken ct = default);
    Task RemoveHistoryAsync(int styleId, string changedBy, CancellationToken ct = default);
}
