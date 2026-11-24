using System.Globalization;
using System.Text;
using ExcelDataReader;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Costing;
using LT.ODM.Infrastructure.StyleLibrary;

namespace LT.ODM.Infrastructure.Costing;

/// <summary>
/// Reads the ERP costing export: the first sheet, one row per costing version, columns found by heading. Money is rounded to 5 decimals,
/// as the ERP calculates it. A row the ERP's net balance does not add up on is kept as sent and pointed out.
/// </summary>
public sealed class CostingWorkbookReader : ICostingWorkbookReader
{
    private const int MaxRows = 200_000;

    /// <summary>A heading of the export (and the names it has had) and the cost category it feeds.</summary>
    private static readonly (string[] Headings, string Code)[] Categories =
    [
        (["MAIN FAB"], "MAINFAB"), (["SUB FAB"], "SUBFAB"), (["TRIMS"], "TRIMS"), (["LC CHARGE"], "LCCHARGE"), (["MAT FRT", "MAR FRT"], "MATFRT"),
        (["LTRIMS"], "LTRIMS"), (["CM"], "CM"), (["SUBCON", "SUBSON"], "SUBCON"), (["GARM WASH"], "GARMWSH"), (["EMBRO"], "EMBRO"),
        (["VA COST"], "VACOST"), (["PRINT COST"], "PRINT"), (["OTHER COST"], "OTHCOST"), (["CLAIM"], "CLAIM"),
    ];

    private static readonly string[] Required = ["CUST", "SEASON", "STYLE ID", "IO NO", "COST VER#", "COST TYPE", "CURRENCY", "FOB"];

    // ExcelDataReader looks up code page 1252 when it starts, even for .xlsx; .NET only has it with this provider.
    static CostingWorkbookReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public CostingWorkbook Read(Stream xlsx)
    {
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(xlsx, new ExcelReaderConfiguration { LeaveOpen = true });
        if (!reader.Read()) return new CostingWorkbook([], ["The first sheet is empty."], []);

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 0; c < reader.FieldCount; c++)
            if (StyleWorkbookReader.AsText(reader.GetValue(c)) is { } heading)
                columns.TryAdd(string.Join(' ', heading.Split(' ', StringSplitOptions.RemoveEmptyEntries)), c);
        var missing = Required.Where(h => !columns.ContainsKey(h)).ToList();
        if (missing.Count > 0) return new CostingWorkbook([], [$"This is not the costing export: no column {string.Join(", ", missing)}."], []);

        var rows = new Dictionary<int, CostingImportRow>();
        var issues = new List<string>();
        var rowNo = 1;
        while (reader.Read())
        {
            rowNo++;
            if (rowNo > MaxRows) return new CostingWorkbook([], [$"The sheet has more than {MaxRows:N0} rows. Split the file."], []);
            object? Cell(params string[] names)
            {
                foreach (var name in names)
                    if (columns.TryGetValue(name, out var c) && c < reader.FieldCount) return reader.GetValue(c);
                return null;
            }

            var styleNo = Text(Cell("STYLE ID"));
            var ioNo = Text(Cell("IO NO"));
            var version = Number(Cell("COST VER#"));
            if (styleNo is null && ioNo is null && version is null) continue;   // blank row

            var cust = Text(Cell("CUST"));
            var season = Text(Cell("SEASON"));
            var type = Text(Cell("COST TYPE"))?.ToUpperInvariant();
            var currency = Text(Cell("CURRENCY"))?.ToUpperInvariant();
            var fob = Number(Cell("FOB"));
            if (cust is null || season is null || styleNo is null || ioNo is null || version is null || fob is null)
            {
                issues.Add($"Row {rowNo}: left out, customer, season, style, IO, version or FOB is missing.");
                continue;
            }
            if (type is not ("PCOSTING" or "FCOSTING"))
            {
                issues.Add($"Row {rowNo}: left out, cost type \"{type}\" is not PCOSTING or FCOSTING.");
                continue;
            }
            if (currency is not { Length: 3 })
            {
                issues.Add($"Row {rowNo}: left out, currency \"{currency}\" is not a 3-letter code.");
                continue;
            }
            if (ioNo.Length > 30 || styleNo.Length > 100 || cust.Length > 32 || season.Length > 32 || version != decimal.Truncate(version.Value) || version is <= 0 or > int.MaxValue)
            {
                issues.Add($"Row {rowNo}: left out, the IO, style, customer, season or version does not fit.");
                continue;
            }

            var lines = new List<CostLine>();
            foreach (var (headings, code) in Categories)
                if (Number(Cell(headings)) is { } amount) lines.Add(new CostLine(code, amount));

            var net = Number(Cell("NET BAT"));
            if (net is { } n && Math.Abs(fob.Value - lines.Sum(l => l.Amount) - n) > 0.0001m)
                issues.Add($"Row {rowNo} (IO {ioNo}): net balance {n} is not FOB minus the costs; kept as sent.");

            var qty = Number(Cell("ORDER QTY"));
            var row = new CostingImportRow(cust, season, styleNo, ioNo, Cut(Text(Cell("PLANT CODE")), 20)?.ToUpperInvariant(), Cut(Text(Cell("PLANT NAME")), 120),
                qty is { } q && Math.Abs(q) <= int.MaxValue ? (int)q : null, Day(Cell("LCO DATE", "LCO DT")), Day(Cell("PLAN DELDT")), Number(Cell("MERCH SMV")), Number(Cell("TC SMV")),
                (int)version.Value, type, Cut(Text(Cell("STATUS")), 30), currency, When(Cell("COST DT")), fob.Value, net, Number(Cell("PERC TO FOB")), lines);
            if (!rows.TryAdd(row.CostingVersion, row))
            {
                rows[row.CostingVersion] = row;
                issues.Add($"Row {rowNo}: costing version {row.CostingVersion} is in the file twice; the last row is used.");
            }
        }
        return new CostingWorkbook(rows.Values.ToList(), rows.Count == 0 ? ["The sheet has no costing rows."] : [], issues);
    }

    private static string? Text(object? value) => StyleWorkbookReader.AsText(value);

    private static string? Cut(string? text, int length) => text is not null && text.Length > length ? text[..length] : text;

    private static decimal? Number(object? value) => Text(value) is { } text && decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number)
        ? Math.Round(number, 5, MidpointRounding.AwayFromZero)
        : null;

    private static DateTime? When(object? value) => value switch
    {
        DateTime dt => dt,
        _ => Text(value) is { } text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null,
    };

    private static DateOnly? Day(object? value) => When(value) is { } dt ? DateOnly.FromDateTime(dt) : null;
}
