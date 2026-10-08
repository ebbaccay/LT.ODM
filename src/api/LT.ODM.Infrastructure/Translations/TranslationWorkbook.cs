using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ExcelDataReader;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Translations;

namespace LT.ODM.Infrastructure.Translations;

/// <summary>
/// The translations workbook: sheet "Translations" with Key, one column per language headed "English (en)", and an
/// information column "Corrected in LT ODM"; sheet "How to use" for whoever edits it.
/// Written by hand (a .xlsx is a zip of XML parts) so no extra package is needed; read with ExcelDataReader.
/// </summary>
public sealed partial class TranslationWorkbook : ITranslationWorkbook
{
    // ExcelDataReader looks up code page 1252 when it starts, even for .xlsx; .NET only has it with this provider.
    static TranslationWorkbook() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private const string SheetName = "Translations";
    private const string KeyHeading = "Key";
    private const string CorrectedHeading = "Corrected in LT ODM";

    /// <summary>Style ids in styles.xml: 0 normal, 1 heading, 2 wrapped text, 3 wrapped text on yellow (corrected), 4 key.</summary>
    private const int Heading = 1, Wrapped = 2, Corrected = 3, KeyStyle = 4;

    [GeneratedRegex(@"\(([A-Za-z0-9-]+)\)\s*$")]
    private static partial Regex CodeInHeading();

    private static readonly string[] HowToUse =
    [
        "LT ODM translations",
        "",
        "1. Change the texts in the language columns of the Translations sheet. Yellow cells were already corrected in LT ODM.",
        "2. Keep the Key column and the column headings as they are. Rows can be sorted or filtered; extra columns are ignored.",
        "3. Keep placeholders such as {{name}} or {{count}} exactly as they are: the app fills them in.",
        "4. Clear a cell (or type the original text back) to go back to the text that came with the release.",
        "5. In LT ODM open Settings > Translations > Upload, choose this file, check the list of changes and click Apply.",
        "",
        "Only the cells that differ from the released texts are kept as corrections. Keys added in a later release show",
        "their released text until someone corrects them.",
    ];

    // ----- write -----

