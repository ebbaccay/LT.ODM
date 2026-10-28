using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using LT.ODM.Application.StyleLibrary;
using SkiaSharp;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// A small .xlsx writer for the Style Library downloads (tech pack, style package line sheet): sheets of text and number
/// cells, column widths, frozen headings, a filter, row heights and pictures anchored to a cell (they move with sorting and
/// filtering). Written by hand (a .xlsx is a zip of XML parts), as the translations workbook, so no extra package is needed.
/// Cell styles: <see cref="Normal"/>, <see cref="Heading"/> (bold on grey, wrapped), <see cref="Wrapped"/>, <see cref="Title"/>,
/// <see cref="Section"/> (bold on light blue), <see cref="Bold"/>, <see cref="Label"/> (grey text).
/// </summary>
internal sealed class Xlsx
{
    public const int Normal = 0, Heading = 1, Wrapped = 2, Title = 3, Section = 4, Bold = 5, Label = 6;

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string DrawingMain = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const long Emu = 9525;   // EMUs per pixel

    private readonly List<Sheet> sheets = [];

    /// <summary>A cell value: text (an inline string, so no shared-strings part is needed) or a number; neither = empty.</summary>
    public readonly record struct Cell(string? Text, double? Number, int Style = Normal)
    {
        public static Cell Of(string? text, int style = Normal) => new(string.IsNullOrEmpty(text) ? null : text, null, style);
        public static Cell Of(double? number, int style = Normal) => new(null, number, style);
        public static readonly Cell Empty = default;
    }

    /// <summary>A picture fitted to its box (JPEG on white, stored at twice its size so it stays sharp); Width / Height in pixels as shown.</summary>
    public sealed record Picture(byte[] Jpeg, int Width, int Height);

    public Sheet AddSheet(string name)
    {
        var sheet = new Sheet(name);
        sheets.Add(sheet);
        return sheet;
    }

    public sealed class Sheet(string name)
    {
        internal string Name { get; } = name;
        internal List<double> Widths { get; } = [];
        internal SortedDictionary<int, (Cell[] Cells, double? Height)> Rows { get; } = [];
        internal List<(int Row, int Column, Picture Picture, int Margin)> Pictures { get; } = [];
        internal int FrozenRows { get; private set; }
        internal (int Row, int Columns)? Filter { get; private set; }

        /// <summary>Column widths in characters, from column A.</summary>
        public Sheet Columns(params double[] widths)
        {
            Widths.AddRange(widths);
            return this;
        }

        /// <summary>Rows 1..rows stay visible while scrolling.</summary>
        public Sheet Freeze(int rows)
        {
            FrozenRows = rows;
            return this;
        }

        /// <summary>A filter on the heading row, over the given number of columns down to the last row.</summary>
        public Sheet FilterFrom(int headingRow, int columns)
        {
            Filter = (headingRow, columns);
            return this;
        }

        /// <summary>Cells from column A; height in points (null = Excel's own).</summary>
        public Sheet Row(int row, IEnumerable<Cell> cells, double? height = null)
        {
            Rows[row] = (cells.ToArray(), height);
            return this;
        }

        /// <summary>A picture whose top-left corner is in the cell (column 0 = A), a margin in pixels from its corner.</summary>
        public Sheet AddPicture(int row, int column, Picture? picture, int margin = 5)
        {
            if (picture is not null) Pictures.Add((row, column, picture, margin));
            return this;
        }

        internal int LastRow => Rows.Count == 0 ? 1 : Rows.Keys.Max();
    }

