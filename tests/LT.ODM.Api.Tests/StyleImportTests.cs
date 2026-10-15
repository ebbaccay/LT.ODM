using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;
using System.Text;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.StyleLibrary;
using LT.ODM.Infrastructure.StyleLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

public sealed class FakeStyleImportRepository : IStyleImportRepository
{
    public List<(string FileName, StyleWorkbook Workbook, string UploadedBy)> Staged { get; } = [];
    public List<(int BatchId, IReadOnlyList<string> Keys, string CommittedBy)> Commits { get; } = [];

    private static StyleImportBatchDto Batch(int id, string status = "Staged") => new(id, "file.xlsx", status,
        new StyleImportSummary(new StyleImportStyleCounts(1, 1, 0, 0, 0), new StyleImportRowCounts(1, 1, 1), 1, 0, 0, null),
        "x", DateTime.UtcNow, null, null);

    /// <summary>Imports holding a kept workbook (draft or staged): id -> file and mapping.</summary>
    public Dictionary<int, StyleImportFile> Files { get; } = [];
    private int _nextId = 100;

    public Task<StyleImportBatchDto> CreateDraftAsync(string fileName, string storedFile, ImportColumnMapping mapping, string uploadedBy, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        lock (Files) Files[id] = new StyleImportFile(id, fileName, "Mapping", storedFile, mapping);
        return Task.FromResult(Batch(id, "Mapping") with { HasFile = true });
    }

    public Task<StyleImportFile?> GetFileAsync(int batchId, CancellationToken ct = default)
    {
        lock (Files) return Task.FromResult(Files.GetValueOrDefault(batchId));
    }

    public Task SaveBatchMappingAsync(int batchId, ImportColumnMapping mapping, CancellationToken ct = default)
    {
        lock (Files)
        {
            if (!Files.TryGetValue(batchId, out var f)) throw new StyleImportException(404, "This import does not exist.");
            Files[batchId] = f with { Mapping = mapping };
        }
        return Task.CompletedTask;
    }

    public Task<StyleImportBatchDto> RestageAsync(int batchId, ImportColumnMapping mapping, StyleWorkbook workbook, string uploadedBy, CancellationToken ct = default)
    {
        lock (Files)
        {
            var f = Files[batchId];
            Files[batchId] = f with { Status = "Staged", Mapping = mapping };
            Staged.Add((f.FileName, workbook, uploadedBy));
        }
        return Task.FromResult(Batch(batchId) with { HasFile = true });
    }

    public Task<string?> DeleteAsync(int batchId, CancellationToken ct = default)
    {
        lock (Files)
        {
            if (!Files.Remove(batchId, out var f)) throw new StyleImportException(404, "This import does not exist.");
            return Task.FromResult(f.StoredFile);
        }
    }

    public Task<IReadOnlyList<string>> PurgeFilesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<StyleImportBatchDto?> GetAsync(int batchId, CancellationToken ct = default)
        => Task.FromResult(batchId == 404 ? null : Batch(batchId));

    public Task<IReadOnlyList<StyleImportBatchDto>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StyleImportBatchDto>>([Batch(1)]);

    public Task<PagedResult<StyleImportStyleDto>> GetStylesAsync(int batchId, string? action, string? search, int skip, int take, CancellationToken ct = default)
        => Task.FromResult(new PagedResult<StyleImportStyleDto>([], 0));

    public Task<PagedResult<StyleImportIssueDto>> GetIssuesAsync(int batchId, string? severity, string? styleKey, int skip, int take, CancellationToken ct = default)
        => Task.FromResult(new PagedResult<StyleImportIssueDto>([], 0));

    public Task<StyleImportBatchDto> CommitAsync(int batchId, IReadOnlyList<string> overwriteStyleKeys, string committedBy, CancellationToken ct = default)
    {
        if (batchId == 409) throw new StyleImportException(409, "This import was already committed or cancelled.");
        Commits.Add((batchId, overwriteStyleKeys, committedBy));
        return Task.FromResult(Batch(batchId, "Committed"));
    }

    public Task CancelAsync(int batchId, string cancelledBy, CancellationToken ct = default) => Task.CompletedTask;

