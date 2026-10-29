namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// A style package's line sheet for the client, as a PDF (cards with pictures) or an Excel workbook (one row per style, picture in the row).
/// Only what the client may see: the offered colourways and the note for the client; internal notes are never written,
/// and styles no longer of the package's brand are left out. There is no price yet: the Excel has an empty Price column
/// to be filled when costing comes (roadmap 5.19).
/// </summary>
public interface IPackageLineSheetWriter
{
    Task<byte[]> WritePdfAsync(PackageDetailDto package, string generatedBy, DateTime generatedUtc, CancellationToken ct = default);

    Task<byte[]> WriteExcelAsync(PackageDetailDto package, string generatedBy, DateTime generatedUtc, CancellationToken ct = default);
}

/// <summary>What goes on a line sheet, shared by the PDF and Excel writers.</summary>
public static class LineSheetContent
{
    /// <summary>The styles the client sees, in pitch order (another brand's style never goes out).</summary>
    public static IReadOnlyList<PackageItemDto> Items(PackageDetailDto package)
        => package.Items.Where(i => !i.OtherBrand).OrderBy(i => i.SortOrder).ThenBy(i => i.PackageItemId).ToList();

    /// <summary>The concept story pages, keeping only the styles the client sees (a story with none of them is left out).</summary>
    public static IReadOnlyList<PackageStoryDto> Stories(PackageDetailDto package)
    {
        var shown = Items(package).Select(i => i.PackageItemId).ToHashSet();
        return (package.Stories ?? [])
            .Select(s => s with { PackageItemIds = s.PackageItemIds.Where(shown.Contains).ToList() })
            .Where(s => s.PackageItemIds.Count > 0)
            .ToList();
    }

    /// <summary>The offered colourways that are still on the style.</summary>
    public static IReadOnlyList<PackageColorwayDto> Colorways(PackageItemDto item) => item.Colorways.Where(c => c.Offered && !c.Missing).ToList();

    public static string ColorwayText(PackageColorwayDto c)
        => string.IsNullOrWhiteSpace(c.ColorwayName) || c.ColorwayName == c.ColorwayCode ? c.ColorwayCode : $"{c.ColorwayCode} {c.ColorwayName}";

    /// <summary>A fabric as the client reads it: the composition when it was read, else the BOM description.</summary>
    public static string MaterialText(PackageMaterialDto m) => m.Composition ?? m.Description ?? m.MaterialCode;

    public static string Facts(PackageItemDto i)
        => string.Join(" · ", new[] { i.ProductTypeName ?? i.ProductTypeCode, GenderText(i.Gender), i.WeaveTypeName ?? i.WeaveTypeCode }
            .Where(v => !string.IsNullOrWhiteSpace(v)));

    public static string? Recycled(PackageItemDto i)
        => i.RecycledFabricLines == 0 ? null
            : i.RecycledFabricLines == i.FabricLines ? "Recycled fabric" : $"Recycled: {i.RecycledFabricLines} of {i.FabricLines} fabrics";

    /// <summary>MALE -> Men, FEMALE -> Women, UNISEX, KIDS; other codes as they are.</summary>
    public static string? GenderText(string? gender) => gender switch
    {
        "MALE" => "Men", "FEMALE" => "Women", "UNISEX" => "Unisex", "KIDS" => "Kids", _ => gender,
    };
}
