using System.Text.RegularExpressions;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using static LT.ODM.Application.StyleAi.StyleQueryParser;

namespace LT.ODM.Application.LibraryAssistant;

/// <summary>
/// Saved question patterns (ai.SavedQuestion.IsPattern, seeded by db/seed/ai.saved-questions.sql): a question with its filter
/// words taken out, so one pattern answers every customer and season. "Who are our main suppliers for ADI in 2027-SS?" ->
/// "who are main supplier"; the tool then runs with the filters the phrase list read (customer ADI, season 2027-SS), but only
/// those the tool takes. Style numbers and material codes the library has become slots: "what fabric does 26FA_M_JK01 use" ->
/// "what fabric doe {styleno} use". Codes are found in the question as typed (the phrase list splits "26FA_M_JK01" and reads
/// "26fa" as a season), checked by the caller, and replaced by placeholder words before the phrase list reads the rest.
/// </summary>
public static partial class QuestionPatterns
{
    public const string StyleSlot = "{styleNo}", FromStyleSlot = "{fromStyleNo}", MaterialSlot = "{materialCode}";

    /// <summary>Placeholder words for the slots: letters only, so the phrase list leaves them as unknown words.</summary>
    private static readonly Dictionary<string, string> Placeholders = new(StringComparer.Ordinal)
    {
        [StyleSlot] = "qqstyleno", [FromStyleSlot] = "qqfromstyleno", [MaterialSlot] = "qqmaterialcode",
    };

    /// <summary>The words that join filters to the question; taken out with them ("for ADI in 2027-SS").</summary>
    private static readonly HashSet<string> Joining = new(StringComparer.Ordinal) { "for", "in", "of", "on", "from", "at", "with", "by", "our", "we", "do", "the" };

    /// <summary>Filter words and the tool argument each one fills.</summary>
    private static readonly Dictionary<string, string> FilterArgument = new(StringComparer.Ordinal)
    {
        [WordKinds.Customer] = "customer", [WordKinds.Season] = "seasons", [WordKinds.ProductType] = "productTypes",
        [WordKinds.BusinessUnit] = "businessUnits", [WordKinds.Gender] = "gender", [WordKinds.Weave] = "weaveType",
    };

    /// <summary>Counting by a dimension ignores a filter on it ("men vs women styles" counts every gender).</summary>
    private static readonly Dictionary<string, string> GroupFilter = new(StringComparer.Ordinal)
    {
        ["season"] = "seasons", ["customer"] = "customer", ["productType"] = "productTypes", ["businessUnit"] = "businessUnits",
        ["gender"] = "gender", ["weaveType"] = "weaveType",
    };

    /// <summary>
    /// Key: the filter words taken out. KeyKeepingProductTypes: the same with product type words kept as words ("top suppliers":
    /// TOP is also a product type). Slots: slot -> code. Filters: the arguments the removed words fill, for each key.
    /// Read: what the phrase list read from the question with the codes replaced.
    /// </summary>
    public sealed record Reading(
        string? Key, string? KeyKeepingProductTypes, IReadOnlyDictionary<string, string> Slots,
        IReadOnlySet<string> Filters, IReadOnlySet<string> FiltersKeepingProductTypes, StyleSearchFiltersDto Read);

    [GeneratedRegex(@"[^\s,;!?""]+", RegexOptions.CultureInvariant)]
    private static partial Regex RawWordRx();

