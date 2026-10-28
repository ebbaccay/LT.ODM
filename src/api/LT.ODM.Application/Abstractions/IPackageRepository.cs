using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.Abstractions;

/// <summary>
/// Style Packages (pack.usp_Package_*, usp_PackageItem_*). Writes throw StyleRuleException for refused changes (not found,
/// changed by someone else, brand rule).
/// </summary>
public interface IPackageRepository
{
    Task<PackagePageDto> ListAsync(PackageListQuery query, CancellationToken ct = default);

    /// <summary>Null when the package does not exist.</summary>
    Task<PackageDetailDto?> GetAsync(int packageId, CancellationToken ct = default);

    Task<IReadOnlyList<StylePackageDto>> ForStyleAsync(int styleId, CancellationToken ct = default);

    /// <summary>What to offer the customer's brand (rules, no AI); styles already in packageId are left out.</summary>
    Task<OfferDto> OfferAsync(string customerCode, int? packageId, int top, CancellationToken ct = default);

    /// <summary>Creates (packageId null) or updates a package's header; returns its id.</summary>
    Task<int> SaveAsync(int? packageId, SavePackageRequest request, string changedBy, CancellationToken ct = default);
    Task DeleteAsync(int packageId, byte[] rowVer, string changedBy, CancellationToken ct = default);

    Task<IReadOnlyList<PackageAddResultDto>> AddItemsAsync(int packageId, IReadOnlyList<int> styleIds, string? addedFrom, string changedBy,
        CancellationToken ct = default);
    /// <summary>Copies another customer's style as a new style of the package's customer (know-how only) and adds it.</summary>
    Task<PackageCopyResultDto> AddCopyAsync(int packageId, int styleId, string seasonCode, string styleNo, string changedBy, CancellationToken ct = default);
    Task SaveItemAsync(int packageId, int packageItemId, SavePackageItemRequest request, string changedBy, CancellationToken ct = default);
    Task RemoveItemsAsync(int packageId, IReadOnlyList<int> packageItemIds, string changedBy, CancellationToken ct = default);
    Task ReorderItemsAsync(int packageId, IReadOnlyList<int> packageItemIds, string changedBy, CancellationToken ct = default);

    // ----- Hit rate -----

    Task SetOutcomeAsync(int packageId, int packageItemId, string? outcome, string changedBy, CancellationToken ct = default);

    /// <summary>Styles offered in packages sent between from and to (null = no limit), optionally for one customer's brand.</summary>
    Task<HitRateDto> HitRateAsync(string? customerCode, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    // ----- Share links (no sign-in for the client) -----

    /// <summary>Makes a link (only the secret's hash is passed); a draft package becomes Sent. Returns ShareId.</summary>
    Task<int> CreateShareAsync(int packageId, byte[] tokenHash, string? tokenProtected, string label, DateTime expiresUtc, string changedBy,
        CancellationToken ct = default);

    /// <summary>The encrypted secret of an active link of the package, or null (revoked, expired, or made before links were kept).</summary>
    Task<string?> ShareTokenAsync(int packageId, int shareId, CancellationToken ct = default);
    Task<IReadOnlyList<PackageShareDto>> ListSharesAsync(int packageId, CancellationToken ct = default);
    Task RevokeShareAsync(int packageId, int shareId, string changedBy, CancellationToken ct = default);

    /// <summary>A live link by its secret's hash, or null; count = log the opening (and mark a Sent package Viewed).</summary>
    Task<OpenedShare?> OpenShareAsync(byte[] tokenHash, string? ip, string? userAgent, bool count, CancellationToken ct = default);
    /// <summary>Saves the answer; returns the notices it added to or made (package owner, link maker), none when nothing new was said.</summary>
    Task<IReadOnlyList<PackageNoticeFor>> SaveShareFeedbackAsync(byte[] tokenHash, int packageItemId, ShareFeedbackRequest answer, CancellationToken ct = default);

    /// <summary>The user's package notices, unread first, newest first.</summary>
    Task<IReadOnlyList<PackageNoticeDto>> ListNoticesAsync(string userName, CancellationToken ct = default);

    /// <summary>Marks one notice (or all, noticeId null) of the user read.</summary>
    Task ReadNoticesAsync(string userName, int? noticeId, CancellationToken ct = default);

    /// <summary>The stored image is shown on the live link's page (a style or colourway picture of the package).</summary>
    Task<bool> ShareImageAllowedAsync(byte[] tokenHash, string imageUrl, CancellationToken ct = default);
}
