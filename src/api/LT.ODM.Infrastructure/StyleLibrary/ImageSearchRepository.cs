using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Image search: style.usp_ImageIndex_*, usp_ImageSearch, usp_ImageVector_*, usp_ImageVectorSearch (db/procedures/style.ai.procedures.sql).</summary>
public sealed class ImageSearchRepository(IDbConnectionFactory connectionFactory) : IImageSearchRepository
{
    public async Task<IReadOnlyList<ImageIndexSourceCountDto>> StatusAsync(string visionModel, string embeddingModel, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ImageIndexSourceCountDto>(Proc("style.usp_ImageIndex_Status", new
        {
            VisionModel = visionModel, EmbeddingModel = embeddingModel,
        }, ct))).ToList();
    }

    public async Task<IReadOnlyList<ImageToIndexDto>> ToIndexAsync(string visionModel, string embeddingModel, int take, string indexedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ImageToIndexDto>(Proc("style.usp_ImageIndex_ToIndex", new
        {
            VisionModel = visionModel, EmbeddingModel = embeddingModel, Take = take, IndexedBy = indexedBy,
        }, ct))).ToList();
    }

    public async Task<int> SaveAsync(IReadOnlyList<ImageReading> readings, string visionModel, string embeddingModel, string provider, string indexedBy,
        CancellationToken ct = default)
    {
        if (readings.Count == 0) return 0;
        await using var conn = await connectionFactory.OpenAsync(ct);
        // OPENJSON matches property names exactly: PascalCase as in the procedure. Embedding goes in as a JSON array.
        var items = JsonSerializer.Serialize(readings);
        return await conn.QuerySingleAsync<int>(Proc("style.usp_ImageIndex_Save", new
        {
            Items = items, VisionModel = visionModel, EmbeddingModel = embeddingModel, Provider = Clip(provider, 100), IndexedBy = indexedBy,
        }, ct));
    }

    public async Task<IReadOnlyList<ImageMatchDto>> SearchAsync(string embeddingModel, float[]? vector, int? fromStyleId, string? customer, int take,
        CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ImageMatchDto>(Proc("style.usp_ImageSearch", new
        {
            EmbeddingModel = embeddingModel, Embedding = vector is null ? null : VectorJson(vector), FromStyleId = fromStyleId, CustomerCode = customer, Take = take,
        }, ct))).ToList();
    }

    // ----- Look-alike vectors (style.ImageVector) -----

    public async Task<IReadOnlyList<ImageIndexSourceCountDto>> VectorStatusAsync(string model, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ImageIndexSourceCountDto>(Proc("style.usp_ImageVector_Status", new { Model = model }, ct))).ToList();
    }

    public async Task<IReadOnlyList<ImageVectorToIndexDto>> VectorsToIndexAsync(string model, int take, string indexedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ImageVectorToIndexDto>(Proc("style.usp_ImageVector_ToIndex", new
        {
            Model = model, Take = take, IndexedBy = indexedBy,
        }, ct))).ToList();
    }

    public async Task<int> SaveVectorsAsync(IReadOnlyList<ImageVectorReading> readings, string model, string indexedBy, CancellationToken ct = default)
    {
        if (readings.Count == 0) return 0;
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await conn.QuerySingleAsync<int>(Proc("style.usp_ImageVector_Save", new
        {
            Items = JsonSerializer.Serialize(readings), Model = model, IndexedBy = indexedBy,
        }, ct));
    }

    public async Task<IReadOnlyList<ImageMatchDto>> SearchVectorsAsync(string model, float[]? vector, int? fromStyleId, string? customer, int take,
        CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ImageMatchDto>(Proc("style.usp_ImageVectorSearch", new
        {
            Model = model, Vector = vector is null ? null : VectorJson(vector), FromStyleId = fromStyleId, CustomerCode = customer, Take = take,
        }, ct))).ToList();
    }

    /// <summary>"[0.1,-0.2,...]": the text SQL Server casts to vector(1024) or vector(768).</summary>
    internal static string VectorJson(float[] vector)
    {
        var sb = new StringBuilder(vector.Length * 12).Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
