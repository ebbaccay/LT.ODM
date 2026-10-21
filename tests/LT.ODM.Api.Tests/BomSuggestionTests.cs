using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>
/// Library for the suggestion tests: style 1 (adidas, men's knit recycled-polyester tee, no BOM) and three candidates:
/// 2 its earlier version, 3 another adidas tee, 4 a Skechers tee (another brand). Style 404 does not exist.
/// </summary>
public sealed class FakeBomSuggestionRepository : IBomSuggestionRepository
{
    public List<BomSuggestSourceLine> TargetLines { get; } = [];

    public Task<(BomSuggestTarget Target, IReadOnlyList<BomSuggestCandidate> Candidates)?> CandidatesAsync(int styleId, int take, CancellationToken ct = default)
    {
        if (styleId == 404) return Task.FromResult<(BomSuggestTarget, IReadOnlyList<BomSuggestCandidate>)?>(null);
        var target = new BomSuggestTarget(styleId, "ADI", "ADIDAS", "TEE", "MALE; 100%POLYESTER(100%RECYCLED); Solid; 101.0 G/SQM; T-SHIRT (SHORT SLEEVE)",
            "ADI365 TEE M", "TSS", "T-SHIRT (SHORT SLEEVE)", "KNT", "MALE", null, null, TargetLines.Count);
        IReadOnlyList<BomSuggestCandidate> candidates =
        [
            Candidate(2, "S2808MR0000", "ADI", sameBrand: true, family: true, 75),
            Candidate(3, "F2708MR0201", "ADI", sameBrand: true, family: false, 45),
            Candidate(4, "SK100", "SKE", sameBrand: false, family: false, 70),
        ];
        return Task.FromResult<(BomSuggestTarget, IReadOnlyList<BomSuggestCandidate>)?>((target, candidates));
    }

    private static BomSuggestCandidate Candidate(int id, string styleNo, string customer, bool sameBrand, bool family, int score)
        => new(id, styleNo, "2027-SS", customer, "MALE; 100%POLYESTER(100%RECYCLED); 101.0 G/SQM; TEE", "Tee", "T-SHIRT (SHORT SLEEVE)", "KNT", "MALE", true,
            sameBrand, sameBrand, family, true, true, true, true, score);

    public Task<IReadOnlyList<BomSuggestSourceLine>> LinesAsync(IReadOnlyList<int> styleIds, CancellationToken ct = default)
    {
        if (styleIds is [1]) return Task.FromResult<IReadOnlyList<BomSuggestSourceLine>>(TargetLines);
        BomSuggestSourceLine[] all =
        [
            Line(2, 10, "70038112_1", "100%Recycle Polyester,Single jersey,FD 60D/60F", "KNIT", "FAB", 1.25m, "yd"),
            Line(3, 10, "70038112_1", "100%Recycle Polyester,Single jersey,FD 60D/60F", "KNIT", "FAB", 1.35m, "yd"),
            Line(2, 240, "80020552_VN", "ADIDAS TAPE, WOVEN NON-ELASTIC 6MM", "TAPE", "ACC", 0.05m, "yd"),
            Line(3, 240, "80020552_BOWKER", "ADIDAS TAPE, WOVEN NON-ELASTIC 6MM", "TAPE", "ACC", 0.06m, "yd"),
            Line(2, 800, "62754732_COO:CAMBODIA", "ADIDAS CARE LABEL", "LBL", "LNP", 1m, "pc"),
            Line(3, 800, "62754732_COO:CAMBODIA", "ADIDAS CARE LABEL", "LBL", "LNP", 1m, "pc"),
            Line(4, 800, "SKE-CARE-01", "SKECHERS CARE LABEL", "LBL", "LNP", 1m, "pc"),
            Line(4, 300, "ZIP5000_YKK", "COIL ZIPPER #5", "ZIP", "ACC", 1m, "pc"),
        ];
        return Task.FromResult<IReadOnlyList<BomSuggestSourceLine>>(all.Where(l => styleIds.Contains(l.StyleId)).ToList());
    }

