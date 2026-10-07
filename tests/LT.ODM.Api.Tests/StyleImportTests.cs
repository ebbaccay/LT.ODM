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

    public Task<StyleImportBatchDto> StageAsync(string fileName, StyleWorkbook workbook, string uploadedBy, CancellationToken ct = default)
    {
        Staged.Add((fileName, workbook, uploadedBy));
        return Task.FromResult(Batch(Staged.Count));
    }

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

    public Task<StyleImportBatchDto> MapCodeAsync(int batchId, MapImportCodeRequest request, CancellationToken ct = default)
    {
        if (request.Code == "nonsense") throw new StyleImportException(400, "Choose a code that is already on the list.");
        Maps.Add((batchId, request));
        return Task.FromResult(Batch(batchId));
    }
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
    private static StyleWorkbookReadResult Read(byte[] xlsx) => new StyleWorkbookReader().Read(new MemoryStream(xlsx));

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
            ("Style Header", [["Cust", "Season_ID", "Style_ID"], ["ADI", "2027-SS", new string('X', 41)]]),
            ("Article", [["Cust", "Season ID", "Style ID", "Article ID"]]),
            ("BOM Detail", [["Cust", "Season_ID", "Style_ID", "Ad_Mat_ID", "Article ID"]])));
        Assert.Contains("Style Header row 2: StyleId is longer than 40 characters.", result.Problems);
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
}
