using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>SKE has a profile (min 50% recycled, restricted PVC, one mill); style 421 breaks the recycled rule, style 4 holds PVC.</summary>
public sealed class FakeClientProfileRepository : IClientProfileRepository
{
    public List<(string ClientKey, SaveClientProfileRequest Request, string ChangedBy)> Saved { get; } = [];
    public List<(string User, string Kind, string RefKey, int? Days)> Dismissed { get; } = [];
    public FollowUpOptions? LastOptions { get; private set; }
    public (string Customer, IReadOnlyList<int> StyleIds)? LastCheck { get; private set; }

    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];

    public Task<IReadOnlyList<ClientSummaryDto>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ClientSummaryDto>>(
        [
            new("SKE", "Skechers", true, 5, 859, 2, true, 8m, 14m, "USD", 50, "PVC", "merch", 1, new DateTime(2026, 11, 20), "2027-SS"),
            new("TMS", "TMS", false, 1, 263, 0, false, null, null, null, null, null, null, 0, null, null),
        ]);

    public Task<ClientProfileDto> GetAsync(string clientKey, CancellationToken ct = default)
        => Task.FromResult(clientKey == "SKE"
            ? new ClientProfileDto("SKE", 8m, 14m, "USD", 50, "PVC", "merch", null, "merch", DateTime.UtcNow, RowVer, [new("1100131", "Mill A")],
                [new("2027-SS", new DateOnly(2026, 11, 20), "Buyers visit")])
            : new ClientProfileDto(clientKey, null, null, "USD", null, null, null, null, null, null, null, [], []));

    public Task SaveAsync(string clientKey, SaveClientProfileRequest request, string changedBy, CancellationToken ct = default)
    {
        if (clientKey == "NOPE") throw new StyleRuleException(404, "This client is not in the library (a brand, or a customer without a brand).");
        Saved.Add((clientKey, request, changedBy));
        return Task.CompletedTask;
    }

    public Task<ClientCheckDto> CheckAsync(string customerCode, IReadOnlyList<int> styleIds, CancellationToken ct = default)
    {
        LastCheck = (customerCode, styleIds);
        return Task.FromResult(customerCode != "SKE"
            ? new ClientCheckDto(null, [])
            : new ClientCheckDto("SKE", new ClientIssueDto[] { new(421, "Recycled", "50% (needs 60%)"), new(4, "Restricted", "PVC: PVC PRINT"), new(4, "Mill", "Other Mill") }
                .Where(i => styleIds.Contains(i.StyleId)).ToList()));
    }

    public Task<IReadOnlyList<FollowUpDto>> FollowUpsAsync(string userName, FollowUpOptions options, CancellationToken ct = default)
    {
        LastOptions = options;
        return Task.FromResult<IReadOnlyList<FollowUpDto>>(
        [
            new("Sample", "I7", 5, "SS27 outdoor", 1, "Buyers", 7, "L326M062", null, null, 4, null, "Cleo: Size M"),
            new("Pitch", "P1", null, null, null, null, null, null, "SKE", "2027-SS", 9, new DateTime(2026, 11, 7), "Buyers visit"),
        ]);
    }

    public Task DismissFollowUpAsync(string userName, string kind, string refKey, int? days, CancellationToken ct = default)
    {
        Dismissed.Add((userName, kind, refKey, days));
        return Task.CompletedTask;
    }
}

