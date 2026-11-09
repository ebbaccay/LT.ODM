using System.Text.RegularExpressions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.LibraryAssistant;

// Library assistant synonyms (ai.Synonym): words people use that the library writes differently ("tees" = product type TOP,
// "puffer" = JACKET, "pitch" = package). Before anything reads a question, each synonym in it is replaced by what the phrase
// list understands (a code, a gender word, a weave word or plain words), so saved questions, patterns, how-to guides and the
// AI all read it the same way. The answer says what was read differently ("tees" read as TOP). Admins keep the list in
// AI Studio > Library assistant > Synonyms. See docs/components/library-assistant.md.

public static class SynonymKinds
{
    public const string ProductType = "productType", Customer = "customer", Season = "season", BusinessUnit = "businessUnit",
        Gender = "gender", WeaveType = "weaveType", Words = "words";
    public static readonly string[] All = [ProductType, Customer, Season, BusinessUnit, Gender, WeaveType, Words];
}

/// <summary>Word: what people type (lower case, 1-4 words); Value: a code of the Kind's list, or plain words for Kind "words".</summary>
public sealed record SynonymDto(int SynonymId, string Word, string Kind, string Value, string? ChangedBy = null, DateTime? ChangedUtc = null);

public sealed record SaveSynonymRequest(string? Word, string? Kind, string? Value);

/// <summary>ai.usp_Synonym_* (db/procedures/ai.assistant.procedures.sql). 50400 / 50404 -> StyleRuleException.</summary>
public interface ISynonymRepository
{
    Task<IReadOnlyList<SynonymDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Adds the word, or changes what it means when it is already on the list.</summary>
    Task SaveAsync(string word, string kind, string value, string changedBy, CancellationToken ct = default);

    Task DeleteAsync(int synonymId, CancellationToken ct = default);
}

public static partial class Synonyms
{
    public const int MaxWordLength = 60;

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N}'\-/ ]{0,58}[\p{L}\p{N}]$|^[\p{L}\p{N}]$", RegexOptions.CultureInvariant)]
    private static partial Regex WordRx();

    /// <summary>Lower case, single spaces; null when it is not 1-60 letters, digits, spaces, ' - or /, or more than 4 words.</summary>
    public static string? NormalizeWord(string? word)
    {
        var w = Regex.Replace(word?.Trim().ToLowerInvariant() ?? "", @"\s+", " ");
        return w.Length is > 0 and <= MaxWordLength && WordRx().IsMatch(w) && w.Split(' ').Length <= 4 ? w : null;
    }

    /// <summary>The library's spelling of the value for its kind, or null when it is not on that list.</summary>
    public static string? CheckValue(string kind, string? value, StyleLookupsDto l)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0) return null;
        string? Known(IReadOnlyList<LookupItem> items) => items.FirstOrDefault(i => string.Equals(i.Code, v, StringComparison.OrdinalIgnoreCase))?.Code
            ?? items.FirstOrDefault(i => string.Equals(i.Name, v, StringComparison.OrdinalIgnoreCase))?.Code;
        return kind switch
        {
            SynonymKinds.ProductType => Known(l.ProductTypes),
            SynonymKinds.Customer => Known(l.Customers),
            SynonymKinds.Season => Known(l.Seasons),
            SynonymKinds.BusinessUnit => Known(l.BusinessUnits),
            SynonymKinds.WeaveType => Known(l.WeaveTypes),
            SynonymKinds.Gender => StyleValidation.Genders.FirstOrDefault(g => string.Equals(g, v, StringComparison.OrdinalIgnoreCase)),
            SynonymKinds.Words => v.Length <= 100 && !v.Any(char.IsControl) ? v : null,
            _ => null,
        };
    }

    /// <summary>What goes into the question instead of the word: text the phrase list reads as that value.</summary>
    public static string Replacement(SynonymDto s, StyleLookupsDto l) => s.Kind switch
    {
        SynonymKinds.Gender => s.Value switch { "MALE" => "men", "FEMALE" => "women", "KIDS" => "kids", _ => "unisex" },
        SynonymKinds.WeaveType => s.Value switch { "KNT" => "knit", "WVN" => "woven", _ => s.Value },
        // A business unit is read by its full name (two words or more) or a word of it.
        SynonymKinds.BusinessUnit => l.BusinessUnits.FirstOrDefault(b => b.Code == s.Value)?.Name ?? s.Value,
        _ => s.Value,
    };

    /// <summary>
    /// The question with every synonym replaced (whole words, a plural s/es allowed, longest synonyms first) and what was read
    /// differently, as "word → value" for the answer. Unchanged when no synonym is in it.
    /// </summary>
    public static (string Question, IReadOnlyList<string> ReadAs) Apply(string question, IReadOnlyList<SynonymDto> synonyms, StyleLookupsDto l)
    {
        var readAs = new List<string>();
        var text = question;
        foreach (var s in synonyms.OrderByDescending(s => s.Word.Length))
        {
            var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"\s+", s.Word.Split(' ').Select(Regex.Escape)) + @"(?:'?s|es)?(?![\p{L}\p{N}])";
            var replacement = Replacement(s, l);
            var changed = Regex.Replace(text, pattern, _ => replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (changed == text) continue;
            text = changed;
            readAs.Add($"{s.Word} → {Label(s, l)}");
        }
        return (text, readAs);
    }

    /// <summary>The value as people know it: "001 (Shirt)" when the code is not the name.</summary>
    private static string Label(SynonymDto s, StyleLookupsDto l)
    {
        var items = s.Kind switch
        {
            SynonymKinds.ProductType => l.ProductTypes, SynonymKinds.Customer => l.Customers, SynonymKinds.Season => l.Seasons,
            SynonymKinds.BusinessUnit => l.BusinessUnits, SynonymKinds.WeaveType => l.WeaveTypes, _ => null,
        };
        var name = items?.FirstOrDefault(i => i.Code == s.Value)?.Name;
        return name is null || string.Equals(name, s.Value, StringComparison.OrdinalIgnoreCase) ? s.Value : $"{s.Value} ({name})";
    }
}
