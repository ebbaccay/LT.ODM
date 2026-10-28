namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// Writes a style's tech pack as a PDF: header facts with the sketch and photo, the colorways, the BOM by section, and
/// each BOM line's material colour per colorway. Pictures come from the style image store; a missing picture leaves
/// an empty frame.
/// </summary>
public interface ITechPackWriter
{
    Task<byte[]> WriteAsync(StyleDetailDto style, string generatedBy, DateTime generatedUtc, CancellationToken ct = default);
}

/// <summary>
/// The same tech pack as an Excel workbook: a Tech pack sheet (facts, sketch and photo), Colorways (with their pictures),
/// BOM (by section, consumption as numbers) and Material colours (each BOM line's colour per colorway).
/// </summary>
public interface ITechPackExcelWriter
{
    Task<byte[]> WriteAsync(StyleDetailDto style, string generatedBy, DateTime generatedUtc, CancellationToken ct = default);
}
