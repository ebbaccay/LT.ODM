using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Concepts;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.StyleLibrary;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Concepts;

/// <summary>Concepts: concept.usp_* procedures (db/procedures/concept.procedures.sql).</summary>
public sealed class ConceptRepository(IDbConnectionFactory connectionFactory) : IConceptRepository
{
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    // ----- Reads -----

    public async Task<ConceptPageDto> ListAsync(ConceptListQuery q, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("concept.usp_Concept_List", new
        {
            q.Search, CustomerCode = q.Customer, SeasonCode = q.Season, q.Category, q.Status, q.Mine, q.UserName, q.Skip, q.Take,
        }, ct));
        var total = await grid.ReadSingleAsync<int>();
        var rows = await grid.ReadAsync<ListRow>();
        return new ConceptPageDto(rows.Select(r => new ConceptListItemDto(r.ConceptId, r.Name, r.CustomerCode, r.CustomerName, r.SeasonCode, r.ProductCategoryCode,
            r.ProductCategoryName, r.TargetMarketName, r.Gender, r.Status, r.OwnerName, r.Images, r.Styles, r.Packages, r.Tags, r.CoverUrl,
            Utc(r.LastChangedUtc))).ToList(), total);
    }

    public async Task<ConceptDetailDto?> GetAsync(int conceptId, string? model, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("concept.usp_Concept_Get", new { ConceptId = conceptId, Model = model }, ct));
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>();
        if (h is null) return null;
        var tags = (await grid.ReadAsync<CodeNameRow>()).Select(t => new CodeNameDto(t.TrendTagCode, t.Name)).ToList();
        var images = (await grid.ReadAsync<ImageRow>()).Select(i => new ConceptImageDto(i.ConceptImageId, i.ImageUrl, i.Caption, i.SortOrder, i.HasVector,
            i.VectorError, i.AddedBy, Utc(i.AddedUtc), i.ForClient)).ToList();
        var styles = (await grid.ReadAsync<StyleRow>()).Select(s => new ConceptStyleDto(s.StyleId, s.StyleNo, s.SeasonCode, s.CustomerCode, s.BrandCode,
            s.ModelName, s.Description, s.ProductTypeName, s.Gender, s.ImageUrl, s.SketchUrl, s.IsActive, s.SameBrand, s.Note, s.AddedFrom, s.AddedBy,
            Utc(s.AddedUtc))).ToList();
        var items = (await grid.ReadAsync<ConceptPackageItemDto>()).ToList();
        var developed = (await grid.ReadAsync<DevelopedRow>()).Select(s => new DevelopedStyleDto(s.StyleId, s.StyleNo, s.SeasonCode, s.CustomerCode, s.ModelName,
            s.ImageUrl, s.SketchUrl, s.OriginStyleId, s.OriginStyleNo, s.CreatedBy, Utc(s.CreatedUtc), s.Packages)).ToList();
        return new ConceptDetailDto(
            new ConceptHeaderDto(h.ConceptId, h.Name, h.CustomerCode, h.CustomerName, h.BrandCode, h.SeasonCode, h.TargetMarketCode, h.TargetMarketName,
                h.ProductCategoryCode, h.ProductCategoryName, h.Gender, h.TargetPrice, h.Description, h.Status, h.OwnerName,
                Brief(h.BriefSummary, h.BriefJson, h.BriefBy, Utc(h.BriefUtc)), h.CreatedBy, Utc(h.CreatedUtc), h.UpdatedBy, Utc(h.UpdatedUtc), h.RowVer,
                h.ClientStory),
            tags, images, styles, items, developed);
    }

    public async Task<IReadOnlyList<StyleConceptDto>> ForStyleAsync(int styleId, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<StyleConceptDto>(Proc("concept.usp_Concept_ForStyle", new { StyleId = styleId }, ct))).ToList();
    }

    public async Task<ConceptLookupsDto> LookupsAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("concept.usp_Concept_Lookups", null, ct));
        var markets = (await grid.ReadAsync<CodeNameDto>()).ToList();
        var tags = (await grid.ReadAsync<CodeNameDto>()).ToList();
        var categories = (await grid.ReadAsync<CodeNameDto>()).ToList();
        return new ConceptLookupsDto(markets, tags, categories);
    }

    public async Task<ConceptMatchContext> MatchContextAsync(int conceptId, IReadOnlyList<string> words, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("concept.usp_Concept_MatchContext",
            new { ConceptId = conceptId, Words = words.Count == 0 ? null : string.Join(',', words) }, ct));
        var types = (await grid.ReadAsync<string>()).ToList();
        var customers = (await grid.ReadAsync<string>()).ToList();
        var found = (await grid.ReadAsync<string>()).ToList();
        return new ConceptMatchContext(types, customers, found);
    }

    // ----- Writes -----

    public Task<int> SaveAsync(int? conceptId, SaveConceptRequest r, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<int>(Proc("concept.usp_Concept_Save", new
        {
            ConceptId = conceptId, RowVer = conceptId is null ? null : r.RowVer, r.Name, r.CustomerCode, r.SeasonCode, r.TargetMarketCode,
            r.ProductCategoryCode, r.Gender, r.TargetPrice, r.Description, Status = r.Status ?? "Draft", r.OwnerName,
            TrendTags = r.TrendTags is null ? null : JsonSerializer.Serialize(r.TrendTags), r.ClientStory, ChangedBy = changedBy,
        }, ct)), ct);

    public Task<byte[]> SaveBriefAsync(int conceptId, string? summary, string? briefJson, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<byte[]>(Proc("concept.usp_Concept_SaveBrief",
            new { ConceptId = conceptId, Summary = summary, BriefJson = briefJson, ChangedBy = changedBy }, ct)), ct);

    public Task DeleteAsync(int conceptId, byte[] rowVer, string changedBy, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("concept.usp_Concept_Delete", new { ConceptId = conceptId, RowVer = rowVer, ChangedBy = changedBy }, ct)), ct);

    public async Task<(int StyleId, bool? SameBrandBase)> DevelopStyleAsync(int conceptId, string seasonCode, string styleNo, int? baseStyleId,
        string? sketchUrl, string changedBy, CancellationToken ct = default)
    {
        var row = await Write(conn => conn.QuerySingleAsync<DevelopRow>(Proc("concept.usp_Concept_DevelopStyle", new
        {
            ConceptId = conceptId, SeasonCode = seasonCode, StyleNo = styleNo, BaseStyleId = baseStyleId, SketchUrl = sketchUrl, ChangedBy = changedBy,
        }, ct)), ct);
        return (row.StyleId, row.SameBrandBase);
    }

    public Task<int> AddImageAsync(int conceptId, string imageUrl, string? caption, PictureVector vector, string addedBy, CancellationToken ct = default)
        => Write(conn => conn.QuerySingleAsync<int>(Proc("concept.usp_ConceptImage_Add", new
        {
            ConceptId = conceptId, ImageUrl = imageUrl, Caption = caption, vector.Model,
            Vector = vector.Vector is null ? null : ImageSearchRepository.VectorJson(vector.Vector), VectorError = vector.Error, AddedBy = addedBy,
        }, ct)), ct);

    public Task RemoveImageAsync(int conceptId, int conceptImageId, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("concept.usp_ConceptImage_Remove", new { ConceptId = conceptId, ConceptImageId = conceptImageId }, ct)), ct);

    public Task SaveImageAsync(int conceptId, int conceptImageId, bool forClient, string? caption, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("concept.usp_ConceptImage_Save",
            new { ConceptId = conceptId, ConceptImageId = conceptImageId, ForClient = forClient, Caption = caption }, ct)), ct);

    public async Task<IReadOnlyList<ConceptImageToReadDto>> ImagesToReadAsync(int conceptId, string model, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ConceptImageToReadDto>(Proc("concept.usp_ConceptImage_ToRead", new { ConceptId = conceptId, Model = model }, ct))).ToList();
    }

    public async Task SetImageVectorAsync(int conceptImageId, string model, float[]? vector, string? error, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("concept.usp_ConceptImage_SetVector", new
        {
            ConceptImageId = conceptImageId, Model = model, Vector = vector is null ? null : ImageSearchRepository.VectorJson(vector), Error = error,
        }, ct));
    }

    public async Task<IReadOnlyList<ConceptStyleAddResultDto>> AddStylesAsync(int conceptId, IReadOnlyList<int> styleIds, string? addedFrom, string changedBy,
        CancellationToken ct = default)
        => await Write(async conn => (await conn.QueryAsync<ConceptStyleAddResultDto>(Proc("concept.usp_ConceptStyle_Add", new
        {
            ConceptId = conceptId, StyleIds = JsonSerializer.Serialize(styleIds), AddedFrom = addedFrom, ChangedBy = changedBy,
        }, ct))).ToList(), ct);

    public Task SaveStyleAsync(int conceptId, int styleId, string? note, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("concept.usp_ConceptStyle_Save", new { ConceptId = conceptId, StyleId = styleId, Note = note }, ct)), ct);

    public Task RemoveStyleAsync(int conceptId, int styleId, CancellationToken ct = default)
        => Write(conn => conn.ExecuteAsync(Proc("concept.usp_ConceptStyle_Remove", new { ConceptId = conceptId, StyleId = styleId }, ct)), ct);

    public async Task<IReadOnlyList<ConceptClosenessRow>> ClosenessAsync(int conceptId, string model, int take, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return (await conn.QueryAsync<ConceptClosenessRow>(new CommandDefinition("concept.usp_Concept_Closeness",
            new { ConceptId = conceptId, Model = model, Take = take }, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 60))).ToList();
    }

    // ----- Helpers -----

    /// <summary>The stored brief (summary + {"products","fabrics","notes"}); null when none was drafted.</summary>
    internal static ConceptBriefDto? Brief(string? summary, string? json, string? by, DateTime? utc)
    {
        if (summary is null && json is null) return null;
        IReadOnlyList<string> products = [], fabrics = [], notes = [];
        if (json is not null)
            try
            {
                using var doc = JsonDocument.Parse(json);
                IReadOnlyList<string> List(string name) => doc.RootElement.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
                    ? a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                    : [];
                (products, fabrics, notes) = (List("products"), List("fabrics"), List("notes"));
            }
            catch (JsonException) { }
        return new ConceptBriefDto(summary, products, fabrics, notes, by, utc);
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
        public int ConceptId { get; init; }
        public string Name { get; init; } = "";
        public string? CustomerCode { get; init; }
        public string? CustomerName { get; init; }
        public string? SeasonCode { get; init; }
        public string? ProductCategoryCode { get; init; }
        public string? ProductCategoryName { get; init; }
        public string? TargetMarketName { get; init; }
        public string? Gender { get; init; }
        public string Status { get; init; } = "";
        public string OwnerName { get; init; } = "";
        public int Images { get; init; }
        public int Styles { get; init; }
        public int Packages { get; init; }
        public string? Tags { get; init; }
        public string? CoverUrl { get; init; }
        public DateTime LastChangedUtc { get; init; }
    }

    private sealed class HeaderRow
    {
        public int ConceptId { get; init; }
        public string Name { get; init; } = "";
        public string? CustomerCode { get; init; }
        public string? CustomerName { get; init; }
        public string? BrandCode { get; init; }
        public string? SeasonCode { get; init; }
        public string? TargetMarketCode { get; init; }
        public string? TargetMarketName { get; init; }
        public string? ProductCategoryCode { get; init; }
        public string? ProductCategoryName { get; init; }
        public string? Gender { get; init; }
        public string? TargetPrice { get; init; }
        public string? Description { get; init; }
        public string Status { get; init; } = "";
        public string OwnerName { get; init; } = "";
        public string? BriefSummary { get; init; }
        public string? BriefJson { get; init; }
        public string? BriefBy { get; init; }
        public DateTime? BriefUtc { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public DateTime? UpdatedUtc { get; init; }
        public byte[] RowVer { get; init; } = [];
        public string? ClientStory { get; init; }
    }

    private sealed class DevelopRow
    {
        public int StyleId { get; init; }
        public bool? SameBrandBase { get; init; }
    }

    private sealed class DevelopedRow
    {
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string CustomerCode { get; init; } = "";
        public string? ModelName { get; init; }
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public int? OriginStyleId { get; init; }
        public string? OriginStyleNo { get; init; }
        public string CreatedBy { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public int Packages { get; init; }
    }

    private sealed class CodeNameRow
    {
        public string TrendTagCode { get; init; } = "";
        public string Name { get; init; } = "";
    }

    private sealed class ImageRow
    {
        public int ConceptImageId { get; init; }
        public string ImageUrl { get; init; } = "";
        public string? Caption { get; init; }
        public short SortOrder { get; init; }
        public bool HasVector { get; init; }
        public string? VectorError { get; init; }
        public string AddedBy { get; init; } = "";
        public DateTime AddedUtc { get; init; }
        public bool ForClient { get; init; }
    }

    private sealed class StyleRow
    {
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string CustomerCode { get; init; } = "";
        public string? BrandCode { get; init; }
        public string? ModelName { get; init; }
        public string? Description { get; init; }
        public string? ProductTypeName { get; init; }
        public string? Gender { get; init; }
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public bool IsActive { get; init; }
        public bool? SameBrand { get; init; }
        public string? Note { get; init; }
        public string? AddedFrom { get; init; }
        public string AddedBy { get; init; } = "";
        public DateTime AddedUtc { get; init; }
    }
}