    private static int nextId = 100;

    public static BomSuggestSourceLine Line(int styleId, int part, string code, string description, string type, string section, decimal lco, string uom)
        => new(styleId, Interlocked.Increment(ref nextId), part, part, Math.Abs(code.GetHashCode()) % 100000, code, description, type, type, section,
            section switch { "FAB" => "Fabric", "ACC" => "Accessories", _ => "Labels & packaging" }, section switch { "FAB" => 1, "ACC" => 3, _ => 5 },
            null, null, "S1", "Supplier one", lco, lco, uom);

    public Task<IReadOnlyList<BomSuggestFabric>> FabricsAsync(string customerCode, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BomSuggestFabric>>(
        [
            new(900, "60099999_9", "100% RECYCLED POLYESTER SINGLE JERSEY 100 GSM", null, null, null, null, 5, 2, 2, "KNIT", "yd", null, null, "S2", "Knits Ltd", 1.3m),
            new(901, "60011111_1", "100% COTTON TWILL 220 GSM WOVEN", null, null, null, null, 9, 0, 0, "WOV", "yd", null, null, "S3", "Wovens Ltd", 1.5m),
        ]);
}

public sealed class BomSuggestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    [Theory]
    [InlineData("MALE; 100%POLYESTER(100%RECYCLED); Solid; 101.0 G/SQM; T-SHIRT", "Polyester (recycled) 100%", 101.0, null)]
    [InlineData("Men's 53% Cotton 47% Polyester WOVEN QUILTED JACKET", "Cotton 53% · Polyester 47%", null, "Woven")]
    [InlineData("87%POLYESTER(100%RECYCLED)/13%ELASTANE; 155.0 G/SQM", "Polyester (recycled) 87% · Elastane 13%", 155.0, null)]
    [InlineData("100%Recycle Polyester,Single jersey,FD 60D/60F", "Polyester (recycled) 100%", null, "Knit")]
    [InlineData("100% RECYCLE PES ,PLAIN,75D/36F", "Polyester (recycled) 100%", null, null)]
    [InlineData("LONG PANTS", null, null, null)]
    public void Reads_fibres_weight_and_weave_from_library_descriptions(string text, string? fibres, double? gsm, string? weave)
    {
        var spec = FabricSpecReader.Read(text);
        Assert.Equal(fibres, spec.FibresText);
        Assert.Equal(gsm is null ? null : (decimal)gsm, spec.WeightGsm);
        Assert.Equal(weave, spec.Weave);
    }

    [Fact]
    public void A_matching_fabric_scores_high_and_a_different_one_low()
    {
        var wanted = FabricSpecReader.Read("100%POLYESTER(100%RECYCLED); 101.0 G/SQM", "KNT");
        Assert.True(FabricSpecReader.Match(wanted, FabricSpecReader.Read("100% RECYCLED POLYESTER SINGLE JERSEY 100 GSM")) >= 90);
        Assert.True(FabricSpecReader.Match(wanted, FabricSpecReader.Read("100% COTTON TWILL 220 GSM WOVEN")) <= 20);
        Assert.Null(FabricSpecReader.Match(FabricSpecReader.Read("LONG PANTS"), FabricSpecReader.Read("100% COTTON")));
    }

    [Theory]
    [InlineData("80020552_VN", "80020552")]
    [InlineData("62754771-COO:CAMBODIA_1", "62754771")]
    [InlineData("62774264+62773143 (20CM STRING)_CAMB.", "62774264")]
    [InlineData("ART#EV51/TKT#120_VN", "ART#EV51/TKT#120")]
    [InlineData("AB_1", "AB_1")]
    public void Material_variants_share_a_base_code(string code, string baseCode) => Assert.Equal(baseCode, BomSuggestionRules.BaseCode(code));

