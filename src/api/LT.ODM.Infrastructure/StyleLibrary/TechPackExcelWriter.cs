using LT.ODM.Application.StyleLibrary;
using static LT.ODM.Infrastructure.StyleLibrary.Xlsx;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// The tech pack as an Excel workbook (<see cref="Xlsx"/>), the same content as the PDF (<see cref="TechPackPdfWriter"/>):
///   Tech pack        the style's facts with the sketch and the photo beside them;
///   Colorways        one row per colorway with its picture;
///   BOM              one row per BOM line by section, consumption and allowance as numbers (to calculate with);
///   Material colours each BOM line's material colour in each colorway.
/// Labels are English: the tech pack goes to brands and suppliers.
/// </summary>
public sealed class TechPackExcelWriter(IStyleImageStore images) : ITechPackExcelWriter
{
    private const int BigPicture = 260, SmallBoxWidth = 120, SmallBoxHeight = 124;   // pixels
    private const double PictureRowHeight = 100;   // points

    public async Task<byte[]> WriteAsync(StyleDetailDto d, string generatedBy, DateTime generatedUtc, CancellationToken ct = default)
    {
        var s = d.Style;
        var book = new Xlsx();

        // ----- Tech pack: facts left, sketch and photo right -----
        var sheet = book.AddSheet("Tech pack").Columns(22, 44, 3, 38, 38);
        sheet.Row(1, [Cell.Of($"Tech pack: {s.StyleNo}", Title)]);
        sheet.Row(2, [Cell.Of(string.Join(" · ", new[]
        {
            $"{s.CustomerName} ({s.CustomerCode})", $"Season {s.SeasonCode}", s.IsActive ? null : "inactive",
            $"generated {generatedUtc:yyyy-MM-dd HH:mm} UTC by {generatedBy}",
        }.Where(v => v is not null)))]);
        sheet.Row(4, [Cell.Of("Style", Section), Cell.Of("", Section), Cell.Empty, Cell.Of("Sketch", Section), Cell.Of("Photo", Section)]);
        var facts = new (string Label, Cell Value)[]
        {
            ("Style no.", Cell.Of(s.StyleNo, Bold)), ("Customer", Cell.Of($"{s.CustomerName} ({s.CustomerCode})")), ("Season", Cell.Of(s.SeasonCode)),
            ("Model ID", Cell.Of(s.ModelCode)), ("Model name", Cell.Of(s.ModelName)), ("Product type", Cell.Of(s.ProductTypeName ?? s.ProductTypeCode)),
            ("Weave", Cell.Of(s.WeaveTypeName ?? s.WeaveTypeCode)), ("Gender", Cell.Of(LineSheetContent.GenderText(s.Gender))),
            ("Business unit", Cell.Of(s.BusinessUnitName ?? s.BusinessUnitCode)), ("Garment lead time (days)", Cell.Of(s.GarmentLeadTimeDays)),
            ("Base style", Cell.Of(s.BaseStyleNo == s.StyleNo ? null : s.BaseStyleNo)), ("Colorways", Cell.Of(d.Colorways.Count)),
            ("BOM lines", Cell.Of(d.BomLines.Count)), ("Description", Cell.Of(s.Description, Wrapped)),
        };
        for (var k = 0; k < facts.Length; k++) sheet.Row(5 + k, [Cell.Of(facts[k].Label, Label), facts[k].Value]);
        var sketch = await LoadPictureAsync(images, s.SketchUrl, 640, BigPicture, BigPicture, ct);
        var photo = await LoadPictureAsync(images, s.ImageUrl, 640, BigPicture, BigPicture, ct);
        sheet.AddPicture(5, 3, sketch).AddPicture(5, 4, photo);
        if (sketch is null || photo is null)
            sheet.Row(5, [Cell.Of(facts[0].Label, Label), facts[0].Value, Cell.Empty, Cell.Of(sketch is null ? "No sketch" : null, Label), Cell.Of(photo is null ? "No photo" : null, Label)]);

        // ----- Colorways -----
        var lineCount = d.Colorways.ToDictionary(c => c.ColorwayId, c => d.BomLines.Count(b => b.Colorways.Any(x => x.ColorwayId == c.ColorwayId)));
        var colorwayPictures = new List<Picture?>();
        foreach (var c in d.Colorways) colorwayPictures.Add(await LoadPictureAsync(images, c.ImageUrl, 320, SmallBoxWidth, SmallBoxHeight, ct));
        var withPictures = colorwayPictures.Any(p => p is not null);
        var colorways = book.AddSheet("Colorways").Columns(18, 22, 36, 12, 12).Freeze(1).FilterFrom(1, 5);
        colorways.Row(1, new[] { "Picture", "Colorway", "Name", "Status", "BOM lines" }.Select(t => Cell.Of(t, Heading)));
        for (var k = 0; k < d.Colorways.Count; k++)
        {
            var c = d.Colorways[k];
            colorways.Row(2 + k,
            [
                Cell.Empty, Cell.Of(c.ColorwayCode, Bold), Cell.Of(c.ColorwayName, Wrapped), Cell.Of(c.Status == "DROPPED" ? "Dropped" : "In range"),
                Cell.Of(lineCount[c.ColorwayId]),
            ], withPictures ? PictureRowHeight : null);
            colorways.AddPicture(2 + k, 0, colorwayPictures[k]);
        }

        // ----- BOM -----
        var codes = d.Colorways.ToDictionary(c => c.ColorwayId, c => c.ColorwayCode);
        var bom = book.AddSheet("BOM").Columns(18, 7, 22, 60, 16, 30, 30, 11, 9, 11, 10, 7, 30).Freeze(1).FilterFrom(1, 13);
        bom.Row(1, new[]
        {
            "Section", "Part", "Material", "Description", "Type", "Nominated supplier", "SAP supplier", "LCO cons.", "LCO estimate", "Brand cons.",
            "Allowance", "UOM", "Colorways",
        }.Select(t => Cell.Of(t, Heading)));
        var row = 1;
        foreach (var b in d.BomLines)
        {
            var used = b.Colorways.Select(x => codes.GetValueOrDefault(x.ColorwayId, "?")).ToList();
            bom.Row(++row,
            [
                Cell.Of(b.ContentClassName ?? "Unclassified"), Cell.Of(b.PartNo), Cell.Of(b.MaterialCode, Bold), Cell.Of(b.MaterialDescription, Wrapped),
                Cell.Of(b.MaterialTypeName ?? b.MaterialTypeCode), Cell.Of(b.NominatedSupplierName ?? b.NominatedSupplierCode, Wrapped),
                Cell.Of(b.SupplierName ?? b.SupplierCode, Wrapped), Cell.Of((double?)b.LcoConsumption), Cell.Of(b.LcoIsEstimate ? "estimate" : null),
                Cell.Of((double?)b.BrandConsumption), Cell.Of((double?)b.ConsumptionAllowance), Cell.Of(b.UomCode),
                Cell.Of(used.Count == 0 ? null : d.Colorways.Count > 1 && used.Count == d.Colorways.Count ? "All colorways" : string.Join(", ", used), Wrapped),
            ]);
        }

        // ----- Material colours by colorway -----
        var matrix = book.AddSheet("Material colours").Columns([18, 7, 22, 50, .. d.Colorways.Select(_ => 26.0)]).Freeze(1).FilterFrom(1, 4 + d.Colorways.Count);
        matrix.Row(1, new[] { "Section", "Part", "Material", "Description" }
            .Concat(d.Colorways.Select(c => string.IsNullOrWhiteSpace(c.ColorwayName) ? c.ColorwayCode : $"{c.ColorwayCode} · {c.ColorwayName}"))
            .Select(t => Cell.Of(t, Heading)));
        row = 1;
        foreach (var b in d.BomLines)
        {
            var cells = new List<Cell>
            {
                Cell.Of(b.ContentClassName ?? "Unclassified"), Cell.Of(b.PartNo), Cell.Of(b.MaterialCode, Bold), Cell.Of(b.MaterialDescription, Wrapped),
            };
            foreach (var c in d.Colorways)
            {
                var colour = b.Colorways.FirstOrDefault(x => x.ColorwayId == c.ColorwayId);
                cells.Add(colour is null
                    ? Cell.Of("not used", Label)
                    : Cell.Of(string.Join(" — ", new[] { colour.MaterialColorCode, colour.MaterialColorDescription }.Where(v => !string.IsNullOrWhiteSpace(v))), Wrapped));
            }
            matrix.Row(++row, cells);
        }

        return await book.SaveAsync(ct);
    }
}
