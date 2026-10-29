namespace LT.ODM.Application.Concepts;

// ----- Concepts (/api/v1/concepts, db/procedures/concept.procedures.sql; roadmap 5.6, 5.7, 5.1) -----

/// <summary>Concept statuses (concept.Concept.Status) and where an inspiration style was found (concept.ConceptStyle.AddedFrom).</summary>
public static class ConceptValues
{
    public static readonly string[] Statuses = ["Draft", "Ready", "Dropped"];
    public static readonly string[] Genders = ["MALE", "FEMALE", "UNISEX", "KIDS"];
    /// <summary>Pick: typed or picked; Match: the AI matches; LookAlike: from the closeness check (pictures).</summary>
    public static readonly string[] StyleSources = ["Pick", "Match", "LookAlike"];

    public const int MaxImages = 24;
    public const int MaxTags = 20;

    /// <summary>The value as the database spells it (any case in), or null when it is not on the list.</summary>
    public static string? Parse(string[] list, string? value) => list.FirstOrDefault(v => string.Equals(v, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record ConceptListQuery(
    string? Search, string? Customer, string? Season, string? Category, string? Status, bool Mine, string UserName, int Skip, int Take);

/// <summary>
/// CoverUrl: the first inspiration picture, else the first inspiration style's picture. Packages: packages with a style offered
/// for the concept. Tags: trend tag names. LastChangedUtc: the latest change to the concept, its pictures or styles.
/// </summary>
public sealed record ConceptListItemDto(
    int ConceptId, string Name, string? CustomerCode, string? CustomerName, string? SeasonCode, string? ProductCategoryCode, string? ProductCategoryName,
    string? TargetMarketName, string? Gender, string Status, string OwnerName, int Images, int Styles, int Packages, string? Tags, string? CoverUrl,
    DateTime LastChangedUtc);

public sealed record ConceptPageDto(IReadOnlyList<ConceptListItemDto> Items, int Total);

public sealed record CodeNameDto(string Code, string Name);

/// <summary>The concept pick lists (active rows), maintained in Settings > Reference lists.</summary>
public sealed record ConceptLookupsDto(IReadOnlyList<CodeNameDto> TargetMarkets, IReadOnlyList<CodeNameDto> TrendTags, IReadOnlyList<CodeNameDto> ProductCategories);

/// <summary>The AI brief: summary plus suggested products, fabric direction and notes (Concept Studio's brief).</summary>
public sealed record ConceptBriefDto(string? Summary, IReadOnlyList<string> Products, IReadOnlyList<string> Fabrics, IReadOnlyList<string> Notes,
    string? By, DateTime? Utc);

/// <summary>RowVer: send back unchanged when saving or deleting. Brief null = none drafted yet.</summary>
public sealed record ConceptHeaderDto(
    int ConceptId, string Name, string? CustomerCode, string? CustomerName, string? BrandCode, string? SeasonCode, string? TargetMarketCode,
    string? TargetMarketName, string? ProductCategoryCode, string? ProductCategoryName, string? Gender, string? TargetPrice, string? Description,
    string Status, string OwnerName, ConceptBriefDto? Brief, string CreatedBy, DateTime CreatedUtc, string? UpdatedBy, DateTime? UpdatedUtc,
    byte[] RowVer, string? ClientStory = null);

/// <summary>
/// An inspiration picture. HasVector: read by the image model now in use (the closeness check can compare it); VectorError:
/// why it could not be read.
/// </summary>
public sealed record ConceptImageDto(int ConceptImageId, string ImageUrl, string? Caption, int SortOrder, bool HasVector, string? VectorError,
    string AddedBy, DateTime AddedUtc, bool ForClient = false);

/// <summary>ForClient: show the picture on the concept's story page for the client (only when the closeness check found it clear).</summary>
public sealed record SaveConceptImageRequest(bool ForClient, string? Caption);

/// <summary>
/// A library style the concept takes from (design provenance, roadmap 5.1). SameBrand: the concept customer's own or its brand's
/// (null when the concept has no customer); another brand's style may inspire know-how only, never the look.
/// </summary>
public sealed record ConceptStyleDto(
    int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? BrandCode, string? ModelName, string? Description,
    string? ProductTypeName, string? Gender, string? ImageUrl, string? SketchUrl, bool IsActive, bool? SameBrand, string? Note, string? AddedFrom,
    string AddedBy, DateTime AddedUtc);

/// <summary>A style offered in a package for the concept.</summary>
public sealed record ConceptPackageItemDto(
    int PackageItemId, int PackageId, string PackageName, string PackageStatus, string CustomerCode, int StyleId, string StyleNo, string SeasonCode,
    string? Outcome);

/// <summary>DevelopedStyles: LT designs made from the concept (Develop as style, roadmap 5.21).</summary>
public sealed record ConceptDetailDto(
    ConceptHeaderDto Concept, IReadOnlyList<CodeNameDto> Tags, IReadOnlyList<ConceptImageDto> Images, IReadOnlyList<ConceptStyleDto> Styles,
    IReadOnlyList<ConceptPackageItemDto> PackageItems, IReadOnlyList<DevelopedStyleDto>? DevelopedStyles = null);

/// <summary>A concept of a style (style page). Kind: Developed (the style is an LT design developed from it) or Inspired (it inspired the concept).</summary>
public sealed record StyleConceptDto(
    int ConceptId, string Name, string? CustomerCode, string? SeasonCode, string Status, string OwnerName, string? Note, string Kind = "Inspired");

/// <summary>
/// New concept when RowVer is null (then OwnerName null = the signed-in user). Status null = Draft. TrendTags: codes from the list
/// (null leaves the tags as they are on an update).
/// </summary>
public sealed record SaveConceptRequest(
    byte[]? RowVer, string? Name, string? CustomerCode, string? SeasonCode, string? TargetMarketCode, string? ProductCategoryCode, string? Gender,
    string? TargetPrice, string? Description, string? Status, string? OwnerName, IReadOnlyList<string>? TrendTags, string? ClientStory = null);

public sealed record AddConceptStylesRequest(IReadOnlyList<int>? StyleIds, string? AddedFrom);

/// <summary>Result: Added, AlreadyIn or NotFound.</summary>
public sealed record ConceptStyleAddResultDto(int StyleId, string? StyleNo, string? SeasonCode, string? CustomerCode, string Result);

public sealed record SaveConceptStyleRequest(string? Note);

/// <summary>A picture's look-alike vector as read by the image model (Model null = the model is not available; nothing to store).</summary>
public sealed record PictureVector(string? Model, float[]? Vector, string? Error);

/// <summary>A concept picture still to read with the image model.</summary>
public sealed record ConceptImageToReadDto(int ConceptImageId, string ImageUrl);
