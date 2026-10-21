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
