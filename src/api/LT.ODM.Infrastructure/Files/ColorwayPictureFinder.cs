using LT.ODM.Application.ConceptStudio;
using LT.ODM.Application.StyleLibrary;
using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>Colour groups of a sketch (<see cref="ColourViews"/>) matched to colorway names (<see cref="ColourNames"/>). Rules only, no AI.</summary>
public sealed class ColorwayPictureFinder : IColorwayPictureFinder
{
    public (int Width, int Height, IReadOnlyList<ColourGroupDto> Groups, IReadOnlyList<ColourNameMatch> Matches) Find(byte[] content, IReadOnlyList<string?> colorwayNames)
    {
        var kind = ConceptStudioRules.DetectImage(content) ?? throw new UnreadableImageException();
        var (upright, _) = ImageOptimizer.Optimize(content, kind, int.MaxValue);
        using var picture = SKBitmap.Decode(upright) ?? throw new UnreadableImageException();

        var groups = ColourViews.Find(picture);
        var names = colorwayNames.Select(ColourNames.Parse).ToList();
        var matches = ColourViews.Match(groups, names);

        var groupDtos = groups.Select((g, i) => new ColourGroupDto(i, Hex(g.Colour), Box(g.Box), g.Views.Select(Box).ToList())).ToList();
        var matchDtos = names.Select((n, i) => new ColourNameMatch(n is null ? null : Hex(n.Rgb), n?.Word, matches[i]?.Group, matches[i]?.Distance,
            matches[i]?.Sure ?? false)).ToList();
        return (picture.Width, picture.Height, groupDtos, matchDtos);
    }

    private static CropBoxDto Box(SKRectI r) => new(r.Left, r.Top, r.Width, r.Height);

    private static string Hex(SKColor c) => $"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}";
}