    /// <summary>
    /// A style image (/api/v1/styles/images/...) fitted into a box of the given pixels, or null when there is none or the
    /// file is missing. thumbnail must be one of ImageRules.ThumbnailWidths (the store gives nothing for other widths).
    /// </summary>
    public static async Task<Picture?> LoadPictureAsync(IStyleImageStore images, string? url, int thumbnail, int boxWidth, int boxHeight, CancellationToken ct)
    {
        if (Pdf.StyleImageFile(url) is not { } name || await images.OpenThumbnailAsync(name, thumbnail, ct) is not { } file) return null;
        byte[] bytes;
        await using (var content = file.Content)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            bytes = copy.ToArray();
        }
        using var source = SKImage.FromEncodedData(bytes);
        if (source is null) return null;
        var scale = Math.Min((double)boxWidth / source.Width, (double)boxHeight / source.Height);
        var (width, height) = (Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        using var surface = SKSurface.Create(new SKImageInfo(width * 2, height * 2));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(source, new SKRect(0, 0, width * 2, height * 2), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = surface.Snapshot();
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return new Picture(jpeg.ToArray(), width, height);
    }

    // ----- writing -----

    public async Task<byte[]> SaveAsync(CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var withDrawing = sheets.Select((s, i) => (s, i)).Where(x => x.s.Pictures.Count > 0).Select(x => x.i).ToHashSet();
            Part(zip, "[Content_Types].xml", w => ContentTypes(w, withDrawing));
            Part(zip, "_rels/.rels", w => Relationships(w, ("rId1", $"{Rel}/officeDocument", "xl/workbook.xml")));
            Part(zip, "xl/workbook.xml", Workbook);
            Part(zip, "xl/_rels/workbook.xml.rels", w => Relationships(w,
                [.. sheets.Select((_, i) => ($"rId{i + 1}", $"{Rel}/worksheet", $"worksheets/sheet{i + 1}.xml")), ($"rId{sheets.Count + 1}", $"{Rel}/styles", "styles.xml")]));
            Part(zip, "xl/styles.xml", Styles);
            var media = 0;
            for (var i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                Part(zip, $"xl/worksheets/sheet{i + 1}.xml", w => SheetXml(w, sheet));
                if (sheet.Pictures.Count == 0) continue;
                Part(zip, $"xl/worksheets/_rels/sheet{i + 1}.xml.rels", w => Relationships(w, ("rId1", $"{Rel}/drawing", $"../drawings/drawing{i + 1}.xml")));
                var files = sheet.Pictures.Select(_ => $"image{++media}.jpg").ToList();
                Part(zip, $"xl/drawings/drawing{i + 1}.xml", w => DrawingXml(w, sheet));
                Part(zip, $"xl/drawings/_rels/drawing{i + 1}.xml.rels",
                    w => Relationships(w, [.. files.Select((f, k) => ($"rId{k + 1}", $"{Rel}/image", $"../media/{f}"))]));
                for (var k = 0; k < files.Count; k++)
                {
                    await using var stream = zip.CreateEntry($"xl/media/{files[k]}", CompressionLevel.NoCompression).Open();
                    await stream.WriteAsync(sheet.Pictures[k].Picture.Jpeg, ct);
                }
            }
        }
        return buffer.ToArray();
    }

    private static void Part(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var w = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        w.WriteStartDocument(standalone: true);
        write(w);
        w.WriteEndDocument();
    }

