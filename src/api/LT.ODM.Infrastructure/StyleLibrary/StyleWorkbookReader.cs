using System.Globalization;
using System.Text;
using ExcelDataReader;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// Reads the Style Library workbook (.xlsx) sheet by sheet, row by row.
/// Sheets are found by name (Style Header / Article / BOM Detail, a few spellings), otherwise by position 1-3.
/// Headings are matched ignoring case, spaces, underscores and dashes ("Season_ID" = "Season ID"); extra columns are ignored.
/// Values become trimmed text (numbers without exponent notation), except Create_dt which stays a date.
/// </summary>
public sealed class StyleWorkbookReader : IStyleWorkbookReader
{
    // ExcelDataReader looks up code page 1252 when it starts, even for .xlsx; .NET only has it with this provider.
    static StyleWorkbookReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>One staging column: its accepted headings (normalised), size, and whether it must be present.</summary>
    internal sealed record Column(string Name, string[] Headings, int MaxLength, bool Required = false, bool IsDate = false);

    internal sealed record Sheet(string Label, string[] Names, Column[] Columns);

    // Column order = staging table column order after BatchId, RowNo (see db/tables/style.tables.sql).
    internal static readonly Sheet StyleSheet = new("Style Header", ["styleheader", "style", "styles"],
    [
        new("Cust", ["cust", "customer", "customerid"], 32, Required: true),
        new("SeasonId", ["seasonid", "season"], 16, Required: true),
        new("StyleId", ["styleid", "style", "styleno"], 40, Required: true),
        new("Description", ["description", "styledesc"], 400),
        new("ModelId", ["modelid", "model"], 60),
        new("ModelName", ["modelname"], 100),
        new("WeaveTypeId", ["weavetypeid", "weavetype"], 8),
        new("WeaveTypeDesc", ["weavetypedesc"], 50),
        new("ProdTypeId", ["prodtypeid", "producttypeid", "producttype"], 40),
        new("ProdTypeDesc", ["prodtypedesc", "producttypedesc"], 100),
        new("Gender", ["gender"], 16),
        new("LeadTime", ["garmentleadtime", "leadtime"], 16),
        new("BuTeam", ["buteam", "businessunit", "bu"], 16),
        new("BuDesc", ["budesc", "businessunitdesc"], 100),
        new("CreateDt", ["createdt", "createdate", "created"], 0, IsDate: true),
        new("SketchUrl", ["sketch", "sketchurl"], 500),
        new("ImageUrl", ["image", "imageurl", "photo"], 500),
    ]);

    internal static readonly Sheet ArticleSheet = new("Article", ["article", "articles", "colorway", "colorways"],
    [
        new("Cust", ["cust", "customer", "customerid"], 32, Required: true),
        new("SeasonId", ["seasonid", "season"], 16, Required: true),
        new("StyleId", ["styleid", "style", "styleno"], 40, Required: true),
        new("SeqNo", ["seqno", "seq", "sortorder"], 8),
        new("ArticleId", ["articleid", "article", "colorwaycode", "colorway"], 20, Required: true),
        new("Description", ["description", "articledesc", "colorwayname"], 150),
        new("Status", ["status"], 16),
        new("ImageUrl", ["image", "imageurl", "photo"], 500),
    ]);

    internal static readonly Sheet BomSheet = new("BOM Detail", ["bomdetail", "bom", "bomdetails"],
    [
        new("Cust", ["cust", "customer", "customerid"], 32, Required: true),
        new("SeasonId", ["seasonid", "season"], 16, Required: true),
        new("StyleId", ["styleid", "style", "styleno"], 40, Required: true),
        new("PartNo", ["partno", "part"], 16),
        new("AdMatId", ["admatid", "materialid", "materialcode", "matid"], 64, Required: true),
        new("MaterialDesc", ["materialdesc", "materialdescription"], 4000),
        new("MatTypeId", ["mattypeid", "materialtypeid", "mattype"], 16),
        new("MatTypeDesc", ["mattypedesc", "materialtypedesc"], 100),
        new("ContentClass", ["contentclass"], 8),
        new("AdSupplierId", ["adsupplierid", "nominatedsupplierid"], 32),
        new("AdSupplierName", ["adsuppliername", "nominatedsuppliername"], 100),
        new("LcoConsump", ["lcoconsump", "lcoconsumption"], 32),
        new("AdConsump", ["adconsump", "adconsumption", "brandconsumption"], 32),
        new("Uom", ["uom"], 8),
        new("SapSupplierId", ["sapsupplierid", "supplierid"], 32),
        new("SapSupplierName", ["sapsuppliername", "suppliername"], 150),
        new("ArticleSeq", ["articleseq"], 16),
        new("ArticleId", ["articleid", "article", "colorwaycode", "colorway"], 20, Required: true),
        new("ArticleDesc", ["articledesc", "colorwayname"], 150),
        new("MatColorId", ["matcolorid", "materialcolorid", "materialcolorcode"], 80),
        new("MatColorDesc", ["matcolordesc", "materialcolordesc", "materialcolordescription"], 150),
        new("ImageUrl", ["image", "imageurl", "photo"], 500),
    ]);