    public byte[] Write(TranslationSheet sheet)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Part(zip, "[Content_Types].xml", ContentTypes);
            Part(zip, "_rels/.rels", RootRels);
            Part(zip, "xl/workbook.xml", w => Workbook(w, sheet.Rows.Count + 1, sheet.Languages.Count + 2));
            Part(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            Part(zip, "xl/styles.xml", Styles);
            Part(zip, "xl/worksheets/sheet1.xml", w => TranslationsSheet(w, sheet));
            Part(zip, "xl/worksheets/sheet2.xml", HowToUseSheet);
        }
        return buffer.ToArray();
    }

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static void Part(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var w = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        w.WriteStartDocument(standalone: true);
        write(w);
        w.WriteEndDocument();
    }

    private static void ContentTypes(XmlWriter w)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        w.WriteStartElement("Types", ns);
        Default("rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default("xml", "application/xml");
        Override("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        Override("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        Override("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        w.WriteEndElement();

        void Default(string ext, string type)
        {
            w.WriteStartElement("Default", ns);
            w.WriteAttributeString("Extension", ext);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
        void Override(string part, string type)
        {
            w.WriteStartElement("Override", ns);
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
    }

    private static void RootRels(XmlWriter w)
        => Relationships(w, ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml"));

    private static void WorkbookRels(XmlWriter w) => Relationships(w,
        ("rId1", $"{Rel}/worksheet", "worksheets/sheet1.xml"),
        ("rId2", $"{Rel}/worksheet", "worksheets/sheet2.xml"),
        ("rId3", $"{Rel}/styles", "styles.xml"));

    private static void Relationships(XmlWriter w, params (string Id, string Type, string Target)[] rels)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        w.WriteStartElement("Relationships", ns);
        foreach (var (id, type, target) in rels)
        {
            w.WriteStartElement("Relationship", ns);
            w.WriteAttributeString("Id", id);
            w.WriteAttributeString("Type", type);
            w.WriteAttributeString("Target", target);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void Workbook(XmlWriter w, int lastRow, int columns)
    {
        w.WriteStartElement("workbook", Main);
        w.WriteAttributeString("xmlns", "r", null, Rel);
        w.WriteStartElement("sheets", Main);
        Sheet(SheetName, 1);
        Sheet("How to use", 2);
        w.WriteEndElement();
        // The filter on the heading row.
        w.WriteStartElement("definedNames", Main);
        w.WriteStartElement("definedName", Main);
        w.WriteAttributeString("name", "_xlnm._FilterDatabase");
        w.WriteAttributeString("localSheetId", "0");
        w.WriteAttributeString("hidden", "1");
        w.WriteString($"'{SheetName}'!$A$1:${ColumnName(columns - 1)}${lastRow}");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        void Sheet(string name, int id)
        {
            w.WriteStartElement("sheet", Main);
            w.WriteAttributeString("name", name);
            w.WriteAttributeString("sheetId", id.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("id", Rel, $"rId{id}");
            w.WriteEndElement();
        }
    }

    private static void Styles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", Main);

        w.WriteStartElement("fonts", Main);
        w.WriteAttributeString("count", "3");
        Font(bold: false, mono: false);
        Font(bold: true, mono: false);
        Font(bold: false, mono: true);
        w.WriteEndElement();

        w.WriteStartElement("fills", Main);
        w.WriteAttributeString("count", "4");
        Fill("none", null);
        Fill("gray125", null);
        Fill("solid", "FFE5E7EB");   // heading
        Fill("solid", "FFFEF3C7");   // corrected
        w.WriteEndElement();

        w.WriteStartElement("borders", Main);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("border", Main);
        foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" }) w.WriteElementString(side, Main, "");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cellStyleXfs", Main);
        w.WriteAttributeString("count", "1");
        Xf(0, 0, wrap: false, inCellXfs: false);
        w.WriteEndElement();

        w.WriteStartElement("cellXfs", Main);
        w.WriteAttributeString("count", "5");
        Xf(0, 0, wrap: false);          // 0 normal
        Xf(1, 2, wrap: false);          // 1 heading
        Xf(0, 0, wrap: true);           // 2 wrapped
        Xf(0, 3, wrap: true);           // 3 corrected
        Xf(2, 0, wrap: false);          // 4 key
        w.WriteEndElement();

        w.WriteStartElement("cellStyles", Main);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle", Main);
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement();

        void Font(bool bold, bool mono)
        {
            w.WriteStartElement("font", Main);
            if (bold) w.WriteElementString("b", Main, "");
            Val("sz", mono ? "10" : "11");
            Val("name", mono ? "Consolas" : "Calibri");
            w.WriteEndElement();
        }
        void Fill(string pattern, string? rgb)
        {
            w.WriteStartElement("fill", Main);
            w.WriteStartElement("patternFill", Main);
            w.WriteAttributeString("patternType", pattern);
            if (rgb is not null)
            {
                w.WriteStartElement("fgColor", Main);
                w.WriteAttributeString("rgb", rgb);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }
        void Xf(int font, int fill, bool wrap, bool inCellXfs = true)
        {
            w.WriteStartElement("xf", Main);
            w.WriteAttributeString("numFmtId", "0");
            w.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", fill.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("borderId", "0");
            if (inCellXfs)
            {
                w.WriteAttributeString("xfId", "0");
                if (font != 0) w.WriteAttributeString("applyFont", "1");
                if (fill != 0) w.WriteAttributeString("applyFill", "1");
                w.WriteAttributeString("applyAlignment", "1");
                w.WriteStartElement("alignment", Main);
                w.WriteAttributeString("vertical", "top");
                if (wrap) w.WriteAttributeString("wrapText", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        void Val(string name, string value)
        {
            w.WriteStartElement(name, Main);
            w.WriteAttributeString("val", value);
            w.WriteEndElement();
        }
    }

    private static void TranslationsSheet(XmlWriter w, TranslationSheet sheet)
    {
        var columns = sheet.Languages.Count + 2;
        w.WriteStartElement("worksheet", Main);

        // Heading row stays visible while scrolling.
        w.WriteStartElement("sheetViews", Main);
        w.WriteStartElement("sheetView", Main);
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteAttributeString("tabSelected", "1");
        w.WriteStartElement("pane", Main);
        w.WriteAttributeString("ySplit", "1");
        w.WriteAttributeString("topLeftCell", "A2");
        w.WriteAttributeString("activePane", "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cols", Main);
        Width(1, 48);
        for (var i = 0; i < sheet.Languages.Count; i++) Width(i + 2, 60);
        Width(columns, 22);
        w.WriteEndElement();

        w.WriteStartElement("sheetData", Main);
        Row(w, 1, [(KeyHeading, Heading), .. sheet.Languages.Select(l => ($"{l.Label} ({l.Code})", Heading)), (CorrectedHeading, Heading)]);
        var rowNo = 1;
        foreach (var row in sheet.Rows)
        {
            rowNo++;
            Row(w, rowNo,
            [
                (row.Key, KeyStyle),
                .. sheet.Languages.Select(l => (row.Values.GetValueOrDefault(l.Code), row.Customised.Contains(l.Code) ? Corrected : Wrapped)),
                (row.Customised.Count == 0 ? null : string.Join(", ", row.Customised), 0),
            ]);
        }
        w.WriteEndElement();

        w.WriteStartElement("autoFilter", Main);
        w.WriteAttributeString("ref", $"A1:{ColumnName(columns - 1)}{rowNo}");
        w.WriteEndElement();

        w.WriteEndElement();

        void Width(int column, double width)
        {
            w.WriteStartElement("col", Main);
            w.WriteAttributeString("min", column.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("max", column.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("customWidth", "1");
            w.WriteEndElement();
        }
    }

    private static void HowToUseSheet(XmlWriter w)
    {
        w.WriteStartElement("worksheet", Main);
        w.WriteStartElement("cols", Main);
        w.WriteStartElement("col", Main);
        w.WriteAttributeString("min", "1");
        w.WriteAttributeString("max", "1");
        w.WriteAttributeString("width", "120");
        w.WriteAttributeString("customWidth", "1");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("sheetData", Main);
        for (var i = 0; i < HowToUse.Length; i++) Row(w, i + 1, [(HowToUse[i], i == 0 ? Heading : 0)]);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    /// <summary>A row of text cells (inline strings, so no shared-strings part is needed). Empty cells are left out.</summary>
    private static void Row(XmlWriter w, int rowNo, (string? Text, int Style)[] cells)
    {
        w.WriteStartElement("row", Main);
        w.WriteAttributeString("r", rowNo.ToString(CultureInfo.InvariantCulture));
        for (var c = 0; c < cells.Length; c++)
        {
            var (text, style) = cells[c];
            w.WriteStartElement("c", Main);
            w.WriteAttributeString("r", ColumnName(c) + rowNo.ToString(CultureInfo.InvariantCulture));
            if (style != 0) w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(text))
            {
                w.WriteAttributeString("t", "inlineStr");
                w.WriteStartElement("is", Main);
                w.WriteStartElement("t", Main);
                w.WriteAttributeString("xml", "space", null, "preserve");
                w.WriteString(XmlSafe(text));
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>0 -> A, 25 -> Z, 26 -> AA.</summary>
    private static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static string XmlSafe(string text)
        => text.All(XmlConvert.IsXmlChar) ? text : new string(text.Where(XmlConvert.IsXmlChar).ToArray());

    // ----- read -----

    public TranslationSheetReadResult Read(Stream xlsx, IReadOnlyList<string> languages)
    {
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(xlsx);
        // The sheet named Translations, otherwise the first one.
        var found = false;
        do
        {
            if (string.Equals(reader.Name?.Trim(), SheetName, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
        } while (reader.NextResult());
        if (!found)
        {
            reader.Reset();
        }
        return ReadSheet(reader, languages);
    }

    private static TranslationSheetReadResult ReadSheet(IExcelDataReader reader, IReadOnlyList<string> languages)
    {
        if (!reader.Read())
            return Fail("The sheet is empty: the first row must hold the headings (Key, then one column per language).");

        var keyColumn = -1;
        var columns = new List<(int Index, string Lang)>();
        for (var c = 0; c < reader.FieldCount; c++)
        {
            var heading = Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (heading.Length == 0) continue;
            if (keyColumn < 0 && string.Equals(heading, KeyHeading, StringComparison.OrdinalIgnoreCase)) { keyColumn = c; continue; }
            var code = CodeInHeading().Match(heading) is { Success: true } m ? m.Groups[1].Value : heading;
            var lang = languages.FirstOrDefault(l => string.Equals(l, code, StringComparison.OrdinalIgnoreCase));
            if (lang is not null && columns.All(x => x.Lang != lang)) columns.Add((c, lang));
        }
        if (keyColumn < 0)
            return Fail("The sheet has no Key column. Export the translations again and edit that file.");
        if (columns.Count == 0)
            return Fail($"The sheet has no language column. The headings must end with the language code, e.g. {string.Join(", ", languages.Select(l => $"\"{TranslationRules.Label(l)} ({l})\""))}.");

        var rows = new List<TranslationSheetRow>();
        var problems = new List<string>();
        var rowNo = 1;
        while (reader.Read())
        {
            rowNo++;
            var key = Text(reader, keyColumn)?.Trim();
            if (string.IsNullOrEmpty(key)) continue;
            if (rows.Count >= TranslationRules.MaxRows)
            {
                problems.Add($"The sheet has more than {TranslationRules.MaxRows:N0} rows; the rest were not read.");
                break;
            }
            rows.Add(new TranslationSheetRow(rowNo, key, columns.ToDictionary(x => x.Lang, x => Text(reader, x.Index)), []));
        }
        return new TranslationSheetReadResult(columns.Select(x => x.Lang).ToList(), rows, problems);
    }

    private static TranslationSheetReadResult Fail(string problem) => new(null, [], [problem]);

    /// <summary>Cell text as typed; numbers without exponent notation; empty -> null.</summary>
    private static string? Text(IExcelDataReader reader, int column)
    {
        if (column >= reader.FieldCount) return null;
        var text = reader.GetValue(column) switch
        {
            null or DBNull => null,
            double d when double.IsFinite(d) && Math.Abs(d) < 7.9e27 => ((decimal)d).ToString(CultureInfo.InvariantCulture),
            bool b => b ? "TRUE" : "FALSE",
            var v => Convert.ToString(v, CultureInfo.InvariantCulture),
        };
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
