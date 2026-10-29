using LT.ODM.Application.StyleLibrary;
using static LT.ODM.Infrastructure.StyleLibrary.Xlsx;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// The line sheet as an Excel workbook (<see cref="Xlsx"/>): the package in the first rows, then one row per style with a
/// filter on the headings and the style's picture (photo, else sketch) in its row, shrunk to the cell and anchored to it.
/// Price is left empty for costing to fill (roadmap 5.19).
/// </summary>
public sealed partial class PackageLineSheetWriter
{
    private const int HeadingRow = 4;
    /// <summary>The picture column (B) and the height of a style row with pictures: pictures fit inside with a small margin.</summary>
    private const int PictureColumn = 1;
    private const double StyleRowHeight = 100;   // points
    private const int PictureBoxWidth = 120, PictureBoxHeight = 124;   // pixels

    private static readonly (string Title, double Width)[] Columns =
    [
        ("No.", 7), ("Picture", 18), ("Style no.", 20), ("Season", 11), ("Model", 28), ("Description", 36), ("Product type", 18),
        ("Gender", 11), ("Weave", 11), ("Main fabric", 44), ("Other fabrics", 44), ("Trims", 44), ("Recycled", 20), ("Colourways", 36),
        ("Lead time (days)", 12), ("Note", 50), ("Price", 12),
    ];

    public async Task<byte[]> WriteExcelAsync(PackageDetailDto package, string generatedBy, DateTime generatedUtc, CancellationToken ct = default)
    {
        var p = package.Package;
        var items = LineSheetContent.Items(package);
        var pictures = new List<Picture?>();
        foreach (var i in items) pictures.Add(await LoadPictureAsync(images, i.ImageUrl ?? i.SketchUrl, 320, PictureBoxWidth, PictureBoxHeight, ct));
        var withPictures = pictures.Any(x => x is not null);

        var book = new Xlsx();
        var sheet = book.AddSheet("Line sheet").Columns([.. Columns.Select(c => c.Width)]).Freeze(HeadingRow).FilterFrom(HeadingRow, Columns.Length);
        sheet.Row(1, [Cell.Of($"Line sheet: {p.Name}", Title)]);
        sheet.Row(2, [Cell.Of(string.Join(" · ", new[]
        {
            $"{p.CustomerName} ({p.CustomerCode})", p.SeasonCode is null ? null : $"Season {p.SeasonCode}", $"{items.Count} styles",
            $"generated {generatedUtc:yyyy-MM-dd HH:mm} UTC by {generatedBy}",
        }.Where(v => v is not null)))]);
        if (!string.IsNullOrWhiteSpace(p.Description)) sheet.Row(3, [Cell.Of(p.Description)]);
        sheet.Row(HeadingRow, Columns.Select(c => Cell.Of(c.Title, Heading)));

        for (var n = 0; n < items.Count; n++)
        {
            var i = items[n];
            var row = HeadingRow + 1 + n;
            var fabrics = i.Materials.Where(m => m.Kind == "Fabric").Select(m => LineSheetContent.MaterialText(m) + (m.Recycled ? " (recycled)" : "")).ToList();
            var trims = i.Materials.Where(m => m.Kind == "Trim").Select(LineSheetContent.MaterialText);
            sheet.Row(row,
            [
                Cell.Of(n + 1), Cell.Empty, Cell.Of(i.StyleNo), Cell.Of(i.SeasonCode), Cell.Of(i.ModelName, Wrapped), Cell.Of(i.Description, Wrapped),
                Cell.Of(i.ProductTypeName ?? i.ProductTypeCode), Cell.Of(LineSheetContent.GenderText(i.Gender)), Cell.Of(i.WeaveTypeName ?? i.WeaveTypeCode),
                Cell.Of(fabrics.FirstOrDefault(), Wrapped), Cell.Of(string.Join("\n", fabrics.Skip(1)), Wrapped), Cell.Of(string.Join("\n", trims), Wrapped),
                Cell.Of(LineSheetContent.Recycled(i)), Cell.Of(string.Join("\n", LineSheetContent.Colorways(i).Select(LineSheetContent.ColorwayText)), Wrapped),
                Cell.Of(i.GarmentLeadTimeDays is { } d && d > 0 ? d : null), Cell.Of(i.PitchNote, Wrapped), Cell.Empty,
            ], withPictures ? StyleRowHeight : null);
            sheet.AddPicture(row, PictureColumn, pictures[n]);
        }

        // Concept stories for the client (roadmap 5.22): one row each, on their own sheet.
        var stories = LineSheetContent.Stories(package);
        if (stories.Count > 0)
        {
            var styleNos = items.ToDictionary(i => i.PackageItemId, i => i.StyleNo);
            var storySheet = book.AddSheet("Stories").Columns([30, 90, 36, 40]);
            storySheet.Row(1, [Cell.Of("Concept", Heading), Cell.Of("Story", Heading), Cell.Of("Trends", Heading), Cell.Of("Styles", Heading)]);
            for (var n = 0; n < stories.Count; n++)
            {
                var s = stories[n];
                storySheet.Row(2 + n,
                [
                    Cell.Of(s.Name, Wrapped), Cell.Of(s.Story, Wrapped), Cell.Of(s.Tags, Wrapped),
                    Cell.Of(string.Join(", ", s.PackageItemIds.Select(id => styleNos.GetValueOrDefault(id)).OfType<string>()), Wrapped),
                ]);
            }
        }
        return await book.SaveAsync(ct);
    }
}
