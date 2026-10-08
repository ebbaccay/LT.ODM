using System.Globalization;
using System.Text.RegularExpressions;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.StyleAi;

/// <summary>
/// Reads a search request with a phrase list instead of AI: gender, weave, season, product type, business unit, customer
/// and material words, plus a style number. The words come partly from the library's own pick lists (product types,
/// business units, customers, seasons), so a new product type is understood as soon as it is on its list.
/// A request is "complete" when every word was understood (or is a filler such as "for", "with"); only then is AI skipped.
/// Shared by Smart search and, later, the Library assistant (AI Lab): phrases it knows never need an AI call.
/// </summary>
public static partial class StyleQueryParser
{
    public sealed record Result(StyleSearchFiltersDto Filters, IReadOnlyList<string> Unmatched)
    {
        public bool HasCriteria => Filters.Search is not null || Filters.Material is not null || Filters.Customer is not null
            || Filters.Seasons.Count > 0 || Filters.BusinessUnit is not null || Filters.ProductTypes.Count > 0 || Filters.WeaveType is not null
            || Filters.Gender is not null;

        public bool Complete => HasCriteria && Unmatched.Count == 0;
    }

    // ----- Fixed words -----

    internal static readonly Dictionary<string, string> GenderWords = new(StringComparer.Ordinal)
    {
        ["men"] = "MALE", ["mens"] = "MALE", ["man"] = "MALE", ["male"] = "MALE", ["boy"] = "KIDS", ["boys"] = "KIDS",
        ["women"] = "FEMALE", ["womens"] = "FEMALE", ["woman"] = "FEMALE", ["ladies"] = "FEMALE", ["lady"] = "FEMALE", ["female"] = "FEMALE",
        ["girl"] = "KIDS", ["girls"] = "KIDS", ["kid"] = "KIDS", ["kids"] = "KIDS", ["youth"] = "KIDS", ["baby"] = "KIDS", ["children"] = "KIDS",
        ["child"] = "KIDS", ["junior"] = "KIDS", ["juniors"] = "KIDS", ["unisex"] = "UNISEX",
    };

    internal static readonly Dictionary<string, string> WeaveWords = new(StringComparer.Ordinal)
    {
        ["knit"] = "KNT", ["knits"] = "KNT", ["knitted"] = "KNT", ["woven"] = "WVN", ["wovens"] = "WVN",
    };

    /// <summary>Words as they appear on BOM lines. Several in a row must all be on the same BOM line ("recycled fleece").</summary>
    internal static readonly HashSet<string> MaterialWords = new(StringComparer.Ordinal)
    {
        "recycled", "fleece", "spandex", "elastane", "lycra", "cotton", "polyester", "nylon", "polyamide", "wool", "merino", "linen", "organic",
        "bamboo", "tencel", "modal", "viscose", "rayon", "rib", "interlock", "pique", "tricot", "taffeta", "ripstop", "softshell", "mesh", "terry",
        "velour", "twill", "denim", "canvas", "lace", "zipper", "zip", "elastic", "drawcord", "cord", "snap", "button", "velcro", "reflective",
        "tape", "padding", "insulation", "brushed", "waterproof", "stretch",
    };

    /// <summary>Season term words; codes (SS, FW, ...) come from the library's season terms.</summary>
    private static readonly Dictionary<string, string> TermWords = new(StringComparer.Ordinal)
    {
        ["spring"] = "SP", ["summer"] = "SU", ["fall"] = "FA", ["autumn"] = "FA", ["winter"] = "WI",
        ["spring/summer"] = "SS", ["spring-summer"] = "SS", ["fall/winter"] = "FW", ["fall-winter"] = "FW", ["autumn/winter"] = "FW", ["autumn-winter"] = "FW",
    };

    /// <summary>Other names for product type words (matched against the end of the product type's name).</summary>
    private static readonly Dictionary<string, string[]> ProductSynonyms = new(StringComparer.Ordinal)
    {
        ["tee"] = ["tshirt"], ["hoodie"] = ["hooded", "sweat"], ["sweater"] = ["pullover"], ["legging"] = ["tight"],
    };