    private void ContentTypes(XmlWriter w, HashSet<int> withDrawing)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        w.WriteStartElement("Types", ns);
        foreach (var (ext, type) in new[] { ("rels", "application/vnd.openxmlformats-package.relationships+xml"), ("xml", "application/xml"), ("jpg", "image/jpeg") })
        {
            w.WriteStartElement("Default", ns);
            w.WriteAttributeString("Extension", ext);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
        var parts = new List<(string, string)> { ("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml") };
        for (var i = 0; i < sheets.Count; i++)
        {
            parts.Add(($"/xl/worksheets/sheet{i + 1}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
            if (withDrawing.Contains(i)) parts.Add(($"/xl/drawings/drawing{i + 1}.xml", "application/vnd.openxmlformats-officedocument.drawing+xml"));
        }
        parts.Add(("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));
        foreach (var (part, type) in parts)
        {
            w.WriteStartElement("Override", ns);
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

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

    private void Workbook(XmlWriter w)
    {
        w.WriteStartElement("workbook", Main);
        w.WriteAttributeString("xmlns", "r", null, Rel);
        w.WriteStartElement("sheets", Main);
        for (var i = 0; i < sheets.Count; i++)
        {
            w.WriteStartElement("sheet", Main);
            w.WriteAttributeString("name", sheets[i].Name);
            w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("id", Rel, $"rId{i + 1}");
            w.WriteEndElement();
        }
        w.WriteEndElement();
        // The filters on the heading rows.
        var filtered = sheets.Select((s, i) => (s, i)).Where(x => x.s.Filter is not null).ToList();
        if (filtered.Count > 0)
        {
            w.WriteStartElement("definedNames", Main);
            foreach (var (s, i) in filtered)
            {
                w.WriteStartElement("definedName", Main);
                w.WriteAttributeString("name", "_xlnm._FilterDatabase");
                w.WriteAttributeString("localSheetId", i.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("hidden", "1");
                w.WriteString($"'{s.Name.Replace("'", "''")}'!{FilterRange(s, absolute: true)}");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static string FilterRange(Sheet s, bool absolute)
    {
        var (row, columns) = s.Filter!.Value;
        var last = Math.Max(s.LastRow, row);
        var d = absolute ? "$" : "";
        return $"{d}A{d}{row}:{d}{ColumnName(columns - 1)}{d}{last}";
    }

    private static void Styles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", Main);
        w.WriteStartElement("fonts", Main);
        w.WriteAttributeString("count", "4");
        Font(bold: false, size: "11", grey: false);
        Font(bold: true, size: "11", grey: false);
        Font(bold: true, size: "14", grey: false);
        Font(bold: false, size: "10", grey: true);
        w.WriteEndElement();

        w.WriteStartElement("fills", Main);
        w.WriteAttributeString("count", "4");
        Fill("none", null);
        Fill("gray125", null);
        Fill("solid", "FFE5E7EB");   // heading
        Fill("solid", "FFEEF2FF");   // section
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
        w.WriteAttributeString("count", "7");
        Xf(0, 0, wrap: false);   // 0 normal
        Xf(1, 2, wrap: true);    // 1 heading
        Xf(0, 0, wrap: true);    // 2 wrapped
        Xf(2, 0, wrap: false);   // 3 title
        Xf(1, 3, wrap: false);   // 4 section
        Xf(1, 0, wrap: false);   // 5 bold
        Xf(3, 0, wrap: false);   // 6 label
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

        void Font(bool bold, string size, bool grey)
        {
            w.WriteStartElement("font", Main);
            if (bold) w.WriteElementString("b", Main, "");
            Val("sz", size);
            if (grey)
            {
                w.WriteStartElement("color", Main);
                w.WriteAttributeString("rgb", "FF6B7280");
                w.WriteEndElement();
            }
            Val("name", "Calibri");
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

    private static void SheetXml(XmlWriter w, Sheet s)
    {
        w.WriteStartElement("worksheet", Main);
        w.WriteAttributeString("xmlns", "r", null, Rel);
        w.WriteStartElement("sheetViews", Main);
        w.WriteStartElement("sheetView", Main);
        w.WriteAttributeString("workbookViewId", "0");
        if (s.FrozenRows > 0)
        {
            w.WriteStartElement("pane", Main);
            w.WriteAttributeString("ySplit", s.FrozenRows.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("topLeftCell", $"A{s.FrozenRows + 1}");
            w.WriteAttributeString("activePane", "bottomLeft");
            w.WriteAttributeString("state", "frozen");
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteEndElement();

        if (s.Widths.Count > 0)
        {
            w.WriteStartElement("cols", Main);
            for (var c = 0; c < s.Widths.Count; c++)
            {
                w.WriteStartElement("col", Main);
                w.WriteAttributeString("min", (c + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("max", (c + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("width", s.Widths[c].ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteStartElement("sheetData", Main);
        foreach (var (rowNo, (cells, height)) in s.Rows)
        {
            w.WriteStartElement("row", Main);
            w.WriteAttributeString("r", rowNo.ToString(CultureInfo.InvariantCulture));
            if (height is { } h)
            {
                w.WriteAttributeString("ht", h.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("customHeight", "1");
            }
            for (var c = 0; c < cells.Length; c++)
            {
                var (text, number, style) = cells[c];
                if (text is null && number is null && style == Normal) continue;
                w.WriteStartElement("c", Main);
                w.WriteAttributeString("r", ColumnName(c) + rowNo.ToString(CultureInfo.InvariantCulture));
                if (style != Normal) w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
                if (number is { } n) w.WriteElementString("v", Main, n.ToString("R", CultureInfo.InvariantCulture));
                else if (text is not null)
                {
                    w.WriteAttributeString("t", "inlineStr");
                    w.WriteStartElement("is", Main);
                    w.WriteStartElement("t", Main);
                    w.WriteAttributeString("xml", "space", null, "preserve");
                    w.WriteString(text.All(XmlConvert.IsXmlChar) ? text : new string(text.Where(XmlConvert.IsXmlChar).ToArray()));
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();

        if (s.Filter is not null)
        {
            w.WriteStartElement("autoFilter", Main);
            w.WriteAttributeString("ref", FilterRange(s, absolute: false));
            w.WriteEndElement();
        }
        if (s.Pictures.Count > 0)
        {
            w.WriteStartElement("drawing", Main);
            w.WriteAttributeString("id", Rel, "rId1");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>One-cell anchors: each picture moves with its cell and keeps its size.</summary>
    private static void DrawingXml(XmlWriter w, Sheet s)
    {
        w.WriteStartElement("xdr", "wsDr", Drawing);
        w.WriteAttributeString("xmlns", "a", null, DrawingMain);
        w.WriteAttributeString("xmlns", "r", null, Rel);
        for (var k = 0; k < s.Pictures.Count; k++)
        {
            var (row, column, pic, margin) = s.Pictures[k];
            var (cx, cy) = ((pic.Width * Emu).ToString(CultureInfo.InvariantCulture), (pic.Height * Emu).ToString(CultureInfo.InvariantCulture));
            w.WriteStartElement("xdr", "oneCellAnchor", Drawing);
            w.WriteStartElement("xdr", "from", Drawing);
            w.WriteElementString("xdr", "col", Drawing, column.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("xdr", "colOff", Drawing, (margin * Emu).ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("xdr", "row", Drawing, (row - 1).ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("xdr", "rowOff", Drawing, (margin * Emu).ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteStartElement("xdr", "ext", Drawing);
            w.WriteAttributeString("cx", cx);
            w.WriteAttributeString("cy", cy);
            w.WriteEndElement();

            w.WriteStartElement("xdr", "pic", Drawing);
            w.WriteStartElement("xdr", "nvPicPr", Drawing);
            w.WriteStartElement("xdr", "cNvPr", Drawing);
            w.WriteAttributeString("id", (k + 2).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("name", $"Picture {k + 1}");
            w.WriteEndElement();
            w.WriteStartElement("xdr", "cNvPicPr", Drawing);
            w.WriteStartElement("a", "picLocks", DrawingMain);
            w.WriteAttributeString("noChangeAspect", "1");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xdr", "blipFill", Drawing);
            w.WriteStartElement("a", "blip", DrawingMain);
            w.WriteAttributeString("r", "embed", Rel, $"rId{k + 1}");
            w.WriteEndElement();
            w.WriteStartElement("a", "stretch", DrawingMain);
            w.WriteElementString("a", "fillRect", DrawingMain, "");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xdr", "spPr", Drawing);
            w.WriteStartElement("a", "xfrm", DrawingMain);
            w.WriteStartElement("a", "off", DrawingMain);
            w.WriteAttributeString("x", "0");
            w.WriteAttributeString("y", "0");
            w.WriteEndElement();
            w.WriteStartElement("a", "ext", DrawingMain);
            w.WriteAttributeString("cx", cx);
            w.WriteAttributeString("cy", cy);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("a", "prstGeom", DrawingMain);
            w.WriteAttributeString("prst", "rect");
            w.WriteElementString("a", "avLst", DrawingMain, "");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteElementString("xdr", "clientData", Drawing, "");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>0 -> A, 25 -> Z, 26 -> AA.</summary>
    public static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }
}