    public Task<StyleImportRollbackResult> RollbackAsync(int batchId, string rolledBackBy, CancellationToken ct = default)
        => batchId switch
        {
            404 => throw new StyleImportException(404, "This import does not exist."),
            409 => Task.FromResult(new StyleImportRollbackResult(Batch(batchId, "Committed"),
                [new StyleImportRollbackBlockerDto("S1", "2027-SS", "Edited after the import by x (2026-10-14 08:00 UTC).")])),
            _ => Task.FromResult(new StyleImportRollbackResult(Batch(batchId, "RolledBack") with { RolledBackBy = rolledBackBy }, [])),
        };

    public List<(int BatchId, MapImportCodeRequest Request)> Maps { get; } = [];

    public static readonly ImportNewCodesDto NewCodes = new(
        [
            new ImportNewCodeDto("uom", "yds", null, 4, 2, false, "yd", "yd"),
            new ImportNewCodeDto("gender", "MAN", null, 2, 2, false, null, null),
            new ImportNewCodeDto("businessUnit", "NEWBU", "New team", 3, 3, false, null, null),
        ],
        [
            new ImportCodeOptionDto("uom", "yd", "yd"), new ImportCodeOptionDto("gender", "MALE", "Male"), new ImportCodeOptionDto("gender", "FEMALE", "Female"),
            new ImportCodeOptionDto("businessUnit", "RUA", "Running A"),
        ]);

    public Task<ImportNewCodesDto> GetNewCodesAsync(int batchId, CancellationToken ct = default) => Task.FromResult(NewCodes);

    public Task<StyleImportBatchDto> MapCodesAsync(int batchId, IReadOnlyList<MapImportCodeItem> items, bool remember, string mappedBy, CancellationToken ct = default)
    {
        if (items.Any(i => i.Code == "nonsense")) throw new StyleImportException(400, "Choose a code that is already on the list.");
        foreach (var i in items) Maps.Add((batchId, new MapImportCodeRequest(i.List!, i.Value!, i.Code!, remember)));
        return Task.FromResult(Batch(batchId));
    }

    public List<(int BatchId, GroupImportCustomersRequest Request, string By)> Groups { get; } = [];

    public Task<StyleImportBatchDto> GroupCustomersAsync(int batchId, GroupImportCustomersRequest request, string mappedBy, CancellationToken ct = default)
    {
        Groups.Add((batchId, request, mappedBy));
        return Task.FromResult(Batch(batchId));
    }

    public List<(int BatchId, BrandImportCustomersRequest Request, string By)> Brands { get; } = [];

    public Task<StyleImportBatchDto> BrandCustomersAsync(int batchId, BrandImportCustomersRequest request, string mappedBy, CancellationToken ct = default)
    {
        Brands.Add((batchId, request, mappedBy));
        return Task.FromResult(Batch(batchId));
    }

    public List<(string List, string Value)> Remembered { get; } = [("supplier", "0001100131")];

    public Task<bool> ForgetCodeAsync(string list, string value, CancellationToken ct = default)
        => Task.FromResult(Remembered.Remove((list, value)));

    public List<ImportMappingDto> Mappings { get; } = [];

    public Task<IReadOnlyList<ImportMappingDto>> ListMappingsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ImportMappingDto>>(Mappings.ToList());

    public Task<ImportMappingDto> SaveMappingAsync(string name, ImportColumnMapping mapping, string savedBy, CancellationToken ct = default)
    {
        Mappings.RemoveAll(m => m.Name == name);
        var saved = new ImportMappingDto(Mappings.Count + 1, name, mapping, savedBy, DateTime.UtcNow);
        Mappings.Add(saved);
        return Task.FromResult(saved);
    }

    public Task<bool> DeleteMappingAsync(int mappingId, CancellationToken ct = default)
        => Task.FromResult(Mappings.RemoveAll(m => m.MappingId == mappingId) > 0);
}

