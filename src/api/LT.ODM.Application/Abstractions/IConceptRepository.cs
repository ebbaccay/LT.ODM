using LT.ODM.Application.Concepts;

namespace LT.ODM.Application.Abstractions;

/// <summary>
/// Concepts (concept.usp_*, db/procedures/concept.procedures.sql). Writes throw StyleRuleException for refused changes (not found,
/// changed by someone else, used in a package).
/// </summary>
public interface IConceptRepository
{
    Task<ConceptPageDto> ListAsync(ConceptListQuery query, CancellationToken ct = default);

    /// <summary>Null when the concept does not exist. model: the image model in use (pictures read by it have HasVector).</summary>
    Task<ConceptDetailDto?> GetAsync(int conceptId, string? model, CancellationToken ct = default);

    Task<IReadOnlyList<StyleConceptDto>> ForStyleAsync(int styleId, CancellationToken ct = default);
    Task<ConceptLookupsDto> LookupsAsync(CancellationToken ct = default);

    /// <summary>The category's product types, the brand's customer codes and which of <paramref name="words"/> occur on fabric BOM lines.</summary>
    Task<ConceptMatchContext> MatchContextAsync(int conceptId, IReadOnlyList<string> words, CancellationToken ct = default);

    /// <summary>Creates (conceptId null) or updates a concept's details and trend tags; returns its id.</summary>
    Task<int> SaveAsync(int? conceptId, SaveConceptRequest request, string changedBy, CancellationToken ct = default);

    /// <summary>Saves (or, both null, clears) the AI brief; returns the concept's new RowVer.</summary>
    Task<byte[]> SaveBriefAsync(int conceptId, string? summary, string? briefJson, string changedBy, CancellationToken ct = default);

    Task DeleteAsync(int conceptId, byte[] rowVer, string changedBy, CancellationToken ct = default);

    /// <summary>An LT design as a new style of the concept's customer (concept.usp_Concept_DevelopStyle); returns its id and SameBrandBase.</summary>
    Task<(int StyleId, bool? SameBrandBase)> DevelopStyleAsync(int conceptId, string seasonCode, string styleNo, int? baseStyleId, string? sketchUrl,
        string changedBy, CancellationToken ct = default);

    // ----- Pictures -----

    Task<int> AddImageAsync(int conceptId, string imageUrl, string? caption, PictureVector vector, string addedBy, CancellationToken ct = default);
    Task RemoveImageAsync(int conceptId, int conceptImageId, CancellationToken ct = default);
    Task SaveImageAsync(int conceptId, int conceptImageId, bool forClient, string? caption, CancellationToken ct = default);
    Task<IReadOnlyList<ConceptImageToReadDto>> ImagesToReadAsync(int conceptId, string model, CancellationToken ct = default);
    Task SetImageVectorAsync(int conceptImageId, string model, float[]? vector, string? error, CancellationToken ct = default);

    // ----- Inspiration styles -----

    Task<IReadOnlyList<ConceptStyleAddResultDto>> AddStylesAsync(int conceptId, IReadOnlyList<int> styleIds, string? addedFrom, string changedBy,
        CancellationToken ct = default);
    Task SaveStyleAsync(int conceptId, int styleId, string? note, CancellationToken ct = default);
    Task RemoveStyleAsync(int conceptId, int styleId, CancellationToken ct = default);

    // ----- Brand closeness -----

    /// <summary>The best <paramref name="take"/> library styles per concept picture read by <paramref name="model"/>.</summary>
    Task<IReadOnlyList<ConceptClosenessRow>> ClosenessAsync(int conceptId, string model, int take, CancellationToken ct = default);
}
