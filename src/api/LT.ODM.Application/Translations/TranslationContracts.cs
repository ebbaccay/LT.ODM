using System.Text.RegularExpressions;

namespace LT.ODM.Application.Translations;

/// <summary>
/// Bound from "Translations". The deployed files (BaseFolder, default wwwroot/assets/i18n) are the base; corrections
/// made in Settings > Translations are kept in OverridesFolder (default App_Data/i18n, outside the web root, so a
/// release does not wipe them) and layered on top.
/// </summary>
public sealed class TranslationOptions
{
    public const string SectionName = "Translations";

    /// <summary>Folder holding the deployed &lt;lang&gt;.json files. Relative paths start at the API folder.</summary>
    public string BaseFolder { get; set; } = "";

    /// <summary>Folder for overrides.json and its history. Relative paths start at the API folder.</summary>
    public string OverridesFolder { get; set; } = "";

    /// <summary>Earlier versions kept in OverridesFolder/history for Restore.</summary>
    public int HistoryCount { get; set; } = 30;
}

public sealed record TranslationLanguageDto(string Code, string Label);

/// <summary>One key: the deployed text per language, the corrections per language, and languages whose deployed text changed since the correction was made.</summary>
public sealed record TranslationRowDto(
    string Key,
    IReadOnlyDictionary<string, string?> Base,
    IReadOnlyDictionary<string, string> Overrides,
    IReadOnlyList<string> BaseChanged);

public sealed record TranslationTableDto(
    IReadOnlyList<TranslationLanguageDto> Languages,
    IReadOnlyList<TranslationRowDto> Rows,
    string? UpdatedBy,
    DateTime? UpdatedUtc);

/// <summary>Value null, blank or equal to the deployed text = use the deployed text (removes the correction).</summary>
public sealed record SetTranslationRequest(string Lang, string Key, string? Value);

public sealed record TranslationChangeDto(int RowNo, string Key, string Lang, string? From, string? To, bool Revert);

/// <summary>Severity Error (the cell is skipped) or Warning (the row is skipped, nothing wrong with the file).</summary>
public sealed record TranslationProblemDto(int RowNo, string? Key, string? Lang, string Severity, string Message);

public sealed record TranslationImportResultDto(
    bool Applied,
    int Rows,
    IReadOnlyList<string> Languages,
    IReadOnlyList<TranslationChangeDto> Changes,
    IReadOnlyList<TranslationProblemDto> Problems,
    int ProblemCount);

/// <summary>A saved version of the corrections; Id "current" is the one in use.</summary>
public sealed record TranslationVersionDto(string Id, DateTime? SavedUtc, string? SavedBy, string? Source, int Overrides, bool Current);

/// <summary>A correction: the text to show, and the deployed text when it was made (to spot later releases that changed it).</summary>
public sealed record TranslationOverride(string Value, string? Base);

/// <summary>The corrections file (OverridesFolder/overrides.json): language -> key -> correction.</summary>
public sealed class TranslationOverrides
{
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, Dictionary<string, TranslationOverride>> Languages { get; set; } = new(StringComparer.Ordinal);

    public int Count => Languages.Values.Sum(l => l.Count);
}

/// <summary>Rows written to / read from the translations workbook.</summary>
public sealed record TranslationSheetRow(int RowNo, string Key, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Customised);

public sealed record TranslationSheet(IReadOnlyList<TranslationLanguageDto> Languages, IReadOnlyList<TranslationSheetRow> Rows);

/// <summary>Languages = the language columns found (codes); null when the sheet could not be used (see Problems).</summary>
public sealed record TranslationSheetReadResult(IReadOnlyList<string>? Languages, IReadOnlyList<TranslationSheetRow> Rows, IReadOnlyList<string> Problems);

public sealed class TranslationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static partial class TranslationRules
{
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxRows = 20_000;
    public const int MaxValueLength = 4000;
    /// <summary>Problems listed in an upload preview (the total is still counted).</summary>
    public const int MaxProblemsListed = 200;

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",
        ["zh-Hans"] = "Chinese (Simplified)",
        ["zh-Hant"] = "Chinese (Traditional)",
        ["vi"] = "Vietnamese",
        ["id"] = "Indonesian",
        ["th"] = "Thai",
        ["km"] = "Khmer",
    };

    public static string Label(string code) => Labels.TryGetValue(code, out var label) ? label : code;

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    public static partial Regex LanguageCode();

    [GeneratedRegex(@"\{\{\s*([\w.]+)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>The {{name}} placeholders in a text, sorted and distinct.</summary>
    public static IReadOnlyList<string> Placeholders(string? text)
        => text is null ? [] : Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>Null when the text is fine as a correction; otherwise why not. Reference = the English deployed text.</summary>
    public static string? Check(string value, string? reference)
    {
        if (value.Length > MaxValueLength) return $"The text is longer than {MaxValueLength:N0} characters.";
        var expected = Placeholders(reference);
        var actual = Placeholders(value);
        if (!expected.SequenceEqual(actual))
        {
            var wanted = expected.Count == 0 ? "no placeholders" : string.Join(", ", expected.Select(p => "{{" + p + "}}"));
            return $"The placeholders must stay as they are ({wanted}). The app fills them in, e.g. {{{{name}}}} becomes the user's name.";
        }
        return null;
    }

    /// <summary>Blank = null; Windows line breaks become \n.</summary>
    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>Same text, ignoring spaces at the ends (a stray space in Excel is not a correction).</summary>
    public static bool SameText(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);
}