    /// <summary>Product type details in brackets: "long sleeve t-shirts" keeps the T-SHIRT (LONG SLEEVE) types.</summary>
    private static readonly (string[] Words, string Key)[] Qualifiers =
    [
        (["long", "sleeve"], "LONG SLEEVE"), (["long", "sleeved"], "LONG SLEEVE"), (["long-sleeve"], "LONG SLEEVE"), (["long-sleeved"], "LONG SLEEVE"),
        (["short", "sleeve"], "SHORT SLEEVE"), (["short", "sleeved"], "SHORT SLEEVE"), (["short-sleeve"], "SHORT SLEEVE"), (["short-sleeved"], "SHORT SLEEVE"),
        (["sleeveless"], "SLEEVELESS"), (["midweight"], "MIDWEIGHT"), (["reversible"], "REVERSIBLE"),
        (["1/1"], "1/1"), (["1/2"], "1/2"), (["1/4"], "1/4"), (["3/4"], "3/4"), (["7/8"], "7/8"),
    ];

    /// <summary>Words that carry no criterion. Words that change the meaning ("without", "not", "except") are not here: they need AI.</summary>
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "for", "with", "that", "which", "who", "use", "uses", "using", "used", "made", "of", "from", "in", "on", "and", "or", "all",
        "any", "some", "show", "me", "find", "list", "get", "give", "search", "styles", "style", "garment", "garments", "item", "items", "product",
        "products", "apparel", "have", "has", "having", "contain", "contains", "containing", "fabric", "fabrics", "material", "materials", "trim",
        "trims", "season", "seasons", "collection", "is", "are", "i", "we", "need", "want", "looking", "please", "every", "only", "range", "line",
        "lines", "by", "to", "at", "our", "their", "what", "do", "you", "there",
    };

    [GeneratedRegex(@"[a-z0-9]+(?:[-/'][a-z0-9]+)*'?", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRx();

    [GeneratedRegex(@"^(sp|ss|su|fa|fw|wi)(\d{2}|\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex TermYearRx();

    [GeneratedRegex(@"^(\d{2}|\d{4})(sp|ss|su|fa|fw|wi)$", RegexOptions.CultureInvariant)]
    private static partial Regex YearTermRx();

    [GeneratedRegex(@"^(\d{4})-([a-z]{2,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonCodeRx();

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex BracketRx();

    public static Result Parse(string query, StyleLookupsDto lookups)
    {
        var tokens = Tokens(query);
        var used = new bool[tokens.Count];
        var unmatched = new List<string>();

        // Seasons first: "ss27" would otherwise look like a style number.
        var seasons = ReadSeasons(tokens, used, lookups);

        // Business units by their full name ("alo yoga", "football licensed"), before single words are taken.
        var businessUnits = new List<string>();
        foreach (var bu in lookups.BusinessUnits)
        {
            var name = Words(bu.Name);
            if (name.Length >= 2 && FindRun(tokens, used, name) is { } at)
            {
                Mark(used, at, name.Length);
                businessUnits.Add(bu.Code);
            }
        }

        var productTypes = ReadProductTypes(tokens, used, lookups.ProductTypes, unmatched);

        string? gender = null, weave = null, customer = null, search = null;
        var materials = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i]) continue;
            var t = tokens[i];
            if (GenderWords.TryGetValue(t, out var g)) gender = One(gender, g, t, unmatched);
            else if (WeaveWords.TryGetValue(t, out var w)) weave = One(weave, w, t, unmatched);
            else if (MaterialWords.Contains(t) || MaterialWords.Contains(Singular(t))) materials.Add(MaterialWords.Contains(t) ? t : Singular(t));
            else if (CustomerFor(t, lookups.Customers) is { } c) customer = One(customer, c, t, unmatched);   // before units: "adidas" is the customer
            else if (BusinessUnitsFor(t, lookups.BusinessUnits) is { Count: > 0 } bus) businessUnits.AddRange(bus);
            else if (Fillers.Contains(t)) { }
            else if (LooksLikeStyleNumber(t) && search is null) search = t.ToUpperInvariant();
            else unmatched.Add(t);
            used[i] = true;
        }

        var filters = new StyleSearchFiltersDto(
            Search: search,
            Material: materials.Count == 0 ? null : string.Join(' ', materials.Distinct().Select(m => m.ToUpperInvariant())),
            Customer: customer,
            Seasons: seasons,
            BusinessUnit: businessUnits.Count == 0 ? null : string.Join(',', businessUnits.Distinct()),
            ProductTypes: productTypes,
            WeaveType: weave is not null && lookups.WeaveTypes.Count > 0 && !lookups.WeaveTypes.Any(x => x.Code == weave) ? null : weave,
            Gender: gender);
        return new Result(filters, unmatched.Distinct().ToList());
    }

    /// <summary>A second different value for a single-value criterion ("men and women") is left to AI.</summary>
    private static string? One(string? current, string value, string word, List<string> unmatched)
    {
        if (current is null || current == value) return value;
        unmatched.Add(word);
        return current;
    }

    // ----- Seasons -----

    private static IReadOnlyList<string> ReadSeasons(List<string> tokens, bool[] used, StyleLookupsDto l)
    {
        var terms = l.SeasonTerms.Select(t => t.Code.ToLowerInvariant()).ToHashSet();
        if (terms.Count == 0) terms = ["sp", "ss", "su", "fa", "fw", "wi"];
        var codes = new List<string>();
        var years = new List<int>();
        var looseTerms = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            Match m;
            if ((m = TermYearRx().Match(t)).Success && terms.Contains(m.Groups[1].Value)) { codes.Add(Code(Year(m.Groups[2].Value), m.Groups[1].Value)); used[i] = true; }
            else if ((m = YearTermRx().Match(t)).Success && terms.Contains(m.Groups[2].Value)) { codes.Add(Code(Year(m.Groups[1].Value), m.Groups[2].Value)); used[i] = true; }
            else if ((m = SeasonCodeRx().Match(t)).Success && terms.Contains(m.Groups[2].Value)) { codes.Add(Code(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value)); used[i] = true; }
            else if (t.Length == 4 && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 2000 and <= 2099) { years.Add(y); used[i] = true; }
            else if (t.Length == 2 && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var yy) && i > 0 && used[i - 1]
                     && (terms.Contains(tokens[i - 1]) || TermWords.ContainsKey(tokens[i - 1]))) { years.Add(2000 + yy); used[i] = true; }   // "ss 27"
            else if (terms.Contains(t)) { looseTerms.Add(t.ToUpperInvariant()); used[i] = true; }
            else if (i + 1 < tokens.Count && TermWords.TryGetValue(t + "/" + tokens[i + 1], out var pair)) { looseTerms.Add(pair); used[i] = used[i + 1] = true; i++; }
            else if (TermWords.TryGetValue(t, out var term)) { looseTerms.Add(term); used[i] = true; }
        }

        if (years.Count > 0)
        {
            var yearTerms = looseTerms.Count > 0 ? looseTerms : l.SeasonTerms.Select(t => t.Code).DefaultIfEmpty("SS").ToList();
            codes.AddRange(years.SelectMany(y => yearTerms.Select(t => Code(y, t))));
        }
        else if (looseTerms.Count > 0)
            codes.AddRange(l.Seasons.Select(s => s.Code).Where(c => looseTerms.Any(t => c.EndsWith("-" + t, StringComparison.OrdinalIgnoreCase))));
        return codes.Distinct().ToList();

        static int Year(string v) => v.Length == 2 ? 2000 + int.Parse(v, CultureInfo.InvariantCulture) : int.Parse(v, CultureInfo.InvariantCulture);
        static string Code(int year, string term) => $"{year}-{term.ToUpperInvariant()}";
    }

    // ----- Product types -----

    /// <summary>
    /// A request word (or 2-3 words) matches every product type whose name ends with it, singular or plural:
    /// "jackets" = JACKET, JACKETS, JACKET (MIDWEIGHT), TRACKSUIT JACKET; "track tops" = TRACK TOP, HOODED TRACK TOP.
    /// The longest match wins. Bracket details ("long sleeve", "7/8") then narrow the list.
    /// </summary>
    private static List<string> ReadProductTypes(List<string> tokens, bool[] used, IReadOnlyList<LookupItem> types, List<string> unmatched)
    {
        if (types.Count == 0) return [];
        var named = types.Select(t => (t.Code, Words: Words(BracketRx().Replace(t.Name, " ")).Select(Singular).ToArray(), Detail: Detail(t.Name))).ToList();
        var found = new List<(string Code, string Detail)>();

        // Bracket details first, so "short sleeve" is not read as shorts.
        var details = new List<(string Words, string Key)>();
        foreach (var (words, key) in Qualifiers)
            while (FindRun(tokens, used, words) is { } at)
            {
                Mark(used, at, words.Length);
                details.Add((string.Join(' ', words), key));
            }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i]) continue;
            for (var n = Math.Min(3, tokens.Count - i); n >= 1; n--)
            {
                if (Enumerable.Range(i, n).Any(k => used[k])) continue;
                var words = tokens.Skip(i).Take(n).Select(w => Singular(Plain(w))).ToArray();
                if (n == 1 && MaterialWords.Contains(words[0])) break;   // "zip" is a trim, not the half-zip product type
                var synonym = words.SelectMany(w => ProductSynonyms.TryGetValue(w, out var syn) ? syn : [w]).ToArray();
                var hits = named.Where(p => EndsWith(p.Words, words) || EndsWith(p.Words, synonym)).ToList();
                if (hits.Count == 0) continue;
                found.AddRange(hits.Select(h => (h.Code, h.Detail)));
                Mark(used, i, n);
                break;
            }
            // A product type code typed as is ("PANTS1/1"), when no name matched.
            if (!used[i] && types.FirstOrDefault(t => string.Equals(t.Code, tokens[i], StringComparison.OrdinalIgnoreCase)) is { } code)
            {
                found.Add((code.Code, Detail(code.Name)));
                used[i] = true;
            }
        }

        // Details narrow the product types found; a detail without a product type (or that none of them has) is not understood.
        foreach (var (words, key) in details)
        {
            var narrowed = found.Where(f => f.Detail.Contains(key, StringComparison.Ordinal)).ToList();
            if (narrowed.Count > 0) found = narrowed;
            else unmatched.Add(words);
        }
        return found.Select(f => f.Code).Distinct().ToList();

        static bool EndsWith(string[] name, string[] phrase) => name.Length >= phrase.Length && name[^phrase.Length..].SequenceEqual(phrase);

        static string Detail(string name) => string.Join(' ', BracketRx().Matches(name).Select(m => m.Value.ToUpperInvariant()))
            .Replace("SLEEVED", "SLEEVE", StringComparison.Ordinal);
    }

    // ----- Business units and customers -----

    /// <summary>"running" = every business unit whose name starts with Running (RUA, RUB, RUX); a code matches itself.</summary>
    private static List<string> BusinessUnitsFor(string word, IReadOnlyList<LookupItem> units)
    {
        var byCode = units.Where(u => string.Equals(u.Code, word, StringComparison.OrdinalIgnoreCase)).Select(u => u.Code).ToList();
        if (byCode.Count > 0 || word.Length < 4 || Fillers.Contains(word)) return byCode;
        return units.Where(u => Words(u.Name) is { Length: > 0 } w && (w[0] == word || Singular(w[0]) == Singular(word))).Select(u => u.Code).ToList();
    }

    /// <summary>Code or name; a longer word starting with a 3+ letter code also counts ("adidas" = ADI, "skechers" = SKE).</summary>
    private static string? CustomerFor(string word, IReadOnlyList<LookupItem> customers)
        => customers.FirstOrDefault(c => string.Equals(c.Code, word, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, word, StringComparison.OrdinalIgnoreCase))?.Code
           ?? (word.Length >= 5 && !MaterialWords.Contains(word)
               ? customers.FirstOrDefault(c => c.Code.Length >= 3 && word.StartsWith(c.Code, StringComparison.OrdinalIgnoreCase))?.Code
               : null);

    private static bool LooksLikeStyleNumber(string t) => t.Length >= 5 && t.Any(char.IsDigit) && t.Any(char.IsLetter) && !t.Contains('\'');

    // ----- Phrase list (for the screen) -----

    /// <summary>The words understood without AI, by criterion. Product types and business units come from the library's lists.</summary>
    public static IReadOnlyList<StyleSearchPhraseGroupDto> Vocabulary(StyleLookupsDto l)
    {
        static IReadOnlyList<string> Sorted(IEnumerable<string> words) => words.Distinct().Order(StringComparer.Ordinal).ToList();
        // Product type names without the bracket details, last word singular: "T-SHIRTS" and "T-SHIRT (LONG SLEEVE)" are both "t-shirt".
        var productWords = l.ProductTypes.Select(p => BracketRx().Replace(p.Name, " ").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length > 0).Select(w => string.Join(' ', w[..^1].Append(Singular(w[^1]) is var one && AlwaysPlural.Contains(one) ? one + "s" : one)));
        return
        [
            new("gender", ["men's", "women's", "ladies", "kids", "youth", "boys", "girls", "unisex"]),
            new("season", ["SS27", "FW26", "2027 SS", "2027-SS", "2027", "spring", "summer", "fall/winter"]),
            new("productType", Sorted(productWords.Concat(["tee", "hoodie", "leggings"]))),
            new("detail", ["long sleeve", "short sleeve", "sleeveless", "midweight", "1/2", "3/4", "7/8"]),
            new("weave", ["knit", "woven"]),
            // A unit's first word, unless it is read as a gender or a customer first ("kids", "adidas").
            new("businessUnit", Sorted(l.BusinessUnits.Select(b => Words(b.Name) is { Length: > 0 } w && w[0].Length >= 4 ? w[0] : b.Code.ToLowerInvariant())
                .Where(w => !GenderWords.ContainsKey(w) && CustomerFor(w, l.Customers) is null))),
            new("customer", Sorted(l.Customers.Select(c => c.Code))),
            new("material", Sorted(MaterialWords)),
            new("styleNo", ["S2808MR0000"]),
        ];
    }

    // ----- Words -----

    private static readonly HashSet<string> AlwaysPlural = new(StringComparer.Ordinal) { "pant", "short", "tight", "legging", "jogger", "other" };

    private static List<string> Tokens(string query)
        => TokenRx().Matches(query.ToLowerInvariant().Replace('’', '\'').Replace('‘', '\''))
            .Select(m => m.Value.EndsWith("'s", StringComparison.Ordinal) ? m.Value[..^2] : m.Value.TrimEnd('\''))
            .Where(t => t.Length > 0).ToList();

    /// <summary>A name as lower-case words: "T-SHIRT (SHORT SLEEVE)" -> tshirt, short, sleeve; "YOUTH/BABY JOGGER" -> youth, baby, jogger.</summary>
    private static string[] Words(string name)
        => name.ToLowerInvariant().Split([' ', '/', '(', ')', ',', '.', '&'], StringSplitOptions.RemoveEmptyEntries).Select(Plain).Where(w => w.Length > 0).ToArray();

    private static string Plain(string word) => word.Replace("-", "", StringComparison.Ordinal).Replace("'", "", StringComparison.Ordinal);

    /// <summary>jackets -> jacket, dresses -> dress, shorts -> short; "dress" and fractions stay.</summary>
    internal static string Singular(string w)
        => w.EndsWith("sses", StringComparison.Ordinal) ? w[..^2]
         : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) ? w[..^1]
         : w;

    private static int? FindRun(List<string> tokens, bool[] used, string[] words)
    {
        for (var i = 0; i + words.Length <= tokens.Count; i++)
            if (Enumerable.Range(0, words.Length).All(k => !used[i + k] && (tokens[i + k] == words[k] || Plain(Singular(tokens[i + k])) == Singular(words[k]))))
                return i;
        return null;
    }

    private static void Mark(bool[] used, int at, int count)
    {
        for (var k = at; k < at + count; k++) used[k] = true;
    }
}
