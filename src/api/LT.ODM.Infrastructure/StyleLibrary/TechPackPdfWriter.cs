using System.Globalization;
using LT.ODM.Application.StyleLibrary;
using SkiaSharp;
using static LT.ODM.Infrastructure.StyleLibrary.Pdf;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// Tech pack PDF drawn with SkiaSharp (already used for images; no PDF library to license). A4 landscape, because the
/// BOM and the colour matrix are wide. Pages are laid out first (a list of drawing steps per page) and drawn after, so
/// the footer can say "page x of y". Text stays text in the PDF (searchable, copyable); fonts are embedded as subsets.
/// Labels are English: the tech pack goes to brands and suppliers.
/// </summary>
public sealed class TechPackPdfWriter(IStyleImageStore images) : ITechPackWriter
{
    /// <summary>Colorways per colour matrix table; more colorways repeat the table with the next ones.</summary>
    private const int MatrixColorways = 4;

    public async Task<byte[]> WriteAsync(StyleDetailDto d, string generatedBy, DateTime generatedUtc, CancellationToken ct = default)
    {
        // Pictures first (async), drawing after (Skia is synchronous). Sketch and photo large, colorway pictures small.
        var pictures = new Dictionary<string, SKImage>();
        try
        {
            async Task Load(string? url, int width)
            {
                var name = StyleImageFile(url);
                if (name is null || pictures.ContainsKey(url!)) return;
                var opened = await images.OpenThumbnailAsync(name, width, ct);
                if (opened is not { } file) return;
                await using var content = file.Content;
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, ct);
                if (SKImage.FromEncodedData(buffer.ToArray()) is { } image) pictures[url!] = image;
            }

            await Load(d.Style.SketchUrl, 640);
            await Load(d.Style.ImageUrl, 640);
            foreach (var c in d.Colorways) await Load(c.ImageUrl, 160);

            using var fonts = new Fonts();
            var layout = new Layout(fonts);
            Header(layout, d, pictures);
            Colorways(layout, d, pictures);
            Bom(layout, d);
            ColourMatrix(layout, d);
            return layout.Render(
                $"{d.Style.StyleNo} · {d.Style.CustomerCode} {d.Style.SeasonCode} · Tech pack",
                $"Generated {generatedUtc:yyyy-MM-dd HH:mm} UTC by {generatedBy}",
                new SKDocumentPdfMetadata
                {
                    Title = $"Tech pack {d.Style.StyleNo}", Author = generatedBy, Creator = "LT ODM", Producer = "LT ODM",
                    Creation = generatedUtc, Modified = generatedUtc,
                });
        }
        finally
        {
            foreach (var p in pictures.Values) p.Dispose();
        }
    }

    // ----- Sections -----

    private static void Header(Layout l, StyleDetailDto d, IReadOnlyDictionary<string, SKImage> pictures)
    {
        var s = d.Style;
        var f = l.Fonts;
        // Title band
        l.Text("TECH PACK", f.Bold, 8, Layout.Accent, l.Left, l.Y + 8);
        l.Text(s.StyleNo, f.Bold, 20, Layout.Ink, l.Left, l.Y + 30);
        if (!string.IsNullOrWhiteSpace(s.ModelName)) l.Text(s.ModelName!, f.Regular, 11, Layout.Muted, l.Left, l.Y + 46);
        l.TextRight($"{s.CustomerName} ({s.CustomerCode})", f.Bold, 11, Layout.Ink, l.Right, l.Y + 30);
        l.TextRight($"Season {s.SeasonCode}" + (s.IsActive ? "" : " · inactive"), f.Regular, 10, Layout.Muted, l.Right, l.Y + 46);
        l.Y += 56;
        l.Rule(Layout.Accent, 1.5f);
        l.Y += 14;

        // Pictures left, facts right
        const float pic = 190, gap = 12;
        var top = l.Y;
        l.Picture(pictures.GetValueOrDefault(s.SketchUrl ?? ""), "Sketch", l.Left, top, pic, pic);
        l.Picture(pictures.GetValueOrDefault(s.ImageUrl ?? ""), "Photo", l.Left + pic + gap, top, pic, pic);

        var x = l.Left + 2 * (pic + gap) + 8;
        var width = l.Right - x;
        var facts = new (string Label, string? Value)[]
        {
            ("Customer", $"{s.CustomerName} ({s.CustomerCode})"), ("Season", s.SeasonCode),
            ("Model ID", s.ModelCode), ("Model name", s.ModelName),
            ("Product type", s.ProductTypeName ?? s.ProductTypeCode), ("Weave", s.WeaveTypeName ?? s.WeaveTypeCode),
            ("Gender", s.Gender), ("Business unit", s.BusinessUnitName ?? s.BusinessUnitCode),
            ("Garment lead time", s.GarmentLeadTimeDays is { } days ? $"{days} days" : null), ("Base style", s.BaseStyleNo == s.StyleNo ? null : s.BaseStyleNo),
            ("Colorways", d.Colorways.Count.ToString(CultureInfo.InvariantCulture)), ("BOM lines", d.BomLines.Count.ToString(CultureInfo.InvariantCulture)),
        };
        var col = (width - 16) / 2;
        var y = top;
        for (var i = 0; i < facts.Length; i += 2)
        {
            var rowHeight = 0f;
            for (var j = 0; j < 2 && i + j < facts.Length; j++)
            {
                var (label, value) = facts[i + j];
                var cx = x + j * (col + 16);
                l.Text(label, f.Regular, 8, Layout.Muted, cx, y + 8);
                var lines = l.Wrap(value ?? "—", f.Regular, 10, col);
                for (var k = 0; k < lines.Count; k++) l.Text(lines[k], f.Regular, 10, Layout.Ink, cx, y + 21 + k * 12.5f);
                rowHeight = Math.Max(rowHeight, 18 + lines.Count * 12.5f);
            }
            y += rowHeight + 6;
        }
        if (!string.IsNullOrWhiteSpace(s.Description))
        {
            l.Text("Description", f.Regular, 8, Layout.Muted, x, y + 8);
            var lines = l.Wrap(s.Description!, f.Regular, 10, width);
            for (var k = 0; k < lines.Count; k++) l.Text(lines[k], f.Regular, 10, Layout.Ink, x, y + 21 + k * 12.5f);
            y += 18 + lines.Count * 12.5f;
        }
        l.Y = Math.Max(top + pic, y) + 18;
    }

    private static void Colorways(Layout l, StyleDetailDto d, IReadOnlyDictionary<string, SKImage> pictures)
    {
        l.Heading("Colorways", d.Colorways.Count);
        if (d.Colorways.Count == 0)
        {
            l.Note("No colorways.");
            return;
        }
        var lineCount = d.Colorways.ToDictionary(c => c.ColorwayId, c => d.BomLines.Count(b => b.Colorways.Any(x => x.ColorwayId == c.ColorwayId)));
        // A picture column only when some colorway has a picture (otherwise rows of empty frames).
        var withPictures = d.Colorways.Any(c => pictures.ContainsKey(c.ImageUrl ?? ""));
        var columns = new List<Column> { new("Colorway", 130), new("Name", 0), new("Status", 90), new("BOM lines", 70, Right: true) };
        if (withPictures) columns.Insert(0, new Column("Picture", 56));
        var table = new Table(l, columns);
        foreach (var c in d.Colorways)
        {
            var cells = new List<Cell>
            {
                new(c.ColorwayCode, Bold: true), new(c.ColorwayName ?? "—"),
                new(c.Status == "DROPPED" ? "Dropped" : "In range", Muted: c.Status == "DROPPED"),
                new(lineCount[c.ColorwayId].ToString(CultureInfo.InvariantCulture)),
            };
            if (withPictures) cells.Insert(0, new Cell(Image: pictures.GetValueOrDefault(c.ImageUrl ?? ""), Picture: true));
            table.Row(cells, minHeight: withPictures ? 48 : 0);
        }
        l.Y += 16;
    }

    private static void Bom(Layout l, StyleDetailDto d)
    {
        l.Heading("Bill of materials", d.BomLines.Count);
        if (d.BomLines.Count == 0)
        {
            l.Note("No BOM lines.");
            return;
        }
        var codes = d.Colorways.ToDictionary(c => c.ColorwayId, c => c.ColorwayCode);
        var table = new Table(l,
        [
            new Column("Part", 34), new Column("Material", 0), new Column("Type", 82), new Column("Supplier", 150),
            new Column("LCO cons.", 62, Right: true), new Column("Brand cons.", 62, Right: true), new Column("Colorways", 110),
        ]);
        foreach (var section in Sections(d.BomLines))
        {
            table.Section($"{section.Name} · {section.Lines.Count}");
            foreach (var b in section.Lines)
            {
                var used = b.Colorways.Select(x => codes.GetValueOrDefault(x.ColorwayId, "?")).ToList();
                var colorways = used.Count == 0 ? "—" : d.Colorways.Count > 1 && used.Count == d.Colorways.Count ? "All colorways" : string.Join(", ", used);
                table.Row(
                [
                    new Cell(b.PartNo?.ToString(CultureInfo.InvariantCulture) ?? "—", Muted: true),
                    new Cell(b.MaterialCode, Bold: true, Sub: b.MaterialDescription),
                    new Cell(b.MaterialTypeName ?? b.MaterialTypeCode ?? "—"),
                    new Cell(b.NominatedSupplierName ?? b.NominatedSupplierCode ?? "—", Sub: (b.SupplierName ?? b.SupplierCode) is { } sap ? $"SAP: {sap}" : null),
                    new Cell(Amount(b.LcoConsumption, b.UomCode) + (b.LcoIsEstimate ? " (est.)" : "")),
                    new Cell(Amount(b.BrandConsumption, b.UomCode)),
                    new Cell(colorways, Muted: used.Count == 0),
                ]);
            }
        }
        l.Y += 16;
    }

    /// <summary>Each BOM line's material colour in each colorway, a few colorways per table.</summary>
    private static void ColourMatrix(Layout l, StyleDetailDto d)
    {
        if (d.Colorways.Count == 0 || d.BomLines.Count == 0) return;
        for (var start = 0; start < d.Colorways.Count; start += MatrixColorways)
        {
            var chunk = d.Colorways.Skip(start).Take(MatrixColorways).ToList();
            var title = d.Colorways.Count > MatrixColorways
                ? $"Material colours by colorway ({start + 1}–{start + chunk.Count} of {d.Colorways.Count})"
                : "Material colours by colorway";
            l.Heading(title, null, newPage: start == 0);
            var columns = new List<Column> { new("Part", 34), new("Material", 210) };
            var colourWidth = (l.Right - l.Left - 34 - 210) / MatrixColorways;
            columns.AddRange(chunk.Select(c => new Column(c.ColorwayCode + (string.IsNullOrWhiteSpace(c.ColorwayName) ? "" : $" · {c.ColorwayName}"), colourWidth)));
            var table = new Table(l, columns);
            foreach (var section in Sections(d.BomLines))
            {
                table.Section(section.Name);
                foreach (var b in section.Lines)
                {
                    var cells = new List<Cell>
                    {
                        new(b.PartNo?.ToString(CultureInfo.InvariantCulture) ?? "—", Muted: true),
                        // One line of description here; the BOM table above has it in full.
                        new(b.MaterialCode, Bold: true, Sub: b.MaterialDescription, SubOneLine: true),
                    };
                    foreach (var c in chunk)
                    {
                        var colour = b.Colorways.FirstOrDefault(x => x.ColorwayId == c.ColorwayId);
                        cells.Add(colour is null
                            ? new Cell("not used", Muted: true)
                            : new Cell(colour.MaterialColorCode ?? colour.MaterialColorDescription ?? "—",
                                Sub: colour.MaterialColorCode is null ? null : colour.MaterialColorDescription));
                    }
                    table.Row(cells);
                }
            }
            l.Y += 16;
        }
    }

    private static List<(string Name, List<BomLineDto> Lines)> Sections(IReadOnlyList<BomLineDto> lines)
        // Lines arrive in section order (usp_Style_Get); keep it.
        => lines.GroupBy(b => b.ContentClassName ?? "Unclassified").Select(g => (g.Key, g.ToList())).ToList();

    private static string Amount(decimal? value, string? uom)
        => value is { } v ? $"{v.ToString("0.####", CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(uom) ? "" : " " + uom)}" : "—";
}
