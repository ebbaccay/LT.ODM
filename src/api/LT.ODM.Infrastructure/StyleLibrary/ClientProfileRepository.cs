using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Client profiles and follow-up reminders: pack.usp_ClientProfile_* and pack.usp_FollowUp_* (db/procedures/pack.procedures.sql).</summary>
public sealed class ClientProfileRepository(IDbConnectionFactory connectionFactory) : IClientProfileRepository
{
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public async Task<IReadOnlyList<ClientSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<SummaryRow>(Proc("pack.usp_ClientProfile_List", null, ct))).Select(r => new ClientSummaryDto(r.ClientKey, r.Name ?? r.ClientKey,
            r.IsBrand, r.Accounts, r.Styles, r.Packages, r.HasProfile, r.PriceMin, r.PriceMax, r.Currency, r.MinRecycledPct, r.RestrictedTerms, r.AccountManager,
            r.Mills, r.NextPitchDate, r.NextPitchSeason, r.MainCustomerCode)).ToList();
    }

    public async Task<ClientProfileDto> GetAsync(string clientKey, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("pack.usp_ClientProfile_Get", new { ClientKey = clientKey }, ct));
        var p = await grid.ReadSingleOrDefaultAsync<ProfileRow>();
        var mills = (await grid.ReadAsync<MillRow>()).Select(m => new LookupItem(m.SupplierCode, m.Name)).ToList();
        var dates = (await grid.ReadAsync<PitchRow>()).Select(d => new PitchDateDto(d.SeasonCode, DateOnly.FromDateTime(d.PitchDate), d.Note)).ToList();
        return p is null
            ? new ClientProfileDto(clientKey, null, null, "USD", null, null, null, null, null, null, null, mills, dates)
            : new ClientProfileDto(p.ClientKey, p.PriceMin, p.PriceMax, p.Currency.Trim(), p.MinRecycledPct, p.RestrictedTerms, p.AccountManager, p.Notes, p.UpdatedBy,
                Utc(p.UpdatedUtc), p.RowVer, mills, dates);
    }

    public async Task SaveAsync(string clientKey, SaveClientProfileRequest r, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            await conn.ExecuteAsync(Proc("pack.usp_ClientProfile_Save", new
            {
                ClientKey = clientKey, r.RowVer, r.PriceMin, r.PriceMax, Currency = string.IsNullOrWhiteSpace(r.Currency) ? "USD" : r.Currency.Trim().ToUpperInvariant(),
                MinRecycledPct = (byte?)r.MinRecycledPct, r.RestrictedTerms, r.AccountManager, r.Notes,
                Mills = JsonSerializer.Serialize(r.Mills ?? []),
                PitchDates = JsonSerializer.Serialize((r.PitchDates ?? []).Select(d => new { seasonCode = d.SeasonCode, pitchDate = d.PitchDate.ToString("yyyy-MM-dd"), note = d.Note })),
                ChangedBy = changedBy,
            }, ct));
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    public async Task<ClientCheckDto> CheckAsync(string customerCode, IReadOnlyList<int> styleIds, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("pack.usp_ClientProfile_Check",
            new { CustomerCode = customerCode, StyleIds = JsonSerializer.Serialize(styleIds) }, ct));
        var key = await grid.ReadSingleOrDefaultAsync<string?>();
        var issues = key is null || grid.IsConsumed ? [] : (await grid.ReadAsync<ClientIssueDto>()).ToList();
        return new ClientCheckDto(key, issues);
    }

    public async Task<IReadOnlyList<FollowUpDto>> FollowUpsAsync(string userName, FollowUpOptions o, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<FollowUpRow>(Proc("pack.usp_FollowUp_List", new
        {
            UserName = userName, o.NotOpenedDays, o.NoAnswerDays, o.ExpiringDays, o.SampleDays, o.PitchDays,
        }, ct))).Select(f => new FollowUpDto(f.Kind, f.RefKey, f.PackageId, f.PackageName, f.ShareId, f.ShareLabel, f.PackageItemId, f.StyleNo, f.ClientKey,
            f.SeasonCode, f.Days, f.DueDate, f.Note)).ToList();
    }

    public async Task DismissFollowUpAsync(string userName, string kind, string refKey, int? days, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("pack.usp_FollowUp_Dismiss", new { UserName = userName, Kind = kind, RefKey = refKey, Days = days }, ct));
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed class SummaryRow
    {
        public string ClientKey { get; init; } = "";
        public string? Name { get; init; }
        public bool IsBrand { get; init; }
        public int Accounts { get; init; }
        public int Styles { get; init; }
        public int Packages { get; init; }
        public bool HasProfile { get; init; }
        public decimal? PriceMin { get; init; }
        public decimal? PriceMax { get; init; }
        public string? Currency { get; init; }
        public byte? MinRecycledPct { get; init; }
        public string? RestrictedTerms { get; init; }
        public string? AccountManager { get; init; }
        public int Mills { get; init; }
        public DateTime? NextPitchDate { get; init; }
        public string? NextPitchSeason { get; init; }
        public string? MainCustomerCode { get; init; }
    }

    private sealed class ProfileRow
    {
        public string ClientKey { get; init; } = "";
        public decimal? PriceMin { get; init; }
        public decimal? PriceMax { get; init; }
        public string Currency { get; init; } = "USD";
        public byte? MinRecycledPct { get; init; }
        public string? RestrictedTerms { get; init; }
        public string? AccountManager { get; init; }
        public string? Notes { get; init; }
        public string UpdatedBy { get; init; } = "";
        public DateTime UpdatedUtc { get; init; }
        public byte[] RowVer { get; init; } = [];
    }

    private sealed class MillRow
    {
        public string SupplierCode { get; init; } = "";
        public string Name { get; init; } = "";
    }

    private sealed class PitchRow
    {
        public int PitchDateId { get; init; }
        public string? SeasonCode { get; init; }
        public DateTime PitchDate { get; init; }
        public string? Note { get; init; }
    }

    private sealed class FollowUpRow
    {
        public string Kind { get; init; } = "";
        public string RefKey { get; init; } = "";
        public int? PackageId { get; init; }
        public string? PackageName { get; init; }
        public int? ShareId { get; init; }
        public string? ShareLabel { get; init; }
        public int? PackageItemId { get; init; }
        public string? StyleNo { get; init; }
        public string? ClientKey { get; init; }
        public string? SeasonCode { get; init; }
        public int Days { get; init; }
        public DateTime? DueDate { get; init; }
        public string? Note { get; init; }
    }
}
