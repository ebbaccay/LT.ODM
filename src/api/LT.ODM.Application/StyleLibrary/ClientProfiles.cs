namespace LT.ODM.Application.StyleLibrary;

// ----- Client profiles (roadmap 5.13) and follow-up reminders (roadmap 5.24); db/procedures/pack.procedures.sql -----

/// <summary>
/// A client on the Client profiles page: a brand, or a customer without a brand (ClientKey), with its accounts, styles and packages,
/// whether a profile is set and its main rules, and the next pitch date.
/// </summary>
public sealed record ClientSummaryDto(
    string ClientKey, string Name, bool IsBrand, int Accounts, int Styles, int Packages, bool HasProfile, decimal? PriceMin, decimal? PriceMax, string? Currency,
    int? MinRecycledPct, string? RestrictedTerms, string? AccountManager, int Mills, DateTime? NextPitchDate, string? NextPitchSeason,
    string? MainCustomerCode = null);

/// <summary>When the client looks at offers for a season (follow-up reminders before it).</summary>
public sealed record PitchDateDto(string? SeasonCode, DateOnly PitchDate, string? Note);

/// <summary>
/// What a client accepts. PriceMin / PriceMax: FOB band in Currency (used once costs exist, roadmap 5.19). MinRecycledPct: the
/// share of fabric lines that must be written as recycled. RestrictedTerms: words (materials, substances) that must not appear
/// on a BOM, comma separated. Mills: preferred mills (supplier codes). AccountManager: the LT user who gets the pitch reminders.
/// RowVer null = no profile yet.
/// </summary>
public sealed record ClientProfileDto(
    string ClientKey, decimal? PriceMin, decimal? PriceMax, string Currency, int? MinRecycledPct, string? RestrictedTerms, string? AccountManager,
    string? Notes, string? UpdatedBy, DateTime? UpdatedUtc, byte[]? RowVer, IReadOnlyList<LookupItem> Mills, IReadOnlyList<PitchDateDto> PitchDates);

/// <summary>RowVer null creates the profile; Mills: supplier codes; PitchDates replace the ones saved.</summary>
public sealed record SaveClientProfileRequest(
    byte[]? RowVer, decimal? PriceMin, decimal? PriceMax, string? Currency, int? MinRecycledPct, string? RestrictedTerms, string? AccountManager,
    string? Notes, IReadOnlyList<string>? Mills, IReadOnlyList<PitchDateDto>? PitchDates);

/// <summary>
/// A style against the client's rules. Kind: Recycled (Detail: its share and the minimum, or "none" without fabric lines),
/// Restricted (the word and the material), Mill (suppliers that are not preferred mills).
/// </summary>
public sealed record ClientIssueDto(int StyleId, string Kind, string Detail);

/// <summary>ClientKey: the client whose profile was used (null = no profile, so nothing was checked).</summary>
public sealed record ClientCheckDto(string? ClientKey, IReadOnlyList<ClientIssueDto> Issues);

/// <summary>
/// A follow-up for the user. Kind: NotOpened, NoAnswer, Expiring (a share link), Sample (a client's sample request not handled),
/// Pitch (a client's pitch date coming with nothing sent). Days: since it started, or (Expiring, Pitch) until it is due.
/// RefKey: what it is about, for Done / Later.
/// </summary>
public sealed record FollowUpDto(
    string Kind, string RefKey, int? PackageId, string? PackageName, int? ShareId, string? ShareLabel, int? PackageItemId, string? StyleNo, string? ClientKey,
    string? SeasonCode, int Days, DateTime? DueDate, string? Note);

/// <summary>Days null = done (for good); else put off for that many days (1-30).</summary>
public sealed record DismissFollowUpRequest(string? Kind, string? RefKey, int? Days);

/// <summary>
/// Settings section "FollowUps": when the follow-up reminders start. NotOpenedDays (5): a link never opened; NoAnswerDays (7): opened
/// but not answered; ExpiringDays (3): a link ending soon; SampleDays (3): a sample request LT has not acted on; PitchDays (14):
/// a pitch date coming with nothing sent. Environment variables FollowUps__NotOpenedDays and so on.
/// </summary>
public sealed class FollowUpOptions
{
    public const string SectionName = "FollowUps";
    public int NotOpenedDays { get; set; } = 5;
    public int NoAnswerDays { get; set; } = 7;
    public int ExpiringDays { get; set; } = 3;
    public int SampleDays { get; set; } = 3;
    public int PitchDays { get; set; } = 14;
}

public static class ClientProfileRules
{
    public static readonly string[] FollowUpKinds = ["NotOpened", "NoAnswer", "Expiring", "Sample", "Pitch"];

    public static Dictionary<string, string[]> Validate(SaveClientProfileRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (r.PriceMin < 0 || r.PriceMax < 0) e["priceMin"] = ["Prices cannot be negative."];
        else if (r.PriceMin > r.PriceMax) e["priceMax"] = ["The highest price is below the lowest."];
        if (r.PriceMin > 99_999_999 || r.PriceMax > 99_999_999) e["priceMax"] = ["The price is too high."];
        if (!string.IsNullOrWhiteSpace(r.Currency) && !System.Text.RegularExpressions.Regex.IsMatch(r.Currency.Trim(), "^[A-Za-z]{3}$"))
            e["currency"] = ["Use a 3-letter currency code (e.g. USD)."];
        if (r.MinRecycledPct is < 0 or > 100) e["minRecycledPct"] = ["Use a share from 0 to 100%."];
        if (r.RestrictedTerms?.Trim().Length > 1000) e["restrictedTerms"] = ["Keep the restricted words under 1,000 characters."];
        if (r.AccountManager?.Trim().Length > 64) e["accountManager"] = ["A user name has at most 64 characters."];
        if (r.Notes?.Trim().Length > 2000) e["notes"] = ["Keep the notes under 2,000 characters."];
        if (r.Mills is { Count: > 50 } || r.Mills?.Any(m => string.IsNullOrWhiteSpace(m) || m.Trim().Length > 32) == true)
            e["mills"] = ["Choose at most 50 mills from the supplier list."];
        if (r.PitchDates is { Count: > 20 }) e["pitchDates"] = ["Keep at most 20 pitch dates."];
        else if (r.PitchDates?.Any(d => d.SeasonCode?.Trim().Length > 16 || d.Note?.Trim().Length > 200) == true)
            e["pitchDates"] = ["A season code has at most 16 characters and a note at most 200."];
        return e;
    }
}

/// <summary>Client profiles and follow-ups (pack.usp_ClientProfile_*, pack.usp_FollowUp_*). Writes throw StyleRuleException.</summary>
public interface IClientProfileRepository
{
    Task<IReadOnlyList<ClientSummaryDto>> ListAsync(CancellationToken ct = default);

    /// <summary>The client's profile, or an empty one (RowVer null) when none is set.</summary>
    Task<ClientProfileDto> GetAsync(string clientKey, CancellationToken ct = default);

    Task SaveAsync(string clientKey, SaveClientProfileRequest request, string changedBy, CancellationToken ct = default);

    /// <summary>The styles against the profile of the customer's client (no issues and ClientKey null when there is none).</summary>
    Task<ClientCheckDto> CheckAsync(string customerCode, IReadOnlyList<int> styleIds, CancellationToken ct = default);

    Task<IReadOnlyList<FollowUpDto>> FollowUpsAsync(string userName, FollowUpOptions options, CancellationToken ct = default);
    Task DismissFollowUpAsync(string userName, string kind, string refKey, int? days, CancellationToken ct = default);
}