    [GeneratedRegex(@"^(\d{4}|(sp|ss|su|fa|fw|wi)\d{2}(\d{2})?|\d{2}(\d{2})?(sp|ss|su|fa|fw|wi)|\d{4}-[a-z]{2,4})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SeasonOrYearRx();

    /// <summary>Words as typed that could be a style number or a material code: 5+ characters with a digit, not a season or year.</summary>
    public static IReadOnlyList<string> Codes(string question)
        => RawWordRx().Matches(question).Select(m => m.Value.Trim('.', ':', '\'')).Where(w => w.Length >= 5 && w.Any(char.IsDigit) && !SeasonOrYearRx().IsMatch(w))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>codeKinds: code as typed -> "styleNo" or "materialCode" (codes the library does not have are left out).</summary>
    public static Reading Read(string question, IReadOnlyDictionary<string, string> codeKinds, StyleLookupsDto lookups)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = question;
        foreach (var code in Codes(question))
        {
            if (!codeKinds.TryGetValue(code, out var kind)) continue;
            var slot = kind == "materialCode" ? (slots.ContainsKey(MaterialSlot) ? null : MaterialSlot)
                : !slots.ContainsKey(StyleSlot) ? StyleSlot : !slots.ContainsKey(FromStyleSlot) ? FromStyleSlot : null;
            if (slot is null) continue;
            slots[slot] = code;
            text = Regex.Replace(text, @"(?<![\w-])" + Regex.Escape(code) + @"(?![\w-])", " " + Placeholders[slot] + " ", RegexOptions.IgnoreCase);
        }
        var (parsed, words) = ParseWords(text, lookups);
        var slotOf = Placeholders.ToDictionary(p => p.Value, p => p.Key.ToLowerInvariant());

        string? Key(bool keepProductTypes, HashSet<string> filters)
        {
            var kept = new List<string>();
            foreach (var w in words)
            {
                if (slotOf.TryGetValue(w.Text, out var slot)) kept.Add(slot);
                else if (FilterArgument.TryGetValue(w.Kind, out var argument) && !(keepProductTypes && w.Kind == WordKinds.ProductType)) filters.Add(argument);
                else if (!Joining.Contains(w.Text)) kept.Add(Singular(w.Text));
            }
            var key = string.Join(' ', kept);
            return key.Length == 0 ? null : key.Length > 300 ? key[..300] : key;
        }

        var filters = new HashSet<string>(StringComparer.Ordinal);
        var filtersKeeping = new HashSet<string>(StringComparer.Ordinal);
        return new Reading(Key(false, filters), Key(true, filtersKeeping), slots, filters, filtersKeeping, parsed.Filters);
    }

    /// <summary>
    /// A pattern's arguments for this question: its own (slots filled in, unfilled slots left out) plus the filters the phrase
    /// list read, for the arguments the tool takes. Null when the question has a filter the tool does not take ("main suppliers
    /// for women"): that is a different question, for the AI.
    /// </summary>
    public static LibraryToolArgs? Apply(
        IReadOnlyDictionary<string, string> patternArguments, bool matchedKeepingProductTypes, Reading reading, IReadOnlyList<string> parameters,
        StyleLookupsDto lookups)
    {
        var asked = (matchedKeepingProductTypes ? reading.FiltersKeepingProductTypes : reading.Filters).ToHashSet(StringComparer.Ordinal);
        if (patternArguments.TryGetValue("groupBy", out var groupBy) && GroupFilter.TryGetValue(groupBy, out var grouped)) asked.Remove(grouped);
        if (asked.Any(a => !parameters.Contains(a))) return null;

        var own = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in patternArguments)
        {
            var filled = value;
            foreach (var (slot, code) in reading.Slots) filled = filled.Replace(slot, code, StringComparison.Ordinal);
            if (!filled.Contains('{')) own[name] = filled;
        }

        // Only the filters taken out of the question; material words and looks stay part of the pattern.
        var f = reading.Read;
        var read = new StyleSearchFiltersDto(
            Search: null, Material: null,
            Customer: asked.Contains("customer") ? f.Customer : null,
            Seasons: asked.Contains("seasons") ? f.Seasons : [],
            BusinessUnit: asked.Contains("businessUnits") ? f.BusinessUnit : null,
            ProductTypes: asked.Contains("productTypes") ? f.ProductTypes : [],
            WeaveType: asked.Contains("weaveType") ? f.WeaveType : null,
            Gender: asked.Contains("gender") ? f.Gender : null,
            Looks: []);
        return LibraryToolArgs.FromDisplay(own, lookups).Fill(read, parameters);
    }
}
