using System.Globalization;
using System.Text;
using ExcelDataReader;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// Reads the Style Library workbook (.xlsx) sheet by sheet, row by row.
/// Inspect returns each sheet's headings with a few sample values (for Settings > Import, Map columns). Read stages
/// the rows the mapping asks for: which workbook sheet feeds Style Header / Article / BOM Detail, and which heading
/// feeds each staging column (StyleImportMapper.Auto finds them by name, as before mapping existed).
/// Values become trimmed text (numbers without exponent notation), except Create_dt which stays a date.
/// </summary>
public sealed class StyleWorkbookReader : IStyleWorkbookReader
{
    /// <summary>Rows looked at for sample values, and samples kept per column.</summary>
    private const int SampleRows = 50, SamplesPerColumn = 3;

    /// <summary>
    /// The upload is inspected, then read: the caller owns (and disposes) the stream. Made per use: the configuration
    /// looks up code page 1252, which only exists once the static constructor has registered it.
    /// </summary>
    private static ExcelReaderConfiguration LeaveOpen => new() { LeaveOpen = true };

    // ExcelDataReader looks up code page 1252 when it starts, even for .xlsx; .NET only has it with this provider.
    static StyleWorkbookReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public IReadOnlyList<WorkbookSheetDto> Inspect(Stream xlsx)
    {
        var sheets = new List<WorkbookSheetDto>();
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(xlsx, LeaveOpen);
        do
        {
            var name = reader.Name ?? $"Sheet{sheets.Count + 1}";
            if (!reader.Read())
            {
                sheets.Add(new WorkbookSheetDto(name, []));
                continue;
            }
            var headings = Headings(reader);
            var samples = headings.Select(_ => new List<string>()).ToArray();
            for (var r = 0; r < SampleRows && reader.Read(); r++)
                for (var c = 0; c < headings.Count && c < reader.FieldCount; c++)
                    if (headings[c] is not null && samples[c].Count < SamplesPerColumn && AsText(reader.GetValue(c)) is { } text && !samples[c].Contains(text))
                        samples[c].Add(text.Length > 80 ? text[..80] + "…" : text);
            var columns = new List<WorkbookColumnDto>();
            for (var c = 0; c < headings.Count; c++)
                if (headings[c] is { } h && columns.All(x => !string.Equals(x.Heading, h, StringComparison.OrdinalIgnoreCase)))
                    columns.Add(new WorkbookColumnDto(h, samples[c]));
            sheets.Add(new WorkbookSheetDto(name, columns));
        } while (reader.NextResult());
        return sheets;
    }

    public StyleWorkbookReadResult Read(Stream xlsx, ImportColumnMapping mapping)
    {
        var problems = new List<string>();
        var targets = StyleImportLayout.Sheets;
        var rows = targets.Select(_ => new List<StagedRow>()).ToArray();
        var found = new bool[targets.Length];

        using var reader = ExcelReaderFactory.CreateOpenXmlReader(xlsx, LeaveOpen);
        do
        {
            // The import sheets this workbook sheet feeds (usually one).
            var feeds = Enumerable.Range(0, targets.Length)
                .Where(i => !found[i] && mapping.Sheets.GetValueOrDefault(targets[i].Key)?.Source is { } source
                            && string.Equals(source.Trim(), reader.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (feeds.Count == 0) continue;
            feeds.ForEach(i => found[i] = true);
            ReadSheet(reader, feeds.Select(i => (targets[i], mapping.Sheets[targets[i].Key], rows[i])).ToList(), problems);
            if (problems.Count >= StyleImportRules.MaxReadProblems) break;
        } while (reader.NextResult());

        for (var i = 0; i < targets.Length; i++)
            if (!found[i] && problems.Count < StyleImportRules.MaxReadProblems)
                problems.Add(mapping.Sheets.GetValueOrDefault(targets[i].Key)?.Source is { } source
                    ? $"The workbook has no sheet named \"{source}\" (for {targets[i].Label})."
                    : $"The workbook has no \"{targets[i].Label}\" sheet.");

        return problems.Count > 0
            ? new StyleWorkbookReadResult(null, problems.Take(StyleImportRules.MaxReadProblems).ToList())
            : new StyleWorkbookReadResult(new StyleWorkbook(rows[0], rows[1], rows[2]), []);
    }

    private static void ReadSheet(IExcelDataReader reader, List<(ImportTargetSheet Sheet, ImportSheetMapping Map, List<StagedRow> Rows)> feeds, List<string> problems)
    {
        if (!reader.Read())
        {
            foreach (var (sheet, _, _) in feeds) problems.Add($"The \"{sheet.Label}\" sheet is empty: the first row must hold the headings.");
            return;
        }

        // Per import sheet: staging column -> Excel column index (-1 = not imported).
        var headings = Headings(reader);
        var maps = new List<int[]>();
        foreach (var (sheet, map, _) in feeds)
        {
            var indexes = sheet.Columns.Select(c => map.Columns.GetValueOrDefault(c.Name) is { Length: > 0 } h ? StyleImportMapper.FindHeading(headings, h) : -1).ToArray();
            for (var k = 0; k < sheet.Columns.Length; k++)
                if (indexes[k] < 0 && map.Columns.GetValueOrDefault(sheet.Columns[k].Name) is { Length: > 0 } h)
                    problems.Add($"The \"{reader.Name}\" sheet has no \"{h}\" column (for {sheet.Label} {sheet.Columns[k].Name}).");
                else if (indexes[k] < 0 && sheet.Columns[k].Required)
                    problems.Add($"The \"{sheet.Label}\" sheet has no {sheet.Columns[k].Name} column.");
            maps.Add(indexes);
        }
        if (problems.Count > 0) return;

        var rowNo = 1;
        while (reader.Read())
        {
            rowNo++;
            for (var f = 0; f < feeds.Count; f++)
            {
                var (sheet, _, rows) = feeds[f];
                var values = new object?[sheet.Columns.Length];
                var any = false;
                for (var k = 0; k < sheet.Columns.Length; k++)
                {
                    var c = maps[f][k];
                    if (c < 0 || c >= reader.FieldCount) continue;
                    var column = sheet.Columns[k];
                    var value = column.IsDate ? AsDate(reader.GetValue(c)) : AsText(reader.GetValue(c));
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
    }

    /// <summary>The current row as headings: trimmed text, null for an empty cell.</summary>
    private static List<string?> Headings(IExcelDataReader reader)
    {
        var headings = new List<string?>();
        for (var c = 0; c < reader.FieldCount; c++)
            headings.Add(Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } h ? h : null);
        return headings;
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

}
