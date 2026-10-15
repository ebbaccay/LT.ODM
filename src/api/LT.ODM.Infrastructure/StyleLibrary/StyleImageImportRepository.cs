using System.Data;
using System.Text.Json;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>Settings > Import pictures: db/procedures/style.image-import.procedures.sql.</summary>
public sealed class StyleImageImportRepository(IDbConnectionFactory connectionFactory, StyleDashboardCache dashboard) : IStyleImageImportRepository
{
    public async Task<IReadOnlyList<(string FileKey, StyleImageMatchDto Match)>> MatchAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0) return [];
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<MatchRow>(new CommandDefinition("style.usp_StyleImageImport_Match",
            new { Keys = JsonSerializer.Serialize(keys) }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(r => (r.FileKey, new StyleImageMatchDto(r.StyleId, r.StyleNo, r.SeasonCode, r.CustomerCode, r.ModelName, r.ImageUrl,
            r.SketchUrl, r.IsActive, r.Match, StyleImageImportRules.TickedMatches.Contains(r.Match)))).ToList();
    }

    public async Task<IReadOnlyList<(int StyleId, bool Updated)>> SetPictureAsync(
        IReadOnlyCollection<int> styleIds, string field, string imageUrl, bool overwrite, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<SetRow>(new CommandDefinition("style.usp_Style_SetPicture",
            new { StyleIds = JsonSerializer.Serialize(styleIds), Field = field, ImageUrl = imageUrl, Overwrite = overwrite, ChangedBy = changedBy },
            commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
        if (rows.Any(r => r.Updated)) dashboard.Invalidate();   // the dashboard counts styles with pictures
        return rows.Select(r => (r.StyleId, r.Updated)).ToList();
    }

    public async Task<IReadOnlyList<ExistingPictureDto>> ExistingAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ExistingRow>(new CommandDefinition("style.usp_StyleImageImport_Existing",
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(r => new ExistingPictureDto(r.StyleId, r.StyleNo, r.SeasonCode, r.CustomerCode, r.ModelName, r.ImageUrl, r.SketchUrl,
            null, null, "", false)).ToList();
    }

    public async Task<IReadOnlyList<(int StyleId, bool Moved)>> MoveExistingAsync(
        IReadOnlyCollection<ExistingPictureMove> moves, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        // PascalCase names: OPENJSON ... WITH matches them case-sensitively.
        var rows = (await conn.QueryAsync<MoveRow>(new CommandDefinition("style.usp_StyleImageImport_MoveExisting",
            new { Items = JsonSerializer.Serialize(moves), ChangedBy = changedBy }, commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
        if (rows.Any(r => r.Moved)) dashboard.Invalidate();
        return rows.Select(r => (r.StyleId, r.Moved)).ToList();
    }

    private sealed class ExistingRow
    {
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string? CustomerCode { get; init; }
        public string? ModelName { get; init; }
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
    }

    private sealed class MoveRow
    {
        public int StyleId { get; init; }
        public bool Moved { get; init; }
    }

    private sealed class MatchRow
    {
        public string FileKey { get; init; } = "";
        public string Match { get; init; } = "";
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string? CustomerCode { get; init; }
        public string? ModelName { get; init; }
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public bool IsActive { get; init; }
    }

    private sealed class SetRow
    {
        public int StyleId { get; init; }
        public bool Updated { get; init; }
    }
}
