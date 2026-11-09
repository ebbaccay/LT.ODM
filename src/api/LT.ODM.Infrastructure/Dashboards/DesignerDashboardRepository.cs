using System.Data;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Dashboards;

namespace LT.ODM.Infrastructure.Dashboards;

/// <summary>The designer dashboard: db/procedures/style.designer-dashboard.procedures.sql. Per user, so not cached.</summary>
public sealed class DesignerDashboardRepository(IDbConnectionFactory connectionFactory) : IDesignerDashboardRepository
{
    public async Task<DesignerDashboardData> GetAsync(string userName, string? season, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(new CommandDefinition("style.usp_Dashboard_Designer",
            new { UserName = userName, SeasonCode = season }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        var seasons = (await grid.ReadAsync<DesignerSeasonDto>()).ToList();
        var styles = (await grid.ReadAsync<StyleRow>()).Select(r => r.ToFacts()).ToList();
        var concepts = (await grid.ReadAsync<ConceptRow>()).Select(r =>
            new DesignerConceptDto(r.ConceptId, r.Name, r.CustomerCode, r.SeasonCode, r.Status, r.Styles, Utc(r.LastChangedUtc))).ToList();
        var pitches = (await grid.ReadAsync<PitchRow>()).Select(r =>
            new DesignerPitchDto(r.PackageId, r.PackageName, r.PackageStatus, r.CustomerCode, r.StyleId, r.StyleNo, r.Outcome, Utc(r.AtUtc))).ToList();
        var team = (await grid.ReadAsync<string>()).ToList();
        return new DesignerDashboardData(seasons, styles, concepts, pitches, team);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed class StyleRow
    {
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string? Description { get; init; }
        public string? ModelName { get; init; }
        public string CustomerCode { get; init; } = "";
        public string SeasonCode { get; init; } = "";
        public string? ImageUrl { get; init; }
        public string? SketchUrl { get; init; }
        public string? ProductTypeCode { get; init; }
        public string? Gender { get; init; }
        public int? ConceptId { get; init; }
        public string? BusinessUnitCode { get; init; }
        public string? CreatedBy { get; init; }
        public string? LastChangedBy { get; init; }
        public int Colorways { get; init; }
        public int ColorwaysNotOnBom { get; init; }
        public int BomLines { get; init; }
        public int LinesNoColour { get; init; }
        public int LinesNoDetails { get; init; }
        public int LinesNoLco { get; init; }
        public int LinesNoSupplier { get; init; }
        public int Packages { get; init; }
        public string? Outcome { get; init; }
        public DateTime CreatedUtc { get; init; }
        public DateTime LastChangedUtc { get; init; }

        public DesignerStyleFacts ToFacts() => new(StyleId, StyleNo, Description, ModelName, CustomerCode, SeasonCode, ImageUrl, SketchUrl,
            ProductTypeCode, Gender, ConceptId, Colorways, ColorwaysNotOnBom, BomLines, LinesNoColour, LinesNoDetails, LinesNoLco, LinesNoSupplier,
            Packages, Outcome, Utc(CreatedUtc), Utc(LastChangedUtc), BusinessUnitCode, CreatedBy, LastChangedBy);
    }

    private sealed class ConceptRow
    {
        public int ConceptId { get; init; }
        public string Name { get; init; } = "";
        public string? CustomerCode { get; init; }
        public string? SeasonCode { get; init; }
        public string Status { get; init; } = "";
        public int Styles { get; init; }
        public DateTime LastChangedUtc { get; init; }
    }

    private sealed class PitchRow
    {
        public int PackageId { get; init; }
        public string PackageName { get; init; } = "";
        public string PackageStatus { get; init; } = "";
        public string? CustomerCode { get; init; }
        public int StyleId { get; init; }
        public string StyleNo { get; init; } = "";
        public string? Outcome { get; init; }
        public DateTime AtUtc { get; init; }
    }
}
