using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>AI Studio: style.usp_BomCheck_Run and style.usp_AiRender_* (db/procedures/style.ai.procedures.sql).</summary>
public sealed class StyleAiRepository(IDbConnectionFactory connectionFactory) : IStyleAiRepository
{
    public async Task<BomCheckDto> RunBomCheckAsync(BomCheckQuery q, int maxRows, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("style.usp_BomCheck_Run", new
        {
            q.StyleId, CustomerCode = q.Customer, SeasonCode = q.Season, MaxRows = maxRows,
        }, ct));
        var scope = await grid.ReadSingleAsync<ScopeRow>();
        var rules = (await grid.ReadAsync<BomCheckRuleCountDto>()).ToList();
        var findings = (await grid.ReadAsync<BomCheckFindingDto>()).ToList();
        return new BomCheckDto(scope.StylesChecked, scope.LinesChecked, scope.Findings, rules, findings);
    }

    public async Task<IReadOnlyList<StyleRenderDto>> ListRendersAsync(int styleId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<RenderRow>(Proc("style.usp_AiRender_List", new { StyleId = styleId }, ct))).Select(r => r.ToDto()).ToList();
    }

    public async Task<StyleRenderDto> AddRenderAsync(
        int styleId, int? colorwayId, string imageUrl, string prompt, string provider, string model, bool usedSketch, string createdBy,
        CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var row = await RuleErrors(() => conn.QuerySingleAsync<RenderRow>(Proc("style.usp_AiRender_Add", new
        {
            StyleId = styleId, ColorwayId = colorwayId, ImageUrl = imageUrl, Prompt = prompt, Provider = provider, Model = model,
            UsedSketch = usedSketch, CreatedBy = createdBy,
        }, ct)));
        return row.ToDto();
    }

    public async Task DeleteRenderAsync(int styleId, int renderId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await RuleErrors(() => conn.ExecuteAsync(Proc("style.usp_AiRender_Delete", new { StyleId = styleId, RenderId = renderId }, ct)));
    }

    public async Task<IReadOnlyList<ConceptCandidateDto>> ConceptCandidatesAsync(ConceptCriteriaDto c, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ConceptCandidateDto>(Proc("style.usp_ConceptMatch_Candidates", new
        {
            CustomerCode = c.Customer, ProductTypes = Join(c.ProductTypes), c.Gender, WeaveTypeCode = c.WeaveType, Keywords = Join(c.Keywords),
            MaterialTypes = Join(c.MaterialTypes), Take = take,
        }, ct))).ToList();
    }

    private static string? Join(IReadOnlyList<string> values) => values.Count == 0 ? null : string.Join(',', values);

    /// <summary>THROW 50400 / 50404 -> StyleRuleException with that HTTP status.</summary>
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

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed class ScopeRow
    {
        public int StylesChecked { get; init; }
        public int LinesChecked { get; init; }
        public int Findings { get; init; }
    }

    private sealed class RenderRow
    {
        public int RenderId { get; init; }
        public int StyleId { get; init; }
        public int? ColorwayId { get; init; }
        public string? ColorwayCode { get; init; }
        public string ImageUrl { get; init; } = "";
        public string Prompt { get; init; } = "";
        public string Provider { get; init; } = "";
        public string Model { get; init; } = "";
        public bool UsedSketch { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }

        public StyleRenderDto ToDto() => new(RenderId, StyleId, ColorwayId, ColorwayCode, ImageUrl, Prompt, Provider, Model, UsedSketch, CreatedBy,
            DateTime.SpecifyKind(CreatedUtc, DateTimeKind.Utc));
    }
}
