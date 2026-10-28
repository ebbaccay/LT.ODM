namespace LT.ODM.Application.StyleLibrary;

// ----- Style Packages (/api/v1/style-packages, db/procedures/pack.procedures.sql) -----

/// <summary>Package statuses (pack.Package.Status) and where a style was added from (pack.PackageItem.AddedFrom).</summary>
public static class PackageValues
{
    public static readonly string[] Statuses = ["Draft", "Sent", "Viewed", "Sampling", "Ordered", "Closed"];
    /// <summary>What became of a style offered (hit rate, roadmap 5.17); null = not known yet.</summary>
    public static readonly string[] Outcomes = ["Sampling", "Ordered", "Dropped"];
    public static readonly string[] AddedFrom = ["Styles", "StylePage", "Similar", "ImageSearch", "Assistant", "Concept", "Copy", "Offer"];

    /// <summary>The value as the database spells it (any case in), or null when it is not on the list.</summary>
    public static string? Parse(string[] list, string? value) => list.FirstOrDefault(v => string.Equals(v, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Mine = only the signed-in user's packages (as owner).</summary>
public sealed record PackageListQuery(string? Search, string? Customer, string? Season, string? Status, bool Mine, string UserName, int Skip, int Take);

/// <summary>CoverUrl: the first item's picture or sketch. LastChangedUtc: the latest change to the package or its items.</summary>
public sealed record PackageListItemDto(
    int PackageId, string Name, string CustomerCode, string CustomerName, string? SeasonCode, string Status, string OwnerName,
    DateTime? SentUtc, int Items, string? CoverUrl, DateTime LastChangedUtc);

public sealed record PackagePageDto(IReadOnlyList<PackageListItemDto> Items, int Total);

/// <summary>RowVer: send back unchanged when saving or deleting the package.</summary>
public sealed record PackageHeaderDto(
    int PackageId, string Name, string CustomerCode, string CustomerName, string? BrandCode, string? SeasonCode, string? Description,
    string Status, string OwnerName, DateTime? SentUtc, string CreatedBy, DateTime CreatedUtc, string? UpdatedBy, DateTime? UpdatedUtc,
    byte[] RowVer);

/// <summary>
/// A colourway of the item's style. Offered: in the offer. Missing: a chosen code that is no longer on the style (no name, status or
/// picture).
/// </summary>
public sealed record PackageColorwayDto(string ColorwayCode, string? ColorwayName, string? Status, string? ImageUrl, bool Offered, bool Missing);

/// <summary>
/// A key material of the style (cards, line sheet). Kind: Fabric (up to 3, largest consumption first) or Trim (up to 4, BOM order).
/// Composition: what the description reader read (null until read). Recycled: the description says so.
/// </summary>
public sealed record PackageMaterialDto(string Kind, string MaterialCode, string? Description, string? Composition, bool Recycled);

/// <summary>
/// A style in the package. PackageItemId stays the same while the style is in the package (costs attach to it later).
/// OtherBrand: the style is not of the package's brand (the customer was changed after it was added); to be offered as a copy.
/// FabricLines / RecycledFabricLines: the style's fabric BOM lines and how many are written as recycled.
/// </summary>
public sealed record PackageItemDto(
    int PackageItemId, int StyleId, int SortOrder, string? PitchNote, string? InternalNote, bool AllColorways, string? AddedFrom,
    string StyleNo, string SeasonCode, string CustomerCode, string? Description, string? ModelName, string? ProductTypeCode,
    string? ProductTypeName, string? Gender, string? WeaveTypeCode, string? WeaveTypeName, short? GarmentLeadTimeDays, string? ImageUrl,
    string? SketchUrl, bool IsActive, bool OtherBrand, int FabricLines, int RecycledFabricLines, string CreatedBy, DateTime CreatedUtc,
    string? UpdatedBy, DateTime? UpdatedUtc, byte[] RowVer, IReadOnlyList<PackageColorwayDto> Colorways, IReadOnlyList<PackageMaterialDto> Materials,
    IReadOnlyList<PackageFeedbackDto>? Feedback = null, string? Outcome = null, string? OutcomeBy = null, DateTime? OutcomeUtc = null);

public sealed record PackageDetailDto(PackageHeaderDto Package, IReadOnlyList<PackageItemDto> Items);

/// <summary>A package a style is in (style page).</summary>
public sealed record StylePackageDto(int PackageId, string Name, string CustomerCode, string? SeasonCode, string Status, string OwnerName, int PackageItemId);

/// <summary>New package when RowVer is null (then OwnerName null = the signed-in user). Status null = Draft.</summary>
public sealed record SavePackageRequest(byte[]? RowVer, string? Name, string? CustomerCode, string? SeasonCode, string? Description, string? Status,
    string? OwnerName);

public sealed record AddPackageItemsRequest(IReadOnlyList<int>? StyleIds, string? AddedFrom);

/// <summary>Result: Added, AlreadyIn, OtherBrand (another brand's style: offer it as a copy) or NotFound.</summary>
public sealed record PackageAddResultDto(int StyleId, string? StyleNo, string? SeasonCode, string? CustomerCode, string Result, int? PackageItemId);

/// <summary>AllColorways false offers only ColorwayCodes.</summary>
public sealed record SavePackageItemRequest(byte[]? RowVer, string? PitchNote, string? InternalNote, bool AllColorways, IReadOnlyList<string>? ColorwayCodes);

public sealed record PackageItemIdsRequest(IReadOnlyList<int>? PackageItemIds);

/// <summary>Another customer's style offered as a new style of the package's customer: its season and its own style number.</summary>
public sealed record CopyToPackageRequest(int StyleId, string? SeasonCode, string? StyleNo);

/// <summary>The new style and its place in the package.</summary>
public sealed record PackageCopyResultDto(int StyleId, int PackageItemId);

// ----- What to offer (roadmap 5.14, pack.usp_Offer_Suggest) -----

/// <summary>The client in the library: its brand, accounts of that brand, styles, first and latest season.</summary>
public sealed record OfferProfileDto(string Brand, string CustomerName, int Accounts, int Styles, string? FirstSeason, string? LatestSeason);

/// <summary>A garment the client buys: its styles and latest season; Offered / Won = styles of it offered in packages sent and won (sampling or ordered).</summary>
public sealed record OfferCategoryDto(string Category, int Styles, string? LatestSeason, int Offered = 0, int Won = 0);

/// <summary>
/// A style to offer. Section: Repitch (the brand's own, not carried over since; Detail1 colourways, Detail2 seasons since),
/// Gap (a garment the brand rarely bought; Detail1 its styles of it, Detail2 LT's) or Fabric (built on fabrics the brand buys;
/// Detail1 shared fabrics). SameBrand false = offered as a copy. CategoryOffered / CategoryWon: this client's record on the garment
/// (styles offered in packages sent, won = sampling or ordered); results move a garment's suggestions up or down.
/// </summary>
public sealed record OfferSuggestionDto(
    string Section, int StyleId, string StyleNo, string SeasonCode, string CustomerCode, string? ModelName, string? Description, string? ProductTypeName,
    string? Category, string? ImageUrl, string? SketchUrl, bool SameBrand, int? Detail1, int? Detail2, int CategoryOffered = 0, int CategoryWon = 0);

// ----- Hit rate (roadmap 5.17, pack.usp_HitRate) -----

public sealed record SetOutcomeRequest(string? Outcome);

/// <summary>
/// Styles offered (in packages sent to the client) and what came of them. Clients' answers are votes, one per person per
/// link per style: LikeVotes / PassVotes, and per style AllLiked (only likes), Mixed (both) or AllPassed (only passes);
/// Answered = styles with any vote. Sampling / Ordered / Dropped as set on the package page. Key: garment, client or source.
/// </summary>
public sealed record HitRateRowDto(
    string? Key, int Offered, int Answered, int LikeVotes, int PassVotes, int AllLiked, int Mixed, int AllPassed, int Sampling, int Ordered, int Dropped,
    int Packages);

public sealed record HitRateDto(HitRateRowDto Totals, IReadOnlyList<HitRateRowDto> ByGarment, IReadOnlyList<HitRateRowDto> ByClient, IReadOnlyList<HitRateRowDto> BySource);

/// <summary>Profile null = the client has no styles in the library yet (gaps and fabrics then have nothing to go by).</summary>
public sealed record OfferDto(OfferProfileDto? Profile, IReadOnlyList<OfferCategoryDto> Categories, IReadOnlyList<OfferSuggestionDto> Suggestions);