/// <summary>Writes small .xlsx files (inline strings and numbers) for the reader and upload tests.</summary>
internal static class TestWorkbook
{
    public static byte[] Create(params (string Name, object?[][] Rows)[] sheets)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Add("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
                "</Types>");
            Add("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"{ns}\" xmlns:r=\"{rel}\"><sheets>" +
                string.Concat(sheets.Select((s, i) => $"<sheet name=\"{SecurityElement.Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) +
                "</sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"{rel}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
                "</Relationships>");
            for (var s = 0; s < sheets.Length; s++)
            {
                var xml = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"{ns}\"><sheetData>");
                for (var r = 0; r < sheets[s].Rows.Length; r++)
                {
                    xml.Append($"<row r=\"{r + 1}\">");
                    for (var c = 0; c < sheets[s].Rows[r].Length; c++)
                    {
                        var cell = $"{(char)('A' + c)}{r + 1}";
                        switch (sheets[s].Rows[r][c])
                        {
                            case null: break;
                            case double or int or decimal:
                                xml.Append($"<c r=\"{cell}\"><v>{Convert.ToString(sheets[s].Rows[r][c], System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                                break;
                            default:
                                xml.Append($"<c r=\"{cell}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(sheets[s].Rows[r][c]!.ToString())}</t></is></c>");
                                break;
                        }
                    }
                    xml.Append("</row>");
                }
                Add($"xl/worksheets/sheet{s + 1}.xml", xml.Append("</sheetData></worksheet>").ToString());
            }
        }
        return buffer.ToArray();
    }

    public const string GoStyle = "26FA_M_PT08-HK BIG LONG NAME WITH SPACES 1234567890";
    public const string GoArticle = "5754 RED/GREEN MULTI COLOURWAY";

    /// <summary>The GO style export's layout: other sheet names and headings, the material code in Gen_Mat_ID.</summary>
    public static byte[] GoExport() => Create(
        ("Style_Parm", [
            ["Cust", "Cust_name", "Season_id", "Style_id", "Prod_tyle_id", "ProdType_Desc"],
            ["0000001304", "TJ Skechers Mens Export(TOMG)", "26FW", GoStyle, "70001", "LONG PANTS"],
        ]),
        ("Style_Articles", [
            ["Cust", "Season_id", "Style_id", "SeqNo", "ArticleID", "Description"],
            ["0000001304", "26FW", GoStyle, 1, GoArticle, "RED"],
        ]),
        ("Style_BOM", [
            ["Cust", "season_id", "style_id", "part_no", "AD_Mat_ID", "Gen_Mat_ID", "ASAD_Consump_Allowance", "AD_COnsump_UOM", "ArticleID"],
            ["0000001304", "26FW", GoStyle, 2, "NULL", 1005902, 0.03, "PC", "NULL"],
        ]));

    /// <summary>A valid three-sheet workbook with one style, one article and one BOM row.</summary>
    public static byte[] Valid() => Create(
        ("Style Header", [
            ["Cust", "Season_ID", "Style_ID", "Model_id", "Garment_Lead_time", "Unused column"],
            ["ADI", "2027-SS", "S2508MR1212_SS27", "KKK60", 90, "x"],
        ]),
        ("Article", [
            ["Cust", "Season ID", "Style ID", "SeqNo", "Article ID", "Description", "Status"],
            ["ADI", "2027-SS", "S2508MR1212_SS27", 1, "JY6366", "CARDBOARD", "INRANGE"],
        ]),
        ("BOM Detail", [
            ["Cust", "Season_ID", "Style_ID", "Part_No", "Ad_Mat_ID", "LCO_Consump", "AD_Consump", "UOM", "Article ID"],
            ["ADI", "2027-SS", "S2508MR1212_SS27", 791, "  STSTRFQ_CAMB ", 0.00001, "NULL", "PC", "JY6366"],
        ]));
}

public sealed class StyleWorkbookReaderTests
{
    /// <summary>Reads with the automatic mapping, or the reasons it cannot be used (as the upload does).</summary>
    private static StyleWorkbookReadResult Read(byte[] xlsx, ImportColumnMapping? mapping = null)
    {
        var reader = new StyleWorkbookReader();
        var sheets = reader.Inspect(new MemoryStream(xlsx));
        mapping ??= StyleImportMapper.Auto(sheets);
        var problems = StyleImportMapper.Problems(mapping, sheets);
        return problems.Count > 0 ? new StyleWorkbookReadResult(null, problems) : reader.Read(new MemoryStream(xlsx), mapping);
    }

    private static IReadOnlyList<WorkbookSheetDto> Inspect(byte[] xlsx) => new StyleWorkbookReader().Inspect(new MemoryStream(xlsx));

