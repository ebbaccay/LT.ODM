using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Fill the gaps and Similar styles: db/procedures/style.gaps.procedures.sql. Every write clears the home dashboard.</summary>
public sealed class StyleGapRepository(IDbConnectionFactory connectionFactory, StyleDashboardCache dashboard) : IStyleGapRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ----- Reads -----

    public async Task<StyleGapSummaryDto> SummaryAsync(bool withSuggestions, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<StyleGapSummaryDto>(Proc("style.usp_Gaps_Summary", new { WithSuggestions = withSuggestions }, ct));
    }

    public async Task<GapPageDto> ListAsync(GapListQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("style.usp_Gaps_List", new
        {
            q.Field, CustomerCode = q.Customer, SeasonCode = q.Season, q.Show, MinConfidence = (byte)q.MinConfidence, q.Skip, q.Take,
        }, ct));
        var rows = (await grid.ReadAsync<GapRow>()).ToList();
        var counts = (await grid.ReadAsync<(byte Confidence, int Styles)>()).ToDictionary(c => c.Confidence.ToString(), c => c.Styles);
        return new GapPageDto(rows.Select(r => new GapItemDto(r.StyleId, r.CustomerCode, r.Account, r.SeasonCode, r.StyleNo, r.Description,
            r.ModelName, r.WeaveTypeCode, r.Gender, r.ProductTypeName, r.ImageUrl, r.SketchUrl, r.Value, r.ValueName, r.Category, r.Confidence,
            r.Reason, r.Source)).ToList(), rows.FirstOrDefault()?.TotalCount ?? 0, counts);
    }

    public async Task<IReadOnlyList<GapStyleToAsk>> ToAskAsync(string field, string? customer, string? season, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<GapStyleToAsk>(Proc("style.usp_Gaps_ToAsk",
            new { Field = field, CustomerCode = customer, SeasonCode = season, Take = take }, ct))).ToList();
    }

    public async Task<IReadOnlyList<GapSeasonDto>> SeasonsAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<GapSeasonDto>(Proc("style.usp_Gaps_Seasons", null, ct))).ToList();
    }

    public async Task<IReadOnlyList<LcoLineDto>> LcoForStyleAsync(int styleId, bool withAllowance, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<LcoLineDto>(Proc("style.usp_Lco_ForStyle", new { StyleId = styleId, WithAllowance = withAllowance }, ct))).ToList();
    }

    public async Task<IReadOnlyList<LcoGroupDto>> LcoSummaryAsync(bool withAllowance, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<LcoGroupDto>(Proc("style.usp_Lco_Summary", new { WithAllowance = withAllowance }, ct))).ToList();
    }

    public async Task<IReadOnlyList<SimilarStyleDto>> SimilarAsync(int styleId, int top, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await RuleErrors(async () => (await conn.QueryAsync<SimilarStyleDto>(Proc("style.usp_Style_Similar",
            new { StyleId = styleId, Top = top }, ct))).ToList());
    }

    // ----- Writes -----

    public Task<int> AcceptAsync(AcceptGapsRequest r, string field, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<int>(Proc("style.usp_Gaps_Accept", new
        {
            Field = field, Items = r.Items is null ? null : JsonSerializer.Serialize(r.Items, Json), CustomerCode = r.Customer, SeasonCode = r.Season,
            MinConfidence = (byte)Math.Clamp(r.MinConfidence ?? 3, 1, 3), ChangedBy = changedBy,
        }, ct)), ct);

    public Task<int> DismissAsync(string field, IReadOnlyList<GapValueDto> items, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<int>(Proc("style.usp_Gaps_Dismiss",
            new { Field = field, Items = JsonSerializer.Serialize(items, Json), ChangedBy = changedBy }, ct)), ct);

    public Task<int> SaveAiAsync(string field, IReadOnlyList<GapAiAnswer> answers, string provider, string model, string createdBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<int>(Proc("style.usp_Gaps_SaveAi", new
        {
            Field = field, Items = JsonSerializer.Serialize(answers, Json), Provider = Clip(provider, 100), Model = Clip(model, 200), CreatedBy = createdBy,
        }, ct)), ct);

    public Task<MergeSeasonResultDto> MergeSeasonAsync(string from, string to, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<MergeSeasonResultDto>(Proc("style.usp_Gaps_MergeSeason",
            new { From = from, To = to, ChangedBy = changedBy }, ct)), ct);

    public Task<ApplyLcoResultDto> ApplyLcoAsync(ApplyLcoRequest r, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<ApplyLcoResultDto>(Proc("style.usp_Lco_Apply", new
        {
            BomLineIds = r.BomLineIds is { Count: > 0 } ids ? JsonSerializer.Serialize(ids) : null, r.StyleId, CustomerCode = r.Customer,
            SeasonCode = r.Season, r.All, r.WithAllowance, ChangedBy = changedBy,
        }, ct)), ct);

    private async Task<T> Write<T>(Func<IDbConnection, Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var conn = await connectionFactory.OpenAsync(ct);
            return await RuleErrors(() => action(conn));
        }
        finally
        {
            dashboard.Invalidate();
        }
    }

    /// <summary>THROW 50400 / 50404 / 50409 -> StyleRuleException with that HTTP status.</summary>
    private static async Task<T> RuleErrors<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 120);

    private sealed class GapRow
    {
        public int StyleId { get; init; }
        public string CustomerCode { get; init; } = "";
        public string Account { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string StyleNo { get; init; } = "";
        public string? Description { get; init; }
        public string? ModelName { get; init; }
        public string? WeaveTypeCode { get; init; }
        public string? Gender { get; init; }
        public string? ProductTypeName { get; init; }
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public string? Value { get; init; }
        public string? ValueName { get; init; }
        public string? Category { get; init; }
        public byte Confidence { get; init; }
        public string? Reason { get; init; }
        public string? Source { get; init; }
        public int TotalCount { get; init; }
    }
}
