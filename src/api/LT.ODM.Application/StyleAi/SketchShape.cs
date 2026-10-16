using LT.ODM.Application.Ai;

namespace LT.ODM.Application.StyleAi;

/// <summary>
/// What a style's sketch shows about the garment's shape, read by the image model built into the API (nothing is sent
/// anywhere): Features are prompt words such as "long sleeves", "a high neck", "maxi length", kept only when the model
/// is clearly sure; FrontAndBack: the sketch has two separate drawings (front and back views). The BOM says nothing about
/// shape, so without these a render from the description alone invents one (a sundress for a long-sleeved midi dress).
/// </summary>
public sealed record SketchShapeDto(IReadOnlyList<string> Features, bool FrontAndBack)
{
    public static readonly SketchShapeDto None = new([], false);
}

public interface ISketchShapeReader
{
    /// <summary>
    /// The shape read from the sketch (cached by <paramref name="cacheKey"/>, the stored file name), or None when the model
    /// is not installed, AI is off, or the picture cannot be read. productTypeName helps when the model is unsure whether
    /// it is a top, bottom, dress or skirt.
    /// </summary>
    Task<SketchShapeDto> ReadAsync(string cacheKey, Func<CancellationToken, Task<byte[]?>> sketch, string? productTypeName, CancellationToken ct = default);
}

/// <summary>
/// Front-and-back sketches for image-to-image models, which draw one garment per picture well and two poorly: the sketch
/// is cut into its two drawings, each is rendered on its own, and the two results are joined side by side.
/// </summary>
public interface ISketchViews
{
    /// <summary>The two drawings cut apart, front first (the left one, or the upper one when stacked); null unless there are exactly two.</summary>
    (ReferenceImage Front, ReferenceImage Back)? Split(ReferenceImage sketch);

    /// <summary>The two renders side by side on the same background, as one PNG.</summary>
    GeneratedImage Join(GeneratedImage front, GeneratedImage back);
}