public sealed class ClientProfilesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<(HttpClient Client, string Name)> SignInAsync(string role)
    {
        var name = "cp" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, name);
    }

    [Fact]
    public async Task Readers_see_profiles_and_only_editors_change_them()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        Assert.Equal(2, (await viewer.GetFromJsonAsync<List<ClientSummaryDto>>("/api/v1/client-profiles"))!.Count);
        var empty = await viewer.GetFromJsonAsync<ClientProfileDto>("/api/v1/client-profiles/TMS");
        Assert.Null(empty!.RowVer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/client-profiles/TMS", new { minRecycledPct = 50 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Factory")).Client.GetAsync("/api/v1/client-profiles")).StatusCode);
    }

    [Fact]
    public async Task Saving_checks_the_rules_and_trims_what_was_typed()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        var bad = await merch.PutAsJsonAsync("/api/v1/client-profiles/SKE", new { priceMin = 20, priceMax = 10, currency = "dollars", minRecycledPct = 120 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync();
        foreach (var field in new[] { "priceMax", "currency", "minRecycledPct" }) Assert.Contains(field, body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent, (await merch.PutAsJsonAsync("/api/v1/client-profiles/SKE", new
        {
            priceMin = 8, priceMax = 14, currency = "usd", minRecycledPct = 50, restrictedTerms = " PVC, PFAS ", accountManager = " merch ",
            mills = new[] { " 1100131 ", "1100131" }, pitchDates = new[] { new { seasonCode = " 2027-ss ", pitchDate = "2026-11-20", note = " " } },
        })).StatusCode);
        var (key, saved, by) = factory.ClientProfiles.Saved.Last();
        Assert.Equal(("SKE", "PVC, PFAS", "merch", name), (key, saved.RestrictedTerms, saved.AccountManager, by));
        Assert.Equal(["1100131"], saved.Mills!);
        Assert.Equal(new PitchDateDto("2027-SS", new DateOnly(2026, 11, 20), null), saved.PitchDates!.Single());
        Assert.Equal(HttpStatusCode.NotFound, (await merch.PutAsJsonAsync("/api/v1/client-profiles/NOPE", new { })).StatusCode);
    }

    [Fact]
    public async Task Packages_and_what_to_offer_are_checked_against_the_profile()
    {
        var (viewer, _) = await SignInAsync("Viewer");
        var check = await viewer.GetFromJsonAsync<ClientCheckDto>("/api/v1/style-packages/5/client-check");
        Assert.Equal("SKE", check!.ClientKey);
        Assert.Equal([421, 1], factory.ClientProfiles.LastCheck!.Value.StyleIds);
        Assert.Equal("Recycled", Assert.Single(check.Issues).Kind);

        var offer = await viewer.GetFromJsonAsync<OfferDto>("/api/v1/style-packages/offer?customer=SKE");
        Assert.Equal(50, offer!.ClientRules!.MinRecycledPct);
        Assert.Equal(["Restricted", "Mill"], offer.Suggestions.Single(s => s.StyleId == 4).Issues!.Select(i => i.Kind));
        Assert.Empty(offer.Suggestions.Single(s => s.StyleId == 3).Issues!);
    }

    [Fact]
    public async Task Follow_ups_are_the_users_own_and_can_be_done_or_put_off()
    {
        var (merch, name) = await SignInAsync("Merchandiser");
        var list = await merch.GetFromJsonAsync<List<FollowUpDto>>("/api/v1/follow-ups");
        Assert.Equal(["Sample", "Pitch"], list!.Select(f => f.Kind));
        Assert.Equal((5, 7, 3, 3, 14), (factory.ClientProfiles.LastOptions!.NotOpenedDays, factory.ClientProfiles.LastOptions.NoAnswerDays,
            factory.ClientProfiles.LastOptions.ExpiringDays, factory.ClientProfiles.LastOptions.SampleDays, factory.ClientProfiles.LastOptions.PitchDays));

        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/follow-ups/dismiss", new { kind = "Party", refKey = "I7" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await merch.PostAsJsonAsync("/api/v1/follow-ups/dismiss", new { kind = "Sample", refKey = "I7", days = 90 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PostAsJsonAsync("/api/v1/follow-ups/dismiss", new { kind = "sample", refKey = " I7 ", days = 3 })).StatusCode);
        Assert.Equal((name, "Sample", "I7", (int?)3), factory.ClientProfiles.Dismissed.Last());
        Assert.Equal(HttpStatusCode.NoContent, (await merch.PostAsJsonAsync("/api/v1/follow-ups/dismiss", new { kind = "Pitch", refKey = "P1" })).StatusCode);
        Assert.Null(factory.ClientProfiles.Dismissed.Last().Days);   // done for good
    }
}
