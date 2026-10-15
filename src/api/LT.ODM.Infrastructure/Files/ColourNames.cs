using System.Text.RegularExpressions;
using SkiaSharp;

namespace LT.ODM.Infrastructure.Files;

/// <summary>A colour read from a colorway name: the words that matched and their approximate sRGB colour.</summary>
public sealed record NamedColour(string Word, SKColor Rgb);

/// <summary>
/// Reads the main colour of a colorway name ("RICH BLUE F27", "EARTH STRATA S23/AURORA COFFEE S26/BLACK",
/// "Black 095A"). The main colour is the part before the first "/"; season codes (F27, S19) and colour codes (095A,
/// AE5S, 54F0) are dropped, and the longest known colour phrase wins ("collegiate navy" before "navy").
/// Colours are approximate (brand colour names as commonly printed); matching only needs them to be nearer the right
/// garment than the other colorways'. Names without a known colour ("multicolor 095A", "18") give null.
/// </summary>
public static partial class ColourNames
{
    private static readonly (string Phrase, uint Rgb)[] Lexicon =
    [
        ("black", 0x141414), ("utility black", 0x1C1C1C), ("aurora black", 0x1E1E1E), ("core black", 0x141414), ("blak", 0x141414),
        ("carbon", 0x3A3A3A), ("charcoal", 0x3C3C3C), ("char", 0x3C3C3C), ("onix", 0x6E6E6E), ("anthracite", 0x3E3E40),
        ("grey", 0x8C8C8C), ("gray", 0x8C8C8C), ("grey one", 0xD9D9D6), ("grey two", 0xC6C6C2), ("grey three", 0xA9A9A5),
        ("grey four", 0x8E8E8A), ("grey five", 0x6A6A68), ("grey six", 0x4C4C4C), ("dash grey", 0x9A9A9A), ("heather", 0x9C9C9C),
        ("grethr", 0x9C9C9C), ("silver", 0xC0C0C0),
        ("white", 0xF5F5F3), ("wonder white", 0xECE5D6), ("off white", 0xEEEAE0), ("owhite", 0xEEEAE0), ("chalk white", 0xEFEDE4),
        ("cream", 0xF0E6CC), ("crem", 0xF0E6CC), ("ivory", 0xF2EDDC), ("ecru", 0xE8DFC8), ("linen", 0xE6DCC8), ("wonbei", 0xD9CBB3),
        ("beige", 0xD6C4A6), ("magic beige", 0xC8B79E), ("wonder beige", 0xD9CBB3), ("sand", 0xD8C3A0), ("crystal sand", 0xD9CBB0),
        ("greige", 0xBFB5A6), ("stone", 0xB8AE9C), ("taupe", 0x8E8070), ("putty", 0xB7AA90),
        ("khaki", 0x9B8A5E), ("khaki three", 0xB0A27C), ("khaki six", 0x6E6447), ("olive", 0x6B6B3A), ("olive strata", 0x7A7454),
        ("olistr", 0x7A7454), ("seaweed", 0x737A3C), ("moss", 0x5E6B3A), ("army", 0x4B5320),
        ("green", 0x3C8C4B), ("dark green", 0x1F4D2E), ("forest", 0x23452F), ("halo green", 0xB7D8C3), ("lime", 0xA8D33A),
        ("magic lime", 0xD7E35C), ("mint", 0xB9E4CF), ("aqua", 0x7FD6D6), ("light aqua", 0xA9E1E0), ("teal", 0x1E7F7F),
        ("blue", 0x2D5DAA), ("rich blue", 0x2E4F9A), ("royal", 0x2B4DB5), ("royal blue", 0x2B4DB5), ("ash blue", 0x8BA3B7),
        ("sky", 0x8EC5E8), ("light blue", 0xA8C8E6), ("dark blue", 0x1D2A50), ("navy", 0x1F2A44), ("collegiate navy", 0x1F2747),
        ("team navy", 0x1F2747), ("team navy blue", 0x1F2747), ("night indigo", 0x2B2E4A), ("indigo", 0x3F3F7F), ("denim", 0x4A6A96),
        ("red", 0xC8202F), ("shadow red", 0x6B2A2E), ("wonder red", 0xB8313A), ("scarlet", 0xC8102E), ("burgundy", 0x6A1F2D),
        ("collegiate burgundy", 0x5E1F2B), ("maroon", 0x5A1E28), ("claret", 0x6E1F3A), ("chalk claret", 0x7E3A4E), ("wine", 0x5C1F2E),
        ("pink", 0xE9A7B8), ("pure pink", 0xF2C6D2), ("sandy pink", 0xE3B9A7), ("lilac", 0xC8B6DA), ("dual lilac", 0xB8A5CF),
        ("purple", 0x6A3E8C), ("violet", 0x7A4FA0),
        ("yellow", 0xF2D23B), ("easy yellow", 0xF3E37A), ("gold", 0xC9A23F), ("mustard", 0xC9A227), ("orange", 0xEE7F2D),
        ("brown", 0x6B4A33), ("night brown", 0x4B3A30), ("dark brown", 0x4A3426), ("dkbr", 0x4A3426), ("wild brown", 0x6B4B35),
        ("auburn", 0x7A3F2A), ("coffee", 0x5A4433), ("aurora coffee", 0x5E4A3C), ("auco", 0x5E4A3C), ("brown desert", 0x8C6F52),
        ("desert", 0xC2A57E), ("earth strata", 0x7A6A58), ("tan", 0xB58A5E), ("camel", 0xB9925F), ("mocha", 0x6B5444),
        ("chocolate", 0x4A3022), ("rust", 0x9A4A2A),
        // Short forms used in colorway names.
        ("grefou", 0x8E8E8A), ("utiblk", 0x1C1C1C), ("wilbrn", 0x6B4B35), ("crysan", 0xD9CBB0), ("mgreyh", 0x9C9C9C),
        ("blk", 0x141414), ("wht", 0xF5F5F3), ("nvy", 0x1F2A44), ("gry", 0x8C8C8C), ("brn", 0x6B4A33),
    ];

    private static readonly Dictionary<string, uint> ByPhrase = Lexicon.ToDictionary(e => e.Phrase, e => e.Rgb);
    private static readonly int LongestPhrase = Lexicon.Max(e => e.Phrase.Split(' ').Length);

    [GeneratedRegex(@"(?<=[A-Za-z])(?=[SF]\d{2})")]
    private static partial Regex GluedSeason();

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex Separators();

    public static NamedColour? Parse(string? colorwayName)
    {
        if (string.IsNullOrWhiteSpace(colorwayName)) return null;
        var main = colorwayName.Split('/')[0];
        main = GluedSeason().Replace(main, " ");
        // Words only: tokens with a digit are season or colour codes.
        var words = Separators().Split(main.ToLowerInvariant()).Where(t => t.Length > 1 && !t.Any(char.IsDigit)).ToArray();
        for (var len = Math.Min(LongestPhrase, words.Length); len >= 1; len--)
            for (var i = 0; i + len <= words.Length; i++)
            {
                var phrase = string.Join(' ', words, i, len);
                if (ByPhrase.TryGetValue(phrase, out var rgb))
                    return new NamedColour(phrase, new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            }
        return null;
    }
}
