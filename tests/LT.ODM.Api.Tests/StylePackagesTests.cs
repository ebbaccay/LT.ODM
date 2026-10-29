using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.Concepts;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.Files;
using LT.ODM.Infrastructure.StyleLibrary;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakePackageRepository : IPackageRepository
{
    public List<(string Action, int? PackageId, object? Request, string ChangedBy)> Writes { get; } = [];
    public PackageListQuery? LastList { get; private set; }

    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];

    public Task<PackagePageDto> ListAsync(PackageListQuery query, CancellationToken ct = default)
    {
        LastList = query;
        return Task.FromResult(new PackagePageDto([new(1, "SS27 outdoor", "SKE", "Skechers", "2027-SS", "Draft", "merch", null, 2, null, DateTime.UtcNow)], 1));
    }

    public Task<PackageDetailDto?> GetAsync(int packageId, CancellationToken ct = default)
        => Task.FromResult(packageId == 404 ? null : new PackageDetailDto(
            new PackageHeaderDto(packageId, "SS27 outdoor", "SKE", "Skechers", "SKE", "2027-SS", null, "Draft", "merch", null, "merch", DateTime.UtcNow, null, null, RowVer),
            [
                new PackageItemDto(7, 421, 1, "Best seller in FW26", "SECRET margin note", false, "Styles", "L326M062", "2026-FA", "SKE", null, "Polo", null, "POLO",
                    "MALE", "KNT", "Knit", 90, null, null, true, false, 2, 1, "merch", DateTime.UtcNow, null, null, RowVer,
                    [new("013P", "Navy", "INRANGE", null, true, false), new("018", "Black", "INRANGE", null, false, false)],
                    [new("Fabric", "M1", "REC. POLY RIPSTOP", null, true), new("Trim", "T1", "YKK ZIPPER", null, false)]),
                new PackageItemDto(8, 1, 2, null, null, true, "Styles", "F2325M142", "2025-FW", "ADI", null, null, null, null, null, null, null, null, null, null,
                    true, true, 0, 0, "merch", DateTime.UtcNow, null, null, RowVer, [], []),
            ],
            // Concept stories: one covers item 7 (and the other brand's item 8, left out for the client); one covers item 8 only (left out).
            [
                new PackageStoryDto(3, "Packable shell", "Light, packable and made from recycled ripstop.", "Athleisure", [7, 8],
                    [new PackageStoryPictureDto(StoryImageUrl, "Mood")]),
                new PackageStoryDto(4, "Hidden", "Only another brand's style.", null, [8], []),
            ]));

    /// <summary>The story picture of concept 3 (a test may store a file under this name in the concept image store).</summary>
    public string StoryImageUrl { get; set; } = "/api/v1/concepts/images/0123456789abcdef0123456789abcdef.png";

    public (string Customer, int? PackageId, int Top)? LastOffer { get; private set; }

    public Task<OfferDto> OfferAsync(string customerCode, int? packageId, int top, CancellationToken ct = default)
    {
        if (customerCode == "NONE") throw new StyleRuleException(404, "This customer is not in the library.");
        LastOffer = (customerCode, packageId, top);
        return Task.FromResult(new OfferDto(new OfferProfileDto("SKE", "Skechers", 5, 859, "25Q2", "2027-SU"), [new OfferCategoryDto("PANTS", 420, "2027-SU")],
            [new OfferSuggestionDto("Repitch", 3, "P227W299", "27Q2", "SKE", null, null, "LONG PANTS", "PANTS", null, null, true, 8, 3),
             new OfferSuggestionDto("Gap", 4, "S2708WR0900", "2027-FW", "ADI", null, null, "T-SHIRT", "TEE", null, null, false, 22, 239)]));
    }

    public Task<IReadOnlyList<StylePackageDto>> ForStyleAsync(int styleId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StylePackageDto>>([new(1, "SS27 outdoor", "SKE", "2027-SS", "Draft", "merch", 7)]);

    public Task<int> SaveAsync(int? packageId, SavePackageRequest request, string changedBy, CancellationToken ct = default)
    {
        if (packageId == 409) throw new StyleRuleException(409, "Someone else changed this package after you opened it. Reload and try again.");
        Writes.Add(("save", packageId, request, changedBy));
        return Task.FromResult(packageId ?? 12);
    }

    public Task DeleteAsync(int packageId, byte[] rowVer, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("delete", packageId, rowVer, changedBy));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PackageAddResultDto>> AddItemsAsync(int packageId, IReadOnlyList<int> styleIds, string? addedFrom, string changedBy,
        int? conceptId = null, CancellationToken ct = default)
    {
        Writes.Add(("add", packageId, (styleIds, addedFrom), changedBy));
        LastConceptId = conceptId;
        return Task.FromResult<IReadOnlyList<PackageAddResultDto>>(
            styleIds.Select(id => new PackageAddResultDto(id, "S" + id, "2027-SS", id == 1 ? "ADI" : "SKE", id == 1 ? "OtherBrand" : "Added", id == 1 ? null : id + 100))
                .ToList());
    }

    public int? LastConceptId { get; private set; }

    public Task<PackageCopyResultDto> AddCopyAsync(int packageId, int styleId, string seasonCode, string styleNo, string changedBy, int? conceptId = null,
        CancellationToken ct = default)
    {
        LastConceptId = conceptId;
        if (styleNo == "TAKEN") throw new StyleRuleException(409, "The package customer already has a style with this number in this season.");
        Writes.Add(("copy", packageId, (styleId, seasonCode, styleNo), changedBy));
        return Task.FromResult(new PackageCopyResultDto(900, 901));
    }

    public Task SaveItemAsync(int packageId, int packageItemId, SavePackageItemRequest request, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("item", packageId, request, changedBy));
        return Task.CompletedTask;
    }

    public Task RemoveItemsAsync(int packageId, IReadOnlyList<int> packageItemIds, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("remove", packageId, packageItemIds, changedBy));
        return Task.CompletedTask;
    }

    public Task ReorderItemsAsync(int packageId, IReadOnlyList<int> packageItemIds, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("order", packageId, packageItemIds, changedBy));
        return Task.CompletedTask;
    }

    /// <summary>Copied item 7: closest other-brand picture 0.97; item 8: no picture yet.</summary>
    public Task<IReadOnlyList<PackageClosenessItemDto>> ClosenessAsync(int packageId, string model, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PackageClosenessItemDto>>(
        [
            new(7, 421, 1, 1, 1, 1, "F2325M142", "2025-FW", "ADI", "/api/v1/styles/images/a.jpg", 0.97m, ""),
            new(8, 900, 2, 0, 0, null, null, null, null, null, null, ""),
        ]);

    public (string? Customer, DateTime? From, DateTime? To)? LastHitRate { get; private set; }

    public Task SetOutcomeAsync(int packageId, int packageItemId, string? outcome, string changedBy, CancellationToken ct = default)
    {
        Writes.Add(("outcome", packageId, (packageItemId, outcome), changedBy));
        return Task.CompletedTask;
    }

    public Task<HitRateDto> HitRateAsync(string? customerCode, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        LastHitRate = (customerCode, fromUtc, toUtc);
        var row = new HitRateRowDto("JACKET", 10, 6, 7, 3, 3, 2, 1, 2, 1, 3, 2);
        return Task.FromResult(new HitRateDto(row with { Key = null }, [row], [row with { Key = "SKE" }], [row with { Key = "Offer" }]));
    }

    // ----- Share links: kept in memory by the secret's hash -----

    public Dictionary<string, (int ShareId, int PackageId, string Label, DateTime Expires, bool Revoked)> Shares { get; } = [];
    public Dictionary<int, string?> ProtectedTokens { get; } = [];
    public Dictionary<(int ShareId, int ItemId, string Voter), ShareAnswer> Answers { get; } = [];
    public List<(int ShareId, bool Counted)> Openings { get; } = [];
    public const string AllowedImage = "/api/v1/styles/images/0123456789abcdef0123456789abcdef.jpg";

    public Task<int> CreateShareAsync(int packageId, byte[] tokenHash, string? tokenProtected, string label, DateTime expiresUtc, string changedBy,
        CancellationToken ct = default)
    {
        var id = Shares.Count + 1;
        Shares[Convert.ToHexString(tokenHash)] = (id, packageId, label, expiresUtc, false);
        ProtectedTokens[id] = tokenProtected;
        return Task.FromResult(id);
    }

    public Task<string?> ShareTokenAsync(int packageId, int shareId, CancellationToken ct = default)
    {
        var share = Shares.Values.SingleOrDefault(x => x.ShareId == shareId && x.PackageId == packageId);
        return Task.FromResult(share.ShareId == 0 || share.Revoked ? null : ProtectedTokens.GetValueOrDefault(shareId));
    }

    public Task<IReadOnlyList<PackageShareDto>> ListSharesAsync(int packageId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PackageShareDto>>(Shares.Values.Where(x => x.PackageId == packageId).Select(x => new PackageShareDto(
            x.ShareId, x.Label, x.Expires, x.Revoked ? DateTime.UtcNow : null, null, 0, null, null, "merch", DateTime.UtcNow, !x.Revoked, 0, 0, 0)).ToList());

    public Task RevokeShareAsync(int packageId, int shareId, string changedBy, CancellationToken ct = default)
    {
        var key = Shares.Single(x => x.Value.ShareId == shareId && x.Value.PackageId == packageId).Key;
        Shares[key] = Shares[key] with { Revoked = true };
        return Task.CompletedTask;
    }

    public Task<OpenedShare?> OpenShareAsync(byte[] tokenHash, string? ip, string? userAgent, bool count, CancellationToken ct = default)
    {
        if (!Shares.TryGetValue(Convert.ToHexString(tokenHash), out var x) || x.Revoked || x.Expires <= DateTime.UtcNow) return Task.FromResult<OpenedShare?>(null);
        Openings.Add((x.ShareId, count));
        return Task.FromResult<OpenedShare?>(new OpenedShare(x.ShareId, x.PackageId, x.Label, x.Expires,
            Answers.Where(a => a.Key.ShareId == x.ShareId).Select(a => a.Value).ToList()));
    }

    public Task<IReadOnlyList<PackageNoticeFor>> SaveShareFeedbackAsync(byte[] tokenHash, int packageItemId, ShareFeedbackRequest answer, CancellationToken ct = default)
    {
        // Only item 7 is the client's to see (item 8 is another brand's style).
        if (!Shares.TryGetValue(Convert.ToHexString(tokenHash), out var x) || x.Revoked || packageItemId != 7) throw new StyleRuleException(404, "Not found.");
        Answers[(x.ShareId, packageItemId, SharedPackageView.VoterKey(answer.Name))] =
            new ShareAnswer(packageItemId, answer.Verdict, answer.Comment, answer.Name, DateTime.UtcNow, answer.Sample ?? false, answer.SampleNote);
        var notice = new PackageNoticeDto(Notices.Count + 1, x.PackageId, "SS27 outdoor", x.Label, answer.Verdict == "Like" ? 1 : 0, answer.Verdict == "Pass" ? 1 : 0,
            answer.Comment is null ? 0 : 1, answer.Name, DateTime.UtcNow, DateTime.UtcNow, Samples: answer.Sample == true ? 1 : 0);
        Notices.Add(new PackageNoticeFor("merch-owner", notice));
        return Task.FromResult<IReadOnlyList<PackageNoticeFor>>([new PackageNoticeFor("merch-owner", notice)]);
    }

    public List<PackageNoticeFor> Notices { get; } = [];
    public List<(string User, int? NoticeId)> NoticeReads { get; } = [];

    public Task<IReadOnlyList<PackageNoticeDto>> ListNoticesAsync(string userName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PackageNoticeDto>>(Notices.Where(n => n.UserName == userName).Select(n => n.Notice).ToList());

    public Task ReadNoticesAsync(string userName, int? noticeId, CancellationToken ct = default)
    {
        NoticeReads.Add((userName, noticeId));
        return Task.CompletedTask;
    }

    public Task<bool> ShareImageAllowedAsync(byte[] tokenHash, string imageUrl, CancellationToken ct = default)
        => Task.FromResult(Shares.ContainsKey(Convert.ToHexString(tokenHash)) && imageUrl == AllowedImage);

    public Task<bool> ShareStoryImageAllowedAsync(byte[] tokenHash, string imageUrl, CancellationToken ct = default)
        => Task.FromResult(Shares.ContainsKey(Convert.ToHexString(tokenHash)) && imageUrl == StoryImageUrl);
}


public sealed class FakePackageNotifier : IPackageNotifier
{
    public List<PackageNoticeFor> Pushed { get; } = [];

    public Task NotifyAsync(IReadOnlyList<PackageNoticeFor> notices, CancellationToken ct = default)
    {
        Pushed.AddRange(notices);
        return Task.CompletedTask;
    }
}

public sealed class StylePackagesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private const string RowVer = "AAAAAAAAAAE=";

    private async Task<(HttpClient Client, string Name)> SignInAsync(string role)
    {
        var name = "pkg" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, name);
    }

    [Fact]
    public async Task Readers_see_packages_but_only_editors_change_them()
    {
        var (viewer, name) = await SignInAsync("Viewer");
        var page = await viewer.GetFromJsonAsync<PackagePageDto>("/api/v1/style-packages?mine=true&status=sent&take=999");
        Assert.Equal(1, page!.Total);
        Assert.Equal(new PackageListQuery(null, null, null, "Sent", true, name, 0, 200), factory.Packages.LastList);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync("/api/v1/style-packages?status=lost")).StatusCode);

        var detail = await viewer.GetFromJsonAsync<PackageDetailDto>("/api/v1/style-packages/5");
        Assert.Equal("013P", detail!.Items[0].Colorways[0].ColorwayCode);
        Assert.True(detail.Items[0].Materials[0].Recycled);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/style-packages/404")).StatusCode);
        Assert.Single((await viewer.GetFromJsonAsync<List<StylePackageDto>>("/api/v1/style-packages/for-style/421"))!);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/style-packages", new { name = "x", customerCode = "SKE" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/style-packages/5/items", new { styleIds = new[] { 1 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).Client.GetAsync("/api/v1/style-packages")).StatusCode);
    }

    [Fact]
    public async Task Creating_and_saving_check_the_fields_and_the_row_version()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        var bad = await merch.PostAsJsonAsync("/api/v1/style-packages", new { name = " ", customerCode = "", status = "Lost" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var problem = await bad.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Equal(["CustomerCode", "Name", "Status"], problem!.Errors.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(k => char.ToUpperInvariant(k[0]) + k[1..]));

        var ok = await merch.PostAsJsonAsync("/api/v1/style-packages", new { name = " SS27 outdoor ", customerCode = " ske ", seasonCode = "2027-ss", status = "sent", rowVer = RowVer });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var write = factory.Packages.Writes.Last(w => w.Action == "save" && w.ChangedBy == name);
        var saved = (SavePackageRequest)write.Request!;
        Assert.Null(write.PackageId);
        Assert.Null(saved.RowVer);   // a new package ignores a row version sent by mistake
        Assert.Equal(("SS27 outdoor", "SKE", "2027-SS", "Sent"), (saved.Name, saved.CustomerCode, saved.SeasonCode, saved.Status));

        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PutAsJsonAsync("/api/v1/style-packages/5", new { name = "x", customerCode = "SKE" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await merch.PutAsJsonAsync("/api/v1/style-packages/409", new { name = "x", customerCode = "SKE", rowVer = RowVer })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.DeleteAsync("/api/v1/style-packages/5?rowVer=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.DeleteAsync($"/api/v1/style-packages/5?rowVer={Uri.EscapeDataString(RowVer)}")).StatusCode);
    }

    [Fact]
    public async Task Adding_styles_reports_each_one_and_checks_the_list()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items", new { styleIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items", new { styleIds = Enumerable.Range(1, 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items", new { styleIds = new[] { 2 }, addedFrom = "Email" })).StatusCode);

        var results = await (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items", new { styleIds = new[] { 1, 2 }, addedFrom = "similar" }))
            .Content.ReadFromJsonAsync<List<PackageAddResultDto>>();
        Assert.Equal(["OtherBrand", "Added"], results!.Select(r => r.Result));
        var write = factory.Packages.Writes.Last(w => w.Action == "add" && w.ChangedBy == name);
        Assert.Equal("Similar", (((IReadOnlyList<int>, string?))write.Request!).Item2);
    }

    [Fact]
    public async Task Item_changes_need_a_row_version_and_a_colourway_choice()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/7", new { allColorways = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/7", new { rowVer = RowVer, allColorways = false, colorwayCodes = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/7", new { rowVer = RowVer, allColorways = true, colorwayCodes = new[] { "013P" } })).StatusCode);
        Assert.Empty(((SavePackageItemRequest)factory.Packages.Writes.Last(w => w.Action == "item" && w.ChangedBy == name).Request!).ColorwayCodes!);

        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items/remove", new { packageItemIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items/remove", new { packageItemIds = new[] { 7 } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/order", new { packageItemIds = new[] { 8, 7 } })).StatusCode);
    }

    [Fact]
    public async Task A_share_link_opens_the_package_without_signing_in_and_shows_only_what_the_client_may_see()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/shares", new { label = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/shares", new { label = "Buyers", expiresInDays = 365 })).StatusCode);
        var created = await (await merch.PostAsJsonAsync("/api/v1/style-packages/5/shares", new { label = " Skechers buyers ", expiresInDays = 14 }))
            .Content.ReadFromJsonAsync<CreatedShareDto>();
        Assert.True(ShareTokens.LooksValid(created!.Token));
        Assert.True(factory.Packages.Shares.ContainsKey(Convert.ToHexString(ShareTokens.Hash(created.Token))));   // only the hash is stored
        Assert.InRange(created.ExpiresUtc, DateTime.UtcNow.AddDays(13.9), DateTime.UtcNow.AddDays(14.1));

        var client = factory.CreateHttpsClient();   // not signed in
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/share/not-a-token")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/share/{new string('a', ShareTokens.Length)}")).StatusCode);

        var page = await client.GetFromJsonAsync<SharedPackageDto>($"/api/v1/share/{created.Token}");
        Assert.Equal("Skechers buyers", page!.Label);
        var item = Assert.Single(page.Items);                                     // another brand's style is left out
        Assert.Equal("Best seller in FW26", item.Note);
        Assert.Equal(["013P"], item.Colorways.Select(c => c.Code));             // only the offered colourway
        Assert.DoesNotContain("SECRET", await client.GetStringAsync($"/api/v1/share/{created.Token}"));   // internal note never sent

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/v1/share/{created.Token}/items/7/feedback", new { verdict = "like", comment = "Love it", name = "Anna" })).StatusCode);
        Assert.Equal("Like", (await client.GetFromJsonAsync<SharedPackageDto>($"/api/v1/share/{created.Token}?name=anna"))!.Items[0].Answer!.Verdict);
        // Several people can use one link: each sees and keeps their own answer.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/v1/share/{created.Token}/items/7/feedback", new { verdict = "Pass", name = "Ben" })).StatusCode);
        Assert.Equal("Pass", (await client.GetFromJsonAsync<SharedPackageDto>($"/api/v1/share/{created.Token}?name=BEN"))!.Items[0].Answer!.Verdict);
        Assert.Equal("Like", (await client.GetFromJsonAsync<SharedPackageDto>($"/api/v1/share/{created.Token}?name=Anna"))!.Items[0].Answer!.Verdict);
        Assert.Null((await client.GetFromJsonAsync<SharedPackageDto>($"/api/v1/share/{created.Token}"))!.Items[0].Answer);
        // The answer is pushed to the package owner as a notice (the bell).
        Assert.Contains(factory.Notifier.Pushed, n => n.UserName == "merch-owner" && n.Notice.Likes == 1 && n.Notice.ClientName == "Anna");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/share/{created.Token}/items/8/feedback", new { verdict = "Like" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/share/{created.Token}/items/7/feedback", new { verdict = "Maybe" })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/share/{created.Token}/images/ffffffffffffffffffffffffffffffff.jpg")).StatusCode);
        Assert.Equal("application/pdf", (await client.GetAsync($"/api/v1/share/{created.Token}/line-sheet.pdf")).Content.Headers.ContentType!.MediaType);
        Assert.Contains(factory.Packages.Openings, o => !o.Counted);              // the PDF is not counted as an opening

        var share = (await merch.GetFromJsonAsync<List<PackageShareDto>>("/api/v1/style-packages/5/shares"))!.Single(x => x.Label == "Skechers buyers");
        // The link can be copied again to re-send it: kept encrypted, never as it is.
        Assert.NotEqual(created.Token, factory.Packages.ProtectedTokens[share.ShareId]);
        var again = await merch.GetFromJsonAsync<ShareLinkDto>($"/api/v1/style-packages/5/shares/{share.ShareId}/link");
        Assert.Equal(created.Token, again!.Token);
        var (viewerToo, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerToo.GetAsync($"/api/v1/style-packages/5/shares/{share.ShareId}/link")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PostAsync($"/api/v1/style-packages/5/shares/{share.ShareId}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/share/{created.Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await merch.GetAsync($"/api/v1/style-packages/5/shares/{share.ShareId}/link")).StatusCode);   // revoked: no copy

        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/style-packages/5/shares", new { label = "x" })).StatusCode);
    }

    [Fact]
    public async Task Outcomes_are_set_by_editors_and_the_hit_rate_is_read_by_everyone()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/7/outcome", new { outcome = "Won" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/7/outcome", new { outcome = "ordered" })).StatusCode);
        Assert.Equal((7, "Ordered"), ((int, string?))factory.Packages.Writes.Last(w => w.Action == "outcome" && w.ChangedBy == name).Request!);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/style-packages/5/items/7/outcome", new { outcome = "" })).StatusCode);
        Assert.Equal((7, (string?)null), ((int, string?))factory.Packages.Writes.Last(w => w.Action == "outcome" && w.ChangedBy == name).Request!);

        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/style-packages/5/items/7/outcome", new { outcome = "Ordered" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync("/api/v1/style-packages/hit-rate?from=2026-10-01&to=2026-09-01")).StatusCode);
        var rate = await viewer.GetFromJsonAsync<HitRateDto>("/api/v1/style-packages/hit-rate?customer=SKE&from=2026-01-01&to=2026-10-28");
        Assert.Equal(10, rate!.Totals.Offered);
        // The end day counts in full: up to the start of the next day.
        Assert.Equal(("SKE", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc)),
            ((string?, DateTime?, DateTime?))factory.Packages.LastHitRate!);
    }

    [Fact]
    public async Task Each_user_reads_and_clears_only_their_own_notices()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        factory.Packages.Notices.Add(new PackageNoticeFor(name, new PackageNoticeDto(99, 5, "SS27 outdoor", "Skechers buyers", 2, 1, 1, "Anna", DateTime.UtcNow, DateTime.UtcNow)));
        factory.Packages.Notices.Add(new PackageNoticeFor("someone-else", new PackageNoticeDto(98, 5, "SS27 outdoor", "x", 1, 0, 0, null, DateTime.UtcNow, DateTime.UtcNow)));
        var mine = await merch.GetFromJsonAsync<List<PackageNoticeDto>>("/api/v1/package-notices");
        Assert.Equal([99], mine!.Select(n => n.NoticeId));
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PostAsync("/api/v1/package-notices/99/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PostAsync("/api/v1/package-notices/read-all", null)).StatusCode);
        Assert.Equal([(name, (int?)99), (name, null)], factory.Packages.NoticeReads.Where(r => r.User == name));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync("/api/v1/package-notices")).StatusCode);
    }

    [Fact]
    public async Task Readers_get_what_to_offer_a_customer()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync("/api/v1/style-packages/offer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/style-packages/offer?customer=NONE")).StatusCode);

        var offer = await viewer.GetFromJsonAsync<OfferDto>("/api/v1/style-packages/offer?customer=%20SKE%20&packageId=7&top=999");
        Assert.Equal(("SKE", (int?)7, 50), factory.Packages.LastOffer);
        Assert.Equal(859, offer!.Profile!.Styles);
        Assert.False(offer.Suggestions.Single(s => s.Section == "Gap").SameBrand);   // offered as a copy

        var (merch, _) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.OK, (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items", new { styleIds = new[] { 3 }, addedFrom = "offer" })).StatusCode);
    }

    [Fact]
    public async Task Copies_need_a_season_and_their_own_style_number()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        var bad = await merch.PostAsJsonAsync("/api/v1/style-packages/5/items/copy", new { styleId = 1, seasonCode = " ", styleNo = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items/copy", new { styleId = 1, seasonCode = "2027-FW", styleNo = "TAKEN" })).StatusCode);

        var copy = await (await merch.PostAsJsonAsync("/api/v1/style-packages/5/items/copy", new { styleId = 1, seasonCode = " 2027-fw ", styleNo = " LT27-RUN01 " }))
            .Content.ReadFromJsonAsync<PackageCopyResultDto>();
        Assert.Equal(new PackageCopyResultDto(900, 901), copy);
        Assert.Equal((1, "2027-FW", "LT27-RUN01"), ((int, string, string))factory.Packages.Writes.Last(w => w.Action == "copy" && w.ChangedBy == name).Request!);

        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync("/api/v1/style-packages/5/items/copy", new { styleId = 1, seasonCode = "2027-FW", styleNo = "X" })).StatusCode);
    }

    [Fact]
    public async Task Line_sheets_leave_out_internal_notes_and_other_brands()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        var pdf = await viewer.GetAsync("/api/v1/style-packages/5/line-sheet.pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        Assert.Equal("SS27_outdoor_line_sheet.pdf", pdf.Content.Headers.ContentDisposition!.FileName);

        var xlsx = await viewer.GetAsync("/api/v1/style-packages/5/line-sheet.xlsx");
        using var zip = new ZipArchive(await xlsx.Content.ReadAsStreamAsync());
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = await reader.ReadToEndAsync();
        Assert.Contains("L326M062", sheet);
        Assert.Contains("REC. POLY RIPSTOP (recycled)", sheet);
        Assert.Contains("013P Navy", sheet);
        Assert.Contains("Best seller in FW26", sheet);
        Assert.DoesNotContain("018 Black", sheet);        // not offered
        Assert.DoesNotContain("SECRET", sheet);           // internal note
        Assert.DoesNotContain("F2325M142", sheet);        // another brand's style

        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/style-packages/5/line-sheet.docx")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/style-packages/404/line-sheet.pdf")).StatusCode);
    }

    [Fact]
    public async Task The_line_sheet_pdf_has_the_style_picture()
    {
        // The real file store: it only makes thumbnails of the widths in ImageRules.ThumbnailWidths and gives nothing for others.
        var root = Path.Combine(factory.FilesRoot, "linesheet-" + Guid.NewGuid().ToString("N"));
        var store = new FileSystemStyleImageStore(Options.Create(new FileStorageOptions { Root = root }));
        using var surface = SKSurface.Create(new SKImageInfo(800, 1000));
        surface.Canvas.Clear(SKColors.SteelBlue);
        using var image = surface.Snapshot();
        var name = await store.SaveAsync(image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray(), ImageKind.Jpeg);

        var package = (await factory.Packages.GetAsync(5))!;
        package = package with { Items = [package.Items[0] with { ImageUrl = "/api/v1/styles/images/" + name }] };
        var writer = new PackageLineSheetWriter(store);
        var pdf = await writer.WritePdfAsync(package, "tester", DateTime.UtcNow);
        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(pdf));

        // Excel: the picture sits in the picture column (B) of the style's row (5), fitted to the cell (at most 120 x 124 px).
        using var zip = new ZipArchive(new MemoryStream(await writer.WriteExcelAsync(package, "tester", DateTime.UtcNow)));
        Assert.NotNull(zip.GetEntry("xl/media/image1.jpg"));
        var drawing = new StreamReader(zip.GetEntry("xl/drawings/drawing1.xml")!.Open()).ReadToEnd();
        Assert.Contains("<xdr:col>1</xdr:col>", drawing);
        Assert.Contains("<xdr:row>4</xdr:row>", drawing);
        Assert.Contains($"cx=\"{99 * 9525}\" cy=\"{124 * 9525}\"", drawing);   // 800 x 1000 fitted to 124 px high
        var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Assert.Contains("<drawing r:id=\"rId1\"", sheet);
        Assert.Contains("<c r=\"A5\"><v>1</v></c>", sheet);   // numbers are numbers
    }

    [Fact]
    public async Task The_client_sees_the_concept_story_and_can_ask_for_a_sample()
    {
        var (merch, _) = await SignInAsync("Merchandiser");
        var store = factory.Services.GetRequiredService<IConceptImageStore>();
        factory.Packages.StoryImageUrl = "/api/v1/concepts/images/" + await store.SaveAsync(FakeAiImageClient.Png, ImageKind.Png);
        var created = await (await merch.PostAsJsonAsync("/api/v1/style-packages/5/shares", new { label = "Story buyers" })).Content.ReadFromJsonAsync<CreatedShareDto>();
        var client = factory.CreateHttpsClient();

        var page = (await client.GetFromJsonAsync<SharedPackageDto>($"/api/v1/share/{created!.Token}"))!;
        var story = Assert.Single(page.Stories!);                                 // the story of another brand's style only is left out
        Assert.Equal(("Packable shell", "Athleisure"), (story.Name, story.Tags));
        Assert.Equal([7], story.PackageItemIds);                                   // the other brand's item is not listed
        var file = Assert.Single(story.ImageFiles);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/share/{created.Token}/story-images/{file}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/share/{created.Token}/story-images/ffffffffffffffffffffffffffffffff.png")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/v1/share/{created.Token}/items/7/feedback", new { sample = true, sampleNote = new string('x', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/v1/share/{created.Token}/items/7/feedback", new { verdict = "Like", name = "Cleo", sample = true, sampleNote = "Size M, navy" })).StatusCode);
        var answer = (await client.GetFromJsonAsync<SharedPackageDto>($"/api/v1/share/{created.Token}?name=cleo"))!.Items[0].Answer!;
        Assert.Equal((true, "Size M, navy"), (answer.Sample, answer.SampleNote));
        Assert.Contains(factory.Notifier.Pushed, n => n.Notice.Samples == 1 && n.Notice.ClientName == "Cleo");

        // The line sheet PDF has the story picture even when no style has one.
        var writer = new PackageLineSheetWriter(factory.Services.GetRequiredService<IStyleImageStore>(), store);
        var pdf = await writer.WritePdfAsync((await factory.Packages.GetAsync(5))!, "tester", DateTime.UtcNow);
        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(pdf));
        using var zip = new ZipArchive(new MemoryStream(await writer.WriteExcelAsync((await factory.Packages.GetAsync(5))!, "tester", DateTime.UtcNow)));
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));                // the Stories sheet
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
