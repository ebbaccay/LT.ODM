using System.Globalization;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.StyleAi;

/// <summary>
/// What changed between two styles (usually a style and the one it was reused from). Pure rules, no AI.
/// BOM lines are paired in passes, so each line is used once:
///   1. same material and part number;  2. same material (part number changed);
///   3. same section and part number, another material (Swapped).
/// Lines left over were Removed (older style) or Added (newer style).
/// Colorways pair by code, then by name (Recoded: same colour, new article number).
/// </summary>
public static class StyleComparer
{
    public static StyleRefDto Ref(StyleDetailDto d)
        => new(d.Style.StyleId, d.Style.StyleNo, d.Style.SeasonCode, d.Style.CustomerCode, d.Style.ModelName, d.Style.ImageUrl, d.Style.SketchUrl,
            d.Colorways.Count, d.BomLines.Count);

    public static StyleCompareDto Compare(StyleDetailDto from, StyleDetailDto to, string? relation)
    {
        var (colorways, unchangedColorways) = Colorways(from.Colorways, to.Colorways);
        var (lines, unchangedLines) = BomLines(from.BomLines, to.BomLines);
        return new StyleCompareDto(Ref(from), Ref(to), relation, Header(from.Style, to.Style), colorways, lines, unchangedColorways, unchangedLines);
    }

    private static List<FieldChangeDto> Header(StyleHeaderDto a, StyleHeaderDto b)
    {
        var list = new List<FieldChangeDto>();
        Diff(list, "customer", a.CustomerCode, b.CustomerCode);
        Diff(list, "description", a.Description, b.Description);
        Diff(list, "modelCode", a.ModelCode, b.ModelCode);
        Diff(list, "modelName", a.ModelName, b.ModelName);
        Diff(list, "productType", a.ProductTypeName ?? a.ProductTypeCode, b.ProductTypeName ?? b.ProductTypeCode);
        Diff(list, "weaveType", a.WeaveTypeName ?? a.WeaveTypeCode, b.WeaveTypeName ?? b.WeaveTypeCode);
        Diff(list, "gender", a.Gender, b.Gender);
        Diff(list, "businessUnit", a.BusinessUnitName ?? a.BusinessUnitCode, b.BusinessUnitName ?? b.BusinessUnitCode);
        Diff(list, "leadTimeDays", a.GarmentLeadTimeDays?.ToString(CultureInfo.InvariantCulture), b.GarmentLeadTimeDays?.ToString(CultureInfo.InvariantCulture));
        return list;
    }

    private static (List<ColorwayChangeDto>, int) Colorways(IReadOnlyList<ColorwayDto> from, IReadOnlyList<ColorwayDto> to)
    {
        var result = new List<ColorwayChangeDto>();
        var unchanged = 0;
        var left = from.ToList();
        var unmatched = new List<ColorwayDto>();

        foreach (var c in to)
        {
            var match = left.FirstOrDefault(x => Same(x.ColorwayCode, c.ColorwayCode));
            if (match is null) { unmatched.Add(c); continue; }
            left.Remove(match);
            var fields = new List<FieldChangeDto>();
            Diff(fields, "colorwayName", match.ColorwayName, c.ColorwayName);
            Diff(fields, "status", match.Status, c.Status);
            if (fields.Count == 0) unchanged++;
            else result.Add(new ColorwayChangeDto("Changed", c.ColorwayCode, c.ColorwayName, null, fields));
        }
        foreach (var c in unmatched)
        {
            var match = string.IsNullOrWhiteSpace(c.ColorwayName) ? null : left.FirstOrDefault(x => Same(x.ColorwayName, c.ColorwayName));
            if (match is null) { result.Add(new ColorwayChangeDto("Added", c.ColorwayCode, c.ColorwayName, null, [])); continue; }
            left.Remove(match);
            var fields = new List<FieldChangeDto>();
            Diff(fields, "status", match.Status, c.Status);
            result.Add(new ColorwayChangeDto("Recoded", c.ColorwayCode, c.ColorwayName, match.ColorwayCode, fields));
        }
        result.AddRange(left.Select(c => new ColorwayChangeDto("Removed", c.ColorwayCode, c.ColorwayName, null, [])));
        return (result, unchanged);
    }

