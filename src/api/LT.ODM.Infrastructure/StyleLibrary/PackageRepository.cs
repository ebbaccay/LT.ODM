using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Concepts;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Style Packages: pack.usp_* procedures (db/procedures/pack.procedures.sql). Packages are not on the home dashboard, so writes leave its cache alone.</summary>
public sealed class PackageRepository(IDbConnectionFactory connectionFactory) : IPackageRepository
{
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    // ----- Reads -----

    public async Task<PackagePageDto> ListAsync(PackageListQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("pack.usp_Package_List", new
        {
            q.Search, CustomerCode = q.Customer, SeasonCode = q.Season, q.Status, q.Mine, q.UserName, q.Skip, q.Take,
        }, ct));
        var total = await grid.ReadSingleAsync<int>();
        var rows = await grid.ReadAsync<ListRow>();
        return new PackagePageDto(rows.Select(r => new PackageListItemDto(r.PackageId, r.Name, r.CustomerCode, r.CustomerName, r.SeasonCode, r.Status,
            r.OwnerName, Utc(r.SentUtc), r.Items, r.CoverUrl, Utc(r.LastChangedUtc))).ToList(), total);
    }

    public async Task<PackageDetailDto?> GetAsync(int packageId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("pack.usp_Package_Get", new { PackageId = packageId }, ct));
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>();
        if (h is null) return null;
        var items = (await grid.ReadAsync<ItemRow>()).ToList();
        var colorways = (await grid.ReadAsync<ColorwayRow>()).ToLookup(c => c.PackageItemId);
        var materials = (await grid.ReadAsync<MaterialRow>()).ToLookup(m => m.PackageItemId);
        var feedback = (await grid.ReadAsync<FeedbackRow>()).ToLookup(f => f.PackageItemId);
        var storyRows = (await grid.ReadAsync<StoryRow>()).ToList();
        var storyPictures = (await grid.ReadAsync<StoryPictureRow>()).ToLookup(x => x.ConceptId);
        var stories = storyRows.Select(s => new PackageStoryDto(s.ConceptId, s.Name, s.ClientStory, s.Tags,
            (s.PackageItemIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
            storyPictures[s.ConceptId].Select(x => new PackageStoryPictureDto(x.ImageUrl, x.Caption)).ToList())).ToList();
        return new PackageDetailDto(
            new PackageHeaderDto(h.PackageId, h.Name, h.CustomerCode, h.CustomerName, h.BrandCode, h.SeasonCode, h.Description, h.Status, h.OwnerName,
                Utc(h.SentUtc), h.CreatedBy, Utc(h.CreatedUtc), h.UpdatedBy, Utc(h.UpdatedUtc), h.RowVer),
            items.Select(i => new PackageItemDto(i.PackageItemId, i.StyleId, i.SortOrder, i.PitchNote, i.InternalNote, i.AllColorways, i.AddedFrom,
                i.StyleNo, i.SeasonCode, i.CustomerCode, i.Description, i.ModelName, i.ProductTypeCode, i.ProductTypeName, i.Gender, i.WeaveTypeCode,
                i.WeaveTypeName, i.GarmentLeadTimeDays, i.ImageUrl, i.SketchUrl, i.IsActive, i.OtherBrand, i.FabricLines, i.RecycledFabricLines,
                i.CreatedBy, Utc(i.CreatedUtc), i.UpdatedBy, Utc(i.UpdatedUtc), i.RowVer,
                colorways[i.PackageItemId].Select(c => new PackageColorwayDto(c.ColorwayCode, c.ColorwayName, c.Status, c.ImageUrl, c.Offered, c.Missing))
                    .ToList(),
                materials[i.PackageItemId].Select(m => new PackageMaterialDto(m.Kind, m.MaterialCode, m.Description, m.Composition, m.Recycled)).ToList(),
                feedback[i.PackageItemId].Select(f => new PackageFeedbackDto(f.ShareId, f.Label, f.Verdict, f.Comment, f.Name, Utc(f.UpdatedUtc), f.Sample,
                    f.SampleNote)).ToList(),
                i.Outcome, i.OutcomeBy, Utc(i.OutcomeUtc), i.ConceptId, i.ConceptName, i.OriginStyleId, i.StyleConceptId))
                .ToList(), stories);
    }

    public async Task<IReadOnlyList<StylePackageDto>> ForStyleAsync(int styleId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<StylePackageDto>(Proc("pack.usp_Package_ForStyle", new { StyleId = styleId }, ct))).ToList();
    }

    public async Task<OfferDto> OfferAsync(string customerCode, int? packageId, int top, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            using var grid = await conn.QueryMultipleAsync(new CommandDefinition("pack.usp_Offer_Suggest",
                new { CustomerCode = customerCode, PackageId = packageId, Top = top }, commandType: CommandType.StoredProcedure, cancellationToken: ct,
                commandTimeout: 60));
            var profile = await grid.ReadSingleOrDefaultAsync<OfferProfileDto>();
            var categories = (await grid.ReadAsync<OfferCategoryDto>()).ToList();
            var suggestions = (await grid.ReadAsync<OfferSuggestionDto>()).ToList();
            return new OfferDto(profile, categories, suggestions);
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    // ----- Writes -----

    public Task<int> SaveAsync(int? packageId, SavePackageRequest r, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<int>(Proc("pack.usp_Package_Save", new
        {
            PackageId = packageId, RowVer = packageId is null ? null : r.RowVer, r.Name, r.CustomerCode, r.SeasonCode, r.Description,
            Status = r.Status ?? "Draft", r.OwnerName, ChangedBy = changedBy,
        }, ct)), ct);

    public Task DeleteAsync(int packageId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("pack.usp_Package_Delete", new { PackageId = packageId, RowVer = rowVer, ChangedBy = changedBy }, ct)), ct);

    public async Task<IReadOnlyList<PackageAddResultDto>> AddItemsAsync(int packageId, IReadOnlyList<int> styleIds, string? addedFrom, string changedBy,
        int? conceptId = null, CancellationToken ct = default)
        => await Write(async conn => (await conn.QueryAsync<PackageAddResultDto>(Proc("pack.usp_PackageItem_Add", new
        {
            PackageId = packageId, StyleIds = JsonSerializer.Serialize(styleIds), AddedFrom = addedFrom, ConceptId = conceptId, ChangedBy = changedBy,
        }, ct))).ToList(), ct);

    public Task<PackageCopyResultDto> AddCopyAsync(int packageId, int styleId, string seasonCode, string styleNo, string changedBy, int? conceptId = null,
        CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<PackageCopyResultDto>(Proc("pack.usp_PackageItem_AddCopy", new
        {
            PackageId = packageId, StyleId = styleId, SeasonCode = seasonCode, StyleNo = styleNo, ConceptId = conceptId, ChangedBy = changedBy,
        }, ct)), ct);

    public async Task<IReadOnlyList<PackageClosenessItemDto>> ClosenessAsync(int packageId, string model, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ClosenessRow>(new CommandDefinition("pack.usp_Package_Closeness", new { PackageId = packageId, Model = model },
            commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 60));
        return rows.Select(r => new PackageClosenessItemDto(r.PackageItemId, r.StyleId, r.OriginStyleId, r.Pictures, r.Indexed, r.MatchStyleId, r.MatchStyleNo,
            r.MatchSeasonCode, r.MatchCustomerCode, r.MatchImageUrl, r.Similarity, "")).ToList();
    }

    public Task SaveItemAsync(int packageId, int packageItemId, SavePackageItemRequest r, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("pack.usp_PackageItem_Save", new
        {
            PackageId = packageId, PackageItemId = packageItemId, r.RowVer, r.PitchNote, r.InternalNote, r.AllColorways,
            ColorwayCodes = JsonSerializer.Serialize(r.ColorwayCodes ?? []), ChangedBy = changedBy,
        }, ct)), ct);

    public Task RemoveItemsAsync(int packageId, IReadOnlyList<int> packageItemIds, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("pack.usp_PackageItem_Remove",
            new { PackageId = packageId, PackageItemIds = JsonSerializer.Serialize(packageItemIds), ChangedBy = changedBy }, ct)), ct);

    public Task ReorderItemsAsync(int packageId, IReadOnlyList<int> packageItemIds, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("pack.usp_PackageItem_Reorder",
            new { PackageId = packageId, PackageItemIds = JsonSerializer.Serialize(packageItemIds), ChangedBy = changedBy }, ct)), ct);

    // ----- Hit rate -----

    public Task SetOutcomeAsync(int packageId, int packageItemId, string? outcome, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("pack.usp_PackageItem_SetOutcome", new
        {
            PackageId = packageId, PackageItemId = packageItemId, Outcome = outcome, ChangedBy = changedBy,
        }, ct)), ct);

    public async Task<HitRateDto> HitRateAsync(string? customerCode, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition("pack.usp_HitRate",
            new { CustomerCode = customerCode, FromUtc = fromUtc, ToUtc = toUtc }, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 60));
        var totals = await grid.ReadSingleAsync<HitRateRow>();
        var byGarment = (await grid.ReadAsync<HitRateRow>()).ToList();
        var byClient = (await grid.ReadAsync<HitRateRow>()).ToList();
        var bySource = (await grid.ReadAsync<HitRateRow>()).ToList();
        static HitRateRowDto Map(HitRateRow r) => new(r.Key, r.Offered, r.Answered, r.LikeVotes, r.PassVotes, r.AllLiked, r.Mixed, r.AllPassed, r.Sampling,
            r.Ordered, r.Dropped, r.Packages);
        return new HitRateDto(Map(totals), byGarment.Select(Map).ToList(), byClient.Select(Map).ToList(), bySource.Select(Map).ToList());
    }

    // ----- Share links -----

    public Task<int> CreateShareAsync(int packageId, byte[] tokenHash, string? tokenProtected, string label, DateTime expiresUtc, string changedBy,
        CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<int>(Proc("pack.usp_Share_Create", new
        {
            PackageId = packageId, TokenHash = tokenHash, TokenProtected = tokenProtected, Label = label, ExpiresUtc = expiresUtc, ChangedBy = changedBy,
        }, ct)), ct);

    public async Task<string?> ShareTokenAsync(int packageId, int shareId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<string?>(Proc("pack.usp_Share_Token", new { PackageId = packageId, ShareId = shareId }, ct));
    }

    public async Task<IReadOnlyList<PackageShareDto>> ListSharesAsync(int packageId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ShareRow>(Proc("pack.usp_Share_List", new { PackageId = packageId }, ct)))
            .Select(r => new PackageShareDto(r.ShareId, r.Label, Utc(r.ExpiresUtc), Utc(r.RevokedUtc), r.RevokedBy, r.Views, Utc(r.FirstViewedUtc),
                Utc(r.LastViewedUtc), r.CreatedBy, Utc(r.CreatedUtc), r.Active, r.Likes, r.Passes, r.Comments, r.CanCopy)).ToList();
    }

    public Task RevokeShareAsync(int packageId, int shareId, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("pack.usp_Share_Revoke", new { PackageId = packageId, ShareId = shareId, ChangedBy = changedBy }, ct)), ct);

    public async Task<OpenedShare?> OpenShareAsync(byte[] tokenHash, string? ip, string? userAgent, bool count, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("pack.usp_Share_Open", new
        {
            TokenHash = tokenHash, Ip = ip is { Length: > 45 } ? ip[..45] : ip, UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent, Count = count,
        }, ct));
        var share = await grid.ReadSingleOrDefaultAsync<OpenedShareRow>();
        if (share is null) return null;
        var answers = (await grid.ReadAsync<AnswerRow>())
            .Select(a => new ShareAnswer(a.PackageItemId, a.Verdict, a.Comment, a.Name, Utc(a.UpdatedUtc), a.Sample, a.SampleNote)).ToList();
        return new OpenedShare(share.ShareId, share.PackageId, share.Label, Utc(share.ExpiresUtc), answers);
    }

    public async Task<IReadOnlyList<PackageNoticeFor>> SaveShareFeedbackAsync(byte[] tokenHash, int packageItemId, ShareFeedbackRequest a, CancellationToken ct = default)
        => await Write(async conn => (await conn.QueryAsync<NoticeRow>(Proc("pack.usp_Share_Feedback", new
        {
            TokenHash = tokenHash, PackageItemId = packageItemId, a.Verdict, a.Comment, a.Name, Sample = a.Sample ?? false, a.SampleNote,
        }, ct))).Select(n => new PackageNoticeFor(n.UserName, ToNotice(n))).ToList(), ct);

    public async Task<IReadOnlyList<PackageNoticeDto>> ListNoticesAsync(string userName, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<NoticeRow>(Proc("pack.usp_Notice_List", new { UserName = userName }, ct))).Select(ToNotice).ToList();
    }

    public async Task ReadNoticesAsync(string userName, int? noticeId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("pack.usp_Notice_Read", new { UserName = userName, NoticeId = noticeId }, ct));
    }

    private static PackageNoticeDto ToNotice(NoticeRow n)
        => new(n.NoticeId, n.PackageId, n.PackageName, n.ShareLabel, n.Likes, n.Passes, n.Comments, n.ClientName, Utc(n.FirstUtc), Utc(n.LastUtc), n.IsRead,
            n.Samples);

    public async Task<bool> ShareStoryImageAllowedAsync(byte[] tokenHash, string imageUrl, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<bool>(Proc("pack.usp_Share_StoryImageAllowed", new { TokenHash = tokenHash, ImageUrl = imageUrl }, ct));
    }

    public async Task<bool> ShareImageAllowedAsync(byte[] tokenHash, string imageUrl, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<bool>(Proc("pack.usp_Share_ImageAllowed", new { TokenHash = tokenHash, ImageUrl = imageUrl }, ct));
    }

    private async Task<T> Write<T>(Func<IDbConnection, Task<T>> action, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            return await action(conn);
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            // THROW 50400 / 50404 / 50409 -> StyleRuleException with that HTTP status.
            throw new StyleRuleException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed class ListRow
    {
        public int PackageId { get; init; }
        public string Name { get; init; } = "";
        public string CustomerCode { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string? SeasonCode { get; init; }
        public string Status { get; init; } = "";
        public string OwnerName { get; init; } = "";
        public DateTime? SentUtc { get; init; }
        public int Items { get; init; }
        public string? CoverUrl { get; init; }
        public DateTime LastChangedUtc { get; init; }
    }

    private sealed class HeaderRow
    {
        public int PackageId { get; init; }
        public string Name { get; init; } = "";
        public string CustomerCode { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string? BrandCode { get; init; }
        public string? SeasonCode { get; init; }
        public string? Description { get; init; }
        public string Status { get; init; } = "";
        public string OwnerName { get; init; } = "";
        public DateTime? SentUtc { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
        public byte[] RowVer { get; init; } = [];
    }

    private sealed class ItemRow
    {
        public int PackageItemId { get; init; }
        public int StyleId { get; init; }
        public short SortOrder { get; init; }
        public string? PitchNote { get; init; }
        public string? InternalNote { get; init; }
        public bool AllColorways { get; init; }
        public string? AddedFrom { get; init; }
        public string StyleNo { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string CustomerCode { get; init; } = "";
        public string? Description { get; init; }
        public string? ModelName { get; init; }
        public string? ProductTypeCode { get; init; }
        public string? ProductTypeName { get; init; }
        public string? Gender { get; init; }
        public string? WeaveTypeCode { get; init; }
        public string? WeaveTypeName { get; init; }
        public short? GarmentLeadTimeDays { get; init; }
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public bool IsActive { get; init; }
        public bool OtherBrand { get; init; }
        public int FabricLines { get; init; }
        public int RecycledFabricLines { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
        public byte[] RowVer { get; init; } = [];
        public string? Outcome { get; init; }
        public string? OutcomeBy { get; init; }
        public DateTime? OutcomeUtc { get; init; }
        public int? ConceptId { get; init; }
        public string? ConceptName { get; init; }
        public int? OriginStyleId { get; init; }
        public int? StyleConceptId { get; init; }
    }

    private sealed class ClosenessRow
    {
        public int PackageItemId { get; init; }
        public int StyleId { get; init; }
        public int? OriginStyleId { get; init; }
        public int Pictures { get; init; }
        public int Indexed { get; init; }
        public int? MatchStyleId { get; init; }
        public string? MatchStyleNo { get; init; }
        public string? MatchSeasonCode { get; init; }
        public string? MatchCustomerCode { get; init; }
        public string? MatchImageUrl { get; init; }
        public decimal? Similarity { get; init; }
    }

    private sealed class MaterialRow
    {
        public int PackageItemId { get; init; }
        public string Kind { get; init; } = "";
        public string MaterialCode { get; init; } = "";
        public string? Description { get; init; }
        public string? Composition { get; init; }
        public bool Recycled { get; init; }
    }

    private sealed class NoticeRow
    {
        public int NoticeId { get; init; }
        public string UserName { get; init; } = "";
        public int PackageId { get; init; }
        public string PackageName { get; init; } = "";
        public string ShareLabel { get; init; } = "";
        public int Likes { get; init; }
        public int Passes { get; init; }
        public int Comments { get; init; }
        public string? ClientName { get; init; }
        public DateTime FirstUtc { get; init; }
        public DateTime LastUtc { get; init; }
        public bool IsRead { get; init; }
        public int Samples { get; init; }
    }

    private sealed class StoryRow
    {
        public int ConceptId { get; init; }
        public string Name { get; init; } = "";
        public string ClientStory { get; init; } = "";
        public string? Tags { get; init; }
        public string? PackageItemIds { get; init; }
    }

    private sealed class StoryPictureRow
    {
        public int ConceptId { get; init; }
        public string ImageUrl { get; init; } = "";
        public string? Caption { get; init; }
    }

    private sealed class HitRateRow
    {
        public string? Key { get; init; }
        public int Offered { get; init; }
        public int Answered { get; init; }
        public int LikeVotes { get; init; }
        public int PassVotes { get; init; }
        public int AllLiked { get; init; }
        public int Mixed { get; init; }
        public int AllPassed { get; init; }
        public int Sampling { get; init; }
        public int Ordered { get; init; }
        public int Dropped { get; init; }
        public int Packages { get; init; }
    }

    private sealed class FeedbackRow
    {
        public int PackageItemId { get; init; }
        public int ShareId { get; init; }
        public string Label { get; init; } = "";
        public string? Verdict { get; init; }
        public string? Comment { get; init; }
        public string? Name { get; init; }
        public DateTime UpdatedUtc { get; init; }
        public bool Sample { get; init; }
        public string? SampleNote { get; init; }
    }

    private sealed class ShareRow
    {
        public int ShareId { get; init; }
        public string Label { get; init; } = "";
        public DateTime ExpiresUtc { get; init; }
        public DateTime? RevokedUtc { get; init; }
        public string? RevokedBy { get; init; }
        public int Views { get; init; }
        public DateTime? FirstViewedUtc { get; init; }
        public DateTime? LastViewedUtc { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public bool Active { get; init; }
        public bool CanCopy { get; init; }
        public int Likes { get; init; }
        public int Passes { get; init; }
        public int Comments { get; init; }
    }

    private sealed class OpenedShareRow
    {
        public int ShareId { get; init; }
        public int PackageId { get; init; }
        public string Label { get; init; } = "";
        public DateTime ExpiresUtc { get; init; }
    }

    private sealed class AnswerRow
    {
        public int PackageItemId { get; init; }
        public string? Verdict { get; init; }
        public string? Comment { get; init; }
        public string? Name { get; init; }
        public DateTime UpdatedUtc { get; init; }
        public bool Sample { get; init; }
        public string? SampleNote { get; init; }
    }

    private sealed class ColorwayRow
    {
        public int PackageItemId { get; init; }
        public string ColorwayCode { get; init; } = "";
        public string? ColorwayName { get; init; }
        public string? Status { get; init; }
        public string? ImageUrl { get; init; }
        public bool Offered { get; init; }
        public bool Missing { get; init; }
    }
}
