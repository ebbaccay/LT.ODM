using LT.ODM.Infrastructure.Files;
using SkiaSharp;

namespace LT.ODM.Api.Tests;

public sealed class ColourNamesTests
{
    [Theory]
    [InlineData("RICH BLUE F27", "rich blue")]
    [InlineData("EARTH STRATA S23/AURORA COFFEE S26/BLACK", "earth strata")]   // the main colour is before the first "/"
    [InlineData("Black 095A", "black")]
    [InlineData("LinenS17A9ND/Magic BeigeF22 AELB", "linen")]                 // season code glued to the word
    [InlineData("COLLEGIATE NAVY 54F0", "collegiate navy")]                   // longest phrase wins
    [InlineData("GREFOU/UTIBLK/BLACK", "grefou")]                             // short forms
    [InlineData("RESORT SHIRT OWHITE", "owhite")]
    public void Main_colour_is_read_from_the_name(string name, string word)
        => Assert.Equal(word, ColourNames.Parse(name)?.Word);

    [Theory]
    [InlineData("multicolor 095A")]
    [InlineData("18")]
    [InlineData(null)]
    public void Names_without_a_known_colour_give_nothing(string? name) => Assert.Null(ColourNames.Parse(name));
}

public sealed class ColourViewsTests
{
    /// <summary>Three garments side by side with black outlines: dark red, white (outline only) and blue.</summary>
    private static SKBitmap Lineup()
    {
        var bmp = new SKBitmap(900, 400, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var outline = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
        void Garment(float x, SKColor fill)
        {
            using var paint = new SKPaint { Color = fill };
            canvas.DrawRect(x, 60, 220, 300, paint);
            canvas.DrawRect(x, 60, 220, 300, outline);
        }
        Garment(40, new SKColor(0x6B, 0x2A, 0x2E));
        Garment(340, SKColors.White);
        Garment(640, new SKColor(0x2E, 0x4F, 0x9A));
        return bmp;
    }

    [Fact]
    public void Splits_side_by_side_colourways_and_matches_them_by_name()
    {
        using var page = Lineup();
        var groups = ColourViews.Find(page);
        Assert.Equal(3, groups.Count);

        var names = new[] { "RICH BLUE F27", "SHADOW RED S22", "OFF WHITE", "multicolor 095A" }.Select(ColourNames.Parse).ToList();
        var matches = ColourViews.Match(groups, names);

        int GroupAt(int x) => groups.Select((g, i) => (g, i)).Single(t => t.g.Views.Any(v => v.Left <= x && x <= v.Right)).i;
        Assert.Equal(GroupAt(750), matches[0]!.Group);   // blue
        Assert.Equal(GroupAt(150), matches[1]!.Group);   // red
        Assert.Equal(GroupAt(450), matches[2]!.Group);   // white garment inside its outline
        Assert.Null(matches[3]);                         // no colour in the name
        Assert.True(matches[0]!.Sure && matches[1]!.Sure);
    }

    [Fact]
    public void Far_colours_are_not_matched()
    {
        using var page = Lineup();
        var groups = ColourViews.Find(page);
        var matches = ColourViews.Match(groups, [ColourNames.Parse("EASY YELLOW S17")]);
        Assert.Null(matches[0]);
    }
}