    private static (List<BomLineChangeDto>, int) BomLines(IReadOnlyList<BomLineDto> from, IReadOnlyList<BomLineDto> to)
    {
        var left = from.ToList();
        var pending = to.ToList();
        var pairs = new List<(BomLineDto From, BomLineDto To, bool Swapped)>();

        void Pass(Func<BomLineDto, BomLineDto, bool> same, bool swapped)
        {
            foreach (var line in pending.ToList())
            {
                var match = left.FirstOrDefault(x => same(x, line));
                if (match is null) continue;
                left.Remove(match);
                pending.Remove(line);
                pairs.Add((match, line, swapped));
            }
        }

        Pass((a, b) => Same(a.MaterialCode, b.MaterialCode) && a.PartNo == b.PartNo, false);
        Pass((a, b) => Same(a.MaterialCode, b.MaterialCode), false);
        Pass((a, b) => a.PartNo is not null && a.PartNo == b.PartNo && Same(a.ContentClassCode, b.ContentClassCode), true);

        var result = new List<BomLineChangeDto>();
        var unchanged = 0;
        foreach (var (a, b, swapped) in pairs)
        {
            var fields = new List<FieldChangeDto>();
            Diff(fields, "partNo", a.PartNo?.ToString(CultureInfo.InvariantCulture), b.PartNo?.ToString(CultureInfo.InvariantCulture));
            if (!swapped) Diff(fields, "materialDescription", a.MaterialDescription, b.MaterialDescription);
            Diff(fields, "materialType", a.MaterialTypeName ?? a.MaterialTypeCode, b.MaterialTypeName ?? b.MaterialTypeCode);
            Diff(fields, "lcoConsumption", Num(a.LcoConsumption), Num(b.LcoConsumption));
            Diff(fields, "brandConsumption", Num(a.BrandConsumption), Num(b.BrandConsumption));
            Diff(fields, "uom", a.UomCode, b.UomCode);
            Diff(fields, "supplier", a.SupplierName ?? a.SupplierCode, b.SupplierName ?? b.SupplierCode);
            Diff(fields, "nominatedSupplier", a.NominatedSupplierName ?? a.NominatedSupplierCode, b.NominatedSupplierName ?? b.NominatedSupplierCode);
            if (swapped)
                result.Add(new BomLineChangeDto("Swapped", b.ContentClassCode, b.ContentClassName, b.PartNo, b.MaterialCode, b.MaterialDescription,
                    a.MaterialCode, a.MaterialDescription, fields));
            else if (fields.Count > 0)
                result.Add(new BomLineChangeDto("Changed", b.ContentClassCode, b.ContentClassName, b.PartNo, b.MaterialCode, b.MaterialDescription,
                    null, null, fields));
            else unchanged++;
        }
        result.AddRange(pending.Select(b => new BomLineChangeDto("Added", b.ContentClassCode, b.ContentClassName, b.PartNo, b.MaterialCode,
            b.MaterialDescription, null, null, [])));
        result.AddRange(left.Select(a => new BomLineChangeDto("Removed", a.ContentClassCode, a.ContentClassName, a.PartNo, a.MaterialCode,
            a.MaterialDescription, null, null, [])));

        // Section, then part number, so the screen can group them.
        return (result.OrderBy(r => r.ContentClassCode ?? "~").ThenBy(r => r.PartNo ?? int.MaxValue).ThenBy(r => r.MaterialCode).ToList(), unchanged);
    }

    private static void Diff(List<FieldChangeDto> list, string field, string? before, string? after)
    {
        var a = Norm(before);
        var b = Norm(after);
        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) list.Add(new FieldChangeDto(field, a, b));
    }

    private static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool Same(string? a, string? b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>1.775000 -> "1.775"; null and 0 both read as "not set".</summary>
    private static string? Num(decimal? value) => value is null or 0 ? null : value.Value.ToString("0.######", CultureInfo.InvariantCulture);
}