    private static readonly Sheet[] Sheets = [StyleSheet, ArticleSheet, BomSheet];

    public StyleWorkbookReadResult Read(Stream xlsx)
    {
        var problems = new List<string>();
        var rows = new List<StagedRow>[] { [], [], [] };
        var found = new bool[3];

        using var reader = ExcelReaderFactory.CreateOpenXmlReader(xlsx);
        var position = 0;
        do
        {
            var index = SheetIndex(reader.Name, position, found);
            position++;
            if (index < 0) continue;
            found[index] = true;
            ReadSheet(reader, Sheets[index], rows[index], problems);
            if (problems.Count >= StyleImportRules.MaxReadProblems) break;
        } while (reader.NextResult());

        for (var i = 0; i < Sheets.Length; i++)
            if (!found[i]) problems.Add($"The workbook has no \"{Sheets[i].Label}\" sheet.");

        return problems.Count > 0
            ? new StyleWorkbookReadResult(null, problems.Take(StyleImportRules.MaxReadProblems).ToList())
            : new StyleWorkbookReadResult(new StyleWorkbook(rows[0], rows[1], rows[2]), []);
    }

    /// <summary>By name first; a sheet with an unknown name counts as the next unused one of the first three.</summary>
    private static int SheetIndex(string? name, int position, bool[] found)
    {
        var key = Normalize(name);
        for (var i = 0; i < Sheets.Length; i++)
            if (!found[i] && Sheets[i].Names.Contains(key)) return i;
        return position < Sheets.Length && !found[position] && !Sheets.Any(s => s.Names.Contains(key)) ? position : -1;
    }

    private static void ReadSheet(IExcelDataReader reader, Sheet sheet, List<StagedRow> rows, List<string> problems)
    {
        if (!reader.Read())
        {
            problems.Add($"The \"{sheet.Label}\" sheet is empty: the first row must hold the headings.");
            return;
        }

        // Heading row -> staging column -> Excel column index.
        var map = new int[sheet.Columns.Length];
        Array.Fill(map, -1);
        for (var c = 0; c < reader.FieldCount; c++)
        {
            var heading = Normalize(Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture));
            if (heading.Length == 0) continue;
            for (var k = 0; k < sheet.Columns.Length; k++)
                if (map[k] < 0 && sheet.Columns[k].Headings.Contains(heading)) { map[k] = c; break; }
        }
        foreach (var missing in sheet.Columns.Where((col, k) => col.Required && map[k] < 0))
            problems.Add($"The \"{sheet.Label}\" sheet has no {missing.Name} column.");
        if (problems.Count > 0) return;

        var rowNo = 1;
        while (reader.Read())
        {
            rowNo++;
            var values = new object?[sheet.Columns.Length];
            var any = false;
            for (var k = 0; k < sheet.Columns.Length; k++)
            {
                if (map[k] < 0 || map[k] >= reader.FieldCount) continue;
                var column = sheet.Columns[k];
                var value = column.IsDate ? AsDate(reader.GetValue(map[k])) : AsText(reader.GetValue(map[k]));
                if (value is string text && text.Length > column.MaxLength)
                {
                    problems.Add($"{sheet.Label} row {rowNo}: {column.Name} is longer than {column.MaxLength} characters.");
                    if (problems.Count >= StyleImportRules.MaxReadProblems) return;
                    value = text[..column.MaxLength];
                }
                values[k] = value;
                any |= value is not null;
            }
            if (!any) continue;   // blank row
            if (rows.Count >= StyleImportRules.MaxRowsPerSheet)
            {
                problems.Add($"The \"{sheet.Label}\" sheet has more than {StyleImportRules.MaxRowsPerSheet:N0} rows. Split the file.");
                return;
            }
            rows.Add(new StagedRow(rowNo, values));
        }
    }

    /// <summary>
    /// Trimmed text; numbers as plain decimals (no "1E-05"); empty or the text NULL (a database null written out by the
    /// source system's export) -> null.
    /// </summary>
    internal static string? AsText(object? value)
    {
        var text = value switch
        {
            null or DBNull => null,
            double d when double.IsFinite(d) && Math.Abs(d) < 7.9e27 => ((decimal)d).ToString(CultureInfo.InvariantCulture),
            DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool b => b ? "TRUE" : "FALSE",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) || text.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : text;
    }

    /// <summary>Excel dates arrive as DateTime; text like "2026-06-02 10:21:34.977" is parsed; anything else -> null.</summary>
    internal static object? AsDate(object? value) => value switch
    {
        DateTime dt => dt,
        double d when d is > 0 and < 2958466 => DateTime.FromOADate(d),
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt) => dt,
        _ => null,
    };

    internal static string Normalize(string? text)
        => new((text ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