    private static int Index(ImportTargetSheet sheet, string column) => Array.FindIndex(sheet.Columns, c => c.Name == column);

    private static ImportColumnMapping With(ImportColumnMapping mapping, string sheet, Func<ImportSheetMapping, ImportSheetMapping> change)
        => new(new Dictionary<string, ImportSheetMapping>(mapping.Sheets) { [sheet] = change(mapping.Sheets[sheet]) });

    private static ImportSheetMapping Column(ImportSheetMapping sheet, string column, string? heading)
        => sheet with { Columns = new Dictionary<string, string?>(sheet.Columns) { [column] = heading } };

    [Fact]
    public void Reads_the_three_sheets_in_staging_column_order()
    {
        var result = Read(TestWorkbook.Valid());
        Assert.Empty(result.Problems);
        var wb = result.Workbook!;

        var style = Assert.Single(wb.Styles);
        Assert.Equal(2, style.RowNo);
        Assert.Equal(["ADI", "2027-SS", "S2508MR1212_SS27"], style.Values[..3]);
        Assert.Equal("KKK60", style.Values[4]);    // ModelId
        Assert.Equal("90", style.Values[11]);      // LeadTime: numbers become text
        Assert.Null(style.Values[3]);              // Description: column absent

        var bom = Assert.Single(wb.Bom);
        Assert.Equal("STSTRFQ_CAMB", bom.Values[4]); // trimmed
        Assert.Equal("0.00001", bom.Values[11]);     // no exponent notation (SQL TRY_CONVERT would fail on 1E-05)
        Assert.Null(bom.Values[12]);                 // the workbook export writes DB nulls as the text "NULL"
        Assert.Equal("PC", bom.Values[13]);
    }

    [Fact]
    public void Unknown_sheet_names_fall_back_to_their_position()
    {
        var renamed = TestWorkbook.Create(
            ("Sheet1", [["Cust", "Season_ID", "Style_ID"], ["SKE", "2026-WI", "P224M053"]]),
            ("Sheet2", [["Cust", "Season ID", "Style ID", "Article ID"]]),
            ("Sheet3", [["Cust", "Season_ID", "Style_ID", "Ad_Mat_ID", "Article ID"]]));
        var result = Read(renamed);
        Assert.Empty(result.Problems);
        Assert.Equal("P224M053", result.Workbook!.Styles[0].Values[2]);
    }

    [Fact]
    public void Missing_sheets_and_headings_are_reported()
    {
        var result = Read(TestWorkbook.Create(
            ("Style Header", [["Cust", "Season_ID"]]),
            ("Article", [["Cust", "Season ID", "Style ID", "Article ID"]])));
        Assert.Null(result.Workbook);
        Assert.Contains(result.Problems, p => p.Contains("no StyleId column"));
        Assert.Contains(result.Problems, p => p.Contains("no \"BOM Detail\" sheet"));
    }

    [Fact]
    public void Values_longer_than_the_column_are_reported_with_their_row()
    {
        var result = Read(TestWorkbook.Create(
            ("Style Header", [["Cust", "Season_ID", "Style_ID"], ["ADI", "2027-SS", new string('X', 101)]]),
            ("Article", [["Cust", "Season ID", "Style ID", "Article ID"]]),
            ("BOM Detail", [["Cust", "Season_ID", "Style_ID", "Ad_Mat_ID", "Article ID"]])));
        Assert.Contains("Style Header row 2: StyleId is longer than 100 characters.", result.Problems);
    }

    [Fact]
    public void Inspect_lists_each_sheet_with_its_headings_and_samples()
    {
        var sheets = Inspect(TestWorkbook.GoExport());
        Assert.Equal(["Style_Parm", "Style_Articles", "Style_BOM"], sheets.Select(s => s.Name));
        Assert.Equal(["1005902"], sheets[2].Columns.Single(c => c.Heading == "Gen_Mat_ID").Samples);
        Assert.Empty(sheets[2].Columns.Single(c => c.Heading == "AD_Mat_ID").Samples);   // the text NULL is no value
    }