    [Fact]
    public async Task Builds_the_bom_from_similar_styles_with_the_brand_guardrail()
    {
        var service = new BomSuggestionService(new FakeBomSuggestionRepository(), null!, null!);
        var s = (await service.SuggestAsync(1))!;

        Assert.Equal("noPicture", s.PictureNote);
        Assert.Equal("Polyester (recycled) 100%", s.Fabric!.Fibres);
        Assert.Equal(3, s.References.Count);
        Assert.True(s.References.Single(r => r.CustomerCode == "SKE").OtherBrand);
        Assert.Contains("family", s.References.Single(r => r.StyleId == 2).Reasons);

        // Labels carry the brand: only adidas's, never the Skechers label.
        var labels = s.Lines.Where(l => l.ContentClassCode == "LNP").ToList();
        Assert.Single(labels);
        Assert.Equal("62754732_COO:CAMBODIA", labels[0].Options[0].MaterialCode);
        Assert.DoesNotContain(s.Lines.SelectMany(l => l.Options), o => o.MaterialCode == "SKE-CARE-01");

        // The two tape variants count as one material used by both adidas styles, ticked.
        var tape = s.Lines.Single(l => l.MaterialTypeCode == "TAPE");
        Assert.Equal(2, tape.Options[0].UsedBy);
        Assert.StartsWith("80020552_", tape.Options[0].MaterialCode);
        Assert.True(tape.Recommended);

        // Another brand's zipper is offered as know-how but never ticked.
        var zip = s.Lines.Single(l => l.MaterialTypeCode == "ZIP");
        Assert.True(zip.Options[0].OtherBrand);
        Assert.False(zip.Recommended);

        // Fabric: the references' fabric first, a library fabric matching the description as an alternative, not the cotton twill.
        var fabric = s.Lines.Single(l => l.ContentClassCode == "FAB");
        Assert.Equal("70038112_1", fabric.Options[0].MaterialCode);
        Assert.Contains(fabric.Options, o => o.MaterialCode == "60099999_9" && o.FabricMatch >= 90);
        Assert.DoesNotContain(fabric.Options, o => o.MaterialCode == "60011111_1");
    }

    [Fact]
    public async Task Materials_already_on_the_bom_are_shown_but_never_ticked()
    {
        var repo = new FakeBomSuggestionRepository();
        repo.TargetLines.Add(FakeBomSuggestionRepository.Line(1, 240, "80020552_TW", "ADIDAS TAPE", "TAPE", "ACC", 0.05m, "yd"));
        var s = (await new BomSuggestionService(repo, null!, null!).SuggestAsync(1))!;
        var tape = s.Lines.Single(l => l.MaterialTypeCode == "TAPE");
        Assert.True(tape.AlreadyOnBom);
        Assert.False(tape.Recommended);
    }

    [Fact]
    public async Task Editors_get_suggestions_and_add_the_picked_lines_viewers_cannot()
    {
        var viewer = await SignInAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/styles/1/bom-suggestion")).StatusCode);

        var editor = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync("/api/v1/styles/404/bom-suggestion")).StatusCode);
        var suggestion = await editor.GetFromJsonAsync<BomSuggestionDto>("/api/v1/styles/1/bom-suggestion");
        Assert.NotEmpty(suggestion!.Lines);

        var before = factory.Styles.Writes.Count(w => w.Action == "bom-line");
        var picked = suggestion.Lines.Where(l => l.Recommended).Select(l => l.Options[0]).Select(o => new SaveBomLineRequest(
            null, o.PartNo, o.MaterialCode, o.Description, null, null, o.NominatedSupplierCode, o.NominatedSupplierName, o.SupplierCode, o.SupplierName,
            o.LcoConsumption, o.BrandConsumption, o.UomCode, null, [])).ToList();
        var response = await editor.PostAsJsonAsync("/api/v1/styles/1/bom-suggestion/apply", new { lines = picked });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(picked.Count, factory.Styles.Writes.Count(w => w.Action == "bom-line") - before);

        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/v1/styles/1/bom-suggestion/apply", new { lines = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/styles/1/bom-suggestion/apply", new { lines = picked })).StatusCode);
    }

    private async Task<HttpClient> SignInAsync(params string[] roles)
    {
        var name = "bs" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}
