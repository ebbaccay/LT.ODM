using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Material description reader: mat.usp_MaterialSpec_* (db/procedures/mat.spec.procedures.sql).</summary>
public sealed class MaterialSpecRepository(IDbConnectionFactory connectionFactory) : IMaterialSpecRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MaterialSpecPageDto> ListAsync(MaterialSpecQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("mat.usp_MaterialSpec_List", new
        {
            ContentClassCode = q.ContentClass, MaterialTypeCode = q.MaterialType, q.Status, q.Search, q.Skip, q.Take,
        }, ct));
        var counts = await grid.ReadSingleAsync<CountsRow>();
        var rows = (await grid.ReadAsync<SpecRow>()).ToList();
        return new MaterialSpecPageDto(
            new MaterialSpecCountsDto(counts.Total, counts.Unread ?? 0, counts.Pending ?? 0, counts.Accepted ?? 0, counts.Rejected ?? 0, counts.Outdated ?? 0),
            rows.Select(r => r.ToDto()).ToList(), rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<MaterialSpecDto?> GetAsync(int materialId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QuerySingleOrDefaultAsync<SpecRow>(Proc("mat.usp_MaterialSpec_Get", new { MaterialId = materialId }, ct)))?.ToDto();
    }

    public async Task<IReadOnlyList<MaterialToReadDto>> ToReadAsync(string? contentClass, string? materialType, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<MaterialToReadDto>(Proc("mat.usp_MaterialSpec_ToRead", new
        {
            ContentClassCode = contentClass, MaterialTypeCode = materialType, Take = take,
        }, ct))).ToList();
    }

    public async Task<int> SaveAsync(IReadOnlyList<MaterialSpecReading> readings, string provider, string model, string readBy, CancellationToken ct = default)
    {
        if (readings.Count == 0) return 0;
        await using var conn = await connectionFactory.OpenAsync(ct);
        // OPENJSON matches property names exactly, so PascalCase as in the procedure. Fibres goes in as a JSON array
        // (the procedure reads it AS JSON), not as a quoted string.
        var specs = "[" + string.Join(",", readings.Select(r =>
        {
            var o = JsonSerializer.SerializeToNode(r with { Fibres = null })!.AsObject();
            o["Fibres"] = r.Fibres is null ? null : System.Text.Json.Nodes.JsonNode.Parse(r.Fibres);
            return o.ToJsonString();
        })) + "]";
        return await conn.QuerySingleAsync<int>(Proc("mat.usp_MaterialSpec_Save", new
        {
            Specs = specs, Provider = Clip(provider, 100), Model = Clip(model, 200), ReadBy = readBy,
        }, ct));
    }

    public async Task<int> ReviewAsync(IReadOnlyList<int> materialIds, string status, string reviewedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        try
        {
            return await conn.QuerySingleAsync<int>(Proc("mat.usp_MaterialSpec_Review", new
            {
                MaterialIds = JsonSerializer.Serialize(materialIds), Status = status, ReviewedBy = reviewedBy,
            }, ct));
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new StyleRuleException(400, ex.Message);
        }
    }

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed class CountsRow
    {
        public int Total { get; init; }
        public int? Unread { get; init; }
        public int? Pending { get; init; }
        public int? Accepted { get; init; }
        public int? Rejected { get; init; }
        public int? Outdated { get; init; }
    }

    private sealed class SpecRow
    {
        public int MaterialId { get; init; }
        public string MaterialCode { get; init; } = "";
        public string? Description { get; init; }
        public string? ContentClassCode { get; init; }
        public string? MaterialTypeCode { get; init; }
        public int Styles { get; init; }
        public string State { get; init; } = "";
        public string? Composition { get; init; }
        public string? Fibres { get; init; }
        public decimal? RecycledPct { get; init; }
        public string? Construction { get; init; }
        public decimal? WeightGsm { get; init; }
        public decimal? WidthCm { get; init; }
        public string? SuggestedContentClass { get; init; }
        public decimal? Confidence { get; init; }
        public string? Notes { get; init; }
        public string? Provider { get; init; }
        public string? Model { get; init; }
        public string? ReadBy { get; init; }
        public DateTime? ReadUtc { get; init; }
        public string? ReviewedBy { get; init; }
        public DateTime? ReviewedUtc { get; init; }
        public int TotalCount { get; init; }

        public MaterialSpecDto ToDto() => new(MaterialId, MaterialCode, Description, ContentClassCode, MaterialTypeCode, Styles, State, Composition,
            Fibres is null ? [] : JsonSerializer.Deserialize<List<FibreDto>>(Fibres, Json) ?? [], RecycledPct, Construction, WeightGsm, WidthCm,
            SuggestedContentClass, Confidence, Notes, Provider, Model, ReadBy, Utc(ReadUtc), ReviewedBy, Utc(ReviewedUtc));

        private static DateTime? Utc(DateTime? v) => v is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }
}