    [Fact]
    public void Automatic_mapping_finds_sheets_by_position_and_known_headings_only()
    {
        var mapping = StyleImportMapper.Auto(Inspect(TestWorkbook.GoExport()));
        Assert.Equal("Style_Parm", mapping.Sheets["Style"].Source);
        Assert.Equal("Style_BOM", mapping.Sheets["Bom"].Source);
        Assert.Equal("Cust_name", mapping.Sheets["Style"].Columns["CustName"]);
        Assert.Null(mapping.Sheets["Style"].Columns["ProdTypeId"]);              // "Prod_tyle_id" is not a known heading
        Assert.Equal("AD_Mat_ID", mapping.Sheets["Bom"].Columns["AdMatId"]);
        Assert.Equal("AD_COnsump_UOM", mapping.Sheets["Bom"].Columns["Uom"]);
        Assert.Equal("ASAD_Consump_Allowance", mapping.Sheets["Bom"].Columns["ConsumpAllowance"]);
    }

    [Fact]
    public void A_mapping_places_other_headings_in_the_right_columns()
    {
        var mapping = StyleImportMapper.Auto(Inspect(TestWorkbook.GoExport()));
        mapping = With(mapping, "Style", s => Column(s, "ProdTypeId", "Prod_tyle_id"));
        mapping = With(mapping, "Bom", s => Column(s, "AdMatId", "Gen_Mat_ID"));

        var result = Read(TestWorkbook.GoExport(), mapping);
        Assert.Empty(result.Problems);
        var style = Assert.Single(result.Workbook!.Styles);
        Assert.Equal("70001", style.Values[Index(StyleImportLayout.Style, "ProdTypeId")]);
        Assert.Equal("TJ Skechers Mens Export(TOMG)", style.Values[Index(StyleImportLayout.Style, "CustName")]);
        Assert.Equal("26FW", style.Values[Index(StyleImportLayout.Style, "SeasonId")]);
        var bom = Assert.Single(result.Workbook.Bom);
        Assert.Equal("1005902", bom.Values[Index(StyleImportLayout.Bom, "AdMatId")]);
        Assert.Equal("0.03", bom.Values[Index(StyleImportLayout.Bom, "ConsumpAllowance")]);
        Assert.Null(bom.Values[Index(StyleImportLayout.Bom, "ArticleId")]);    // blank: the line applies to all colorways
        Assert.Equal(TestWorkbook.GoArticle, Assert.Single(result.Workbook.Articles).Values[Index(StyleImportLayout.Article, "ArticleId")]);
    }

    [Fact]
    public void Mapping_problems_name_the_missing_sheet_heading_and_required_column()
    {
        var sheets = Inspect(TestWorkbook.GoExport());
        var mapping = StyleImportMapper.Auto(sheets);
        mapping = With(mapping, "Style", s => Column(s, "StyleId", null));
        mapping = With(mapping, "Article", s => s with { Source = "Nope" });
        mapping = With(mapping, "Bom", s => Column(s, "AdMatId", "Missing"));

        var problems = StyleImportMapper.Problems(mapping, sheets);
        Assert.Contains("The \"Style Header\" sheet has no StyleId column.", problems);
        Assert.Contains("The workbook has no sheet named \"Nope\" (for Article).", problems);
        Assert.Contains("The \"Style_BOM\" sheet has no \"Missing\" column (for BOM Detail AdMatId).", problems);
    }

    [Fact]
    public void A_saved_mapping_is_suggested_only_when_it_fits_the_workbook()
    {
        var sheets = Inspect(TestWorkbook.GoExport());
        var template = StyleImportMapper.Auto(Inspect(TestWorkbook.Valid()));
        List<ImportMappingDto> saved =
        [
            new(1, "Template", template, "x", DateTime.UtcNow),
            new(2, "GO export", StyleImportMapper.Auto(sheets), "x", DateTime.UtcNow.AddDays(-1)),
        ];
        Assert.Equal(2, StyleImportMapper.Pick(saved, sheets)!.MappingId);
        Assert.Null(StyleImportMapper.Pick(saved[..1], sheets));
    }
}

