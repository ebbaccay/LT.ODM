using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Picture tags: style.usp_ImageTag_* (db/procedures/style.tags.procedures.sql).</summary>
public sealed class PictureTagRepository(IDbConnectionFactory connectionFactory) : IPictureTagRepository
{
    public async Task<IReadOnlyList<PictureToTagDto>> ToTagAsync(string model, string version, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(Proc("style.usp_ImageTag_ToTag", new { Model = model, Version = version, Take = take }, ct));
        return rows.Select(r => new PictureToTagDto(r.ImageVectorId, JsonSerializer.Deserialize<float[]>(r.Vector) ?? [], r.ProductTypeName))
            .Where(p => p.Vector.Length > 0).ToList();
    }

    public async Task<int> SaveAsync(IReadOnlyList<PictureTagsDto> items, string version, CancellationToken ct = default)
    {
        if (items.Count == 0) return 0;
        await using var conn = await connectionFactory.OpenAsync(ct);
        // PascalCase: OPENJSON ... WITH matches names case-sensitively.
        return await conn.QuerySingleAsync<int>(Proc("style.usp_ImageTag_Save", new { Items = JsonSerializer.Serialize(items), Version = version }, ct));
    }

    public async Task<IReadOnlyList<PictureTagCountDto>> SummaryAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<PictureTagCountDto>(Proc("style.usp_ImageTag_Summary", null, ct))).ToList();
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed class Row
    {
        public int ImageVectorId { get; init; }
        public string Vector { get; init; } = "[]";
        public string? ProductTypeName { get; init; }
    }
}