public sealed class StyleImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<(HttpClient Client, UserProfileDto User)> SignInAsync(params string[] roles)
    {
        var name = "imp" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, login.User);
    }

    private static MultipartFormDataContent File(byte[] content, string name = "Style_Library.xlsx")
        => new() { { new ByteArrayContent(content), "file", name } };

    [Fact]
    public async Task Imports_are_for_admins_only()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync("/api/v1/style-library/imports")).StatusCode);
        var (merchandiser, _) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.Forbidden, (await merchandiser.GetAsync("/api/v1/style-library/imports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await merchandiser.PostAsync("/api/v1/style-library/imports", File(TestWorkbook.Valid()))).StatusCode);
        var (admin, _) = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/style-library/imports")).StatusCode);
    }

    [Fact]
    public async Task Upload_stages_the_workbook_under_the_signed_in_admin()
    {
        var (admin, me) = await SignInAsync("Admin");
        var response = await admin.PostAsync("/api/v1/style-library/imports", File(TestWorkbook.Valid()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var staged = factory.StyleImports.Staged.Last();
        Assert.Equal(me.UserName, staged.UploadedBy);
        Assert.Equal("Style_Library.xlsx", staged.FileName);
        Assert.Single(staged.Workbook.Bom);
    }

    [Fact]
    public async Task Files_that_are_not_the_template_are_refused_with_reasons()
    {
        var (admin, _) = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/style-library/imports", File("a,b"u8.ToArray(), "styles.csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/style-library/imports", File("not a zip"u8.ToArray()))).StatusCode);

        var wrong = await admin.PostAsync("/api/v1/style-library/imports", File(TestWorkbook.Create(("Style Header", [["Cust"]]))));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var body = await wrong.Content.ReadAsStringAsync();
        Assert.Contains("does not match the Style Library template", body);
        Assert.Contains("no SeasonId column", body);
    }

    [Fact]
    public async Task Commit_takes_the_ticked_styles_and_the_user_from_the_token()
    {
        var (admin, me) = await SignInAsync("Admin");
        var ok = await admin.PostAsJsonAsync("/api/v1/style-library/imports/7/commit", new { overwriteStyleKeys = new[] { "ADI|2027-SS|S2508MR1212_SS27" } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var commit = factory.StyleImports.Commits.Last();
        Assert.Equal(7, commit.BatchId);
        Assert.Equal(["ADI|2027-SS|S2508MR1212_SS27"], commit.Keys);
        Assert.Equal(me.UserName, commit.CommittedBy);

        var twice = await admin.PostAsJsonAsync("/api/v1/style-library/imports/409/commit", new { overwriteStyleKeys = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Contains("already committed", await twice.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/style-library/imports/404")).StatusCode);
    }

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Inspect_suggests_a_mapping_and_upload_uses_the_one_sent()
    {
        var (admin, _) = await SignInAsync("Admin");
        var inspect = await admin.PostAsync("/api/v1/style-library/imports/inspect", File(TestWorkbook.Valid()));
        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        var layout = (await inspect.Content.ReadFromJsonAsync<WorkbookInspectionDto>())!;
        Assert.Equal(3, layout.Sheets.Count);
        Assert.Equal("Style Header", layout.Mapping.Sheets["Style"].Source);
        Assert.Contains(layout.Targets.Single(t => t.Key == "Bom").Columns, c => c.Name == "AdMatId" && c.Required);

        // Same file, the article code taken from another column.
        var article = layout.Mapping.Sheets["Article"];
        var mapping = new ImportColumnMapping(new Dictionary<string, ImportSheetMapping>(layout.Mapping.Sheets)
        {
            ["Article"] = article with { Columns = new Dictionary<string, string?>(article.Columns) { ["ArticleId"] = "Description" } },
        });
        var form = File(TestWorkbook.Valid());
        form.Add(new StringContent(System.Text.Json.JsonSerializer.Serialize(mapping, Web)), "mapping");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/v1/style-library/imports", form)).StatusCode);
        var staged = Assert.Single(factory.StyleImports.Staged.Last().Workbook.Articles);
        Assert.Equal("CARDBOARD", staged.Values[Array.FindIndex(StyleImportLayout.Article.Columns, c => c.Name == "ArticleId")]);

        var broken = File(TestWorkbook.Valid());
        broken.Add(new StringContent("{not json"), "mapping");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/style-library/imports", broken)).StatusCode);
    }

    [Fact]
    public async Task Mappings_are_saved_by_name()
    {
        var (admin, me) = await SignInAsync("Admin");
        var mapping = StyleImportMapper.Auto(new StyleWorkbookReader().Inspect(new MemoryStream(TestWorkbook.Valid())));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/style-library/imports/mappings", new { name = " ", mapping })).StatusCode);

        var saved = await admin.PutAsJsonAsync("/api/v1/style-library/imports/mappings", new { name = " GO export ", mapping });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = (await saved.Content.ReadFromJsonAsync<ImportMappingDto>())!;
        Assert.Equal("GO export", dto.Name);
        Assert.Equal(me.UserName, dto.UpdatedBy);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/style-library/imports/mappings/{dto.MappingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/style-library/imports/mappings/{dto.MappingId}")).StatusCode);
    }

    [Fact]
    public async Task Rollback_is_for_admins_and_lists_what_stops_it()
    {
        var (merchandiser, _) = await SignInAsync("Merchandiser");
        Assert.Equal(HttpStatusCode.Forbidden, (await merchandiser.PostAsync("/api/v1/style-library/imports/8/rollback", null)).StatusCode);

        var (admin, me) = await SignInAsync("Admin");
        var done = await admin.PostAsync("/api/v1/style-library/imports/8/rollback", null);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var batch = (await done.Content.ReadFromJsonAsync<StyleImportBatchDto>())!;
        Assert.Equal("RolledBack", batch.Status);
        Assert.Equal(me.UserName, batch.RolledBackBy);

        var blocked = await admin.PostAsync("/api/v1/style-library/imports/409/rollback", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("Nothing was changed", body);
        Assert.Contains("Edited after the import", body);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/style-library/imports/404/rollback", null)).StatusCode);
    }

    [Fact]
    public async Task A_workbook_is_kept_so_its_mapping_can_be_resumed_changed_and_deleted()
    {
        var (admin, _) = await SignInAsync("Admin");
        var inspect = await admin.PostAsync("/api/v1/style-library/imports/inspect", File(TestWorkbook.Valid()));
        var layout = (await inspect.Content.ReadFromJsonAsync<WorkbookInspectionDto>())!;
        var id = layout.BatchId!.Value;
        Assert.Equal("Style_Library.xlsx", layout.FileName);
        var kept = factory.StyleImports.Files[id];
        Assert.Equal("Mapping", kept.Status);
        Assert.True(System.IO.File.Exists(Path.Combine(factory.FilesRoot, "style-imports", kept.StoredFile!)));

        // Edit, leave, come back: the edited mapping is what the page reopens with.
        var article = layout.Mapping.Sheets["Article"];
        var edited = new ImportColumnMapping(new Dictionary<string, ImportSheetMapping>(layout.Mapping.Sheets)
        {
            ["Article"] = article with { Columns = new Dictionary<string, string?>(article.Columns) { ["ArticleId"] = "Description" } },
        });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/style-library/imports/{id}/mapping", edited)).StatusCode);
        var resumed = (await admin.GetFromJsonAsync<WorkbookInspectionDto>($"/api/v1/style-library/imports/{id}/inspect"))!;
        Assert.Equal("Description", resumed.Mapping.Sheets["Article"].Columns["ArticleId"]);
        Assert.Equal(3, resumed.Sheets.Count);

        // Stage it (no new upload), then map again.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/style-library/imports/{id}/stage", edited)).StatusCode);
        Assert.Equal("CARDBOARD", factory.StyleImports.Staged.Last().Workbook.Articles[0].Values[Array.FindIndex(StyleImportLayout.Article.Columns, c => c.Name == "ArticleId")]);
        var wrong = await admin.PostAsJsonAsync($"/api/v1/style-library/imports/{id}/stage", layout.Mapping with
        {
            Sheets = new Dictionary<string, ImportSheetMapping>(layout.Mapping.Sheets) { ["Bom"] = layout.Mapping.Sheets["Bom"] with { Source = "Nope" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        // Delete: the import and its workbook go.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/style-library/imports/{id}")).StatusCode);
        Assert.False(System.IO.File.Exists(Path.Combine(factory.FilesRoot, "style-imports", kept.StoredFile!)));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/style-library/imports/{id}/inspect")).StatusCode);
    }
}
