using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;
using static LT.ODM.Application.Ai.AiJson;

namespace LT.ODM.Application.LibraryAssistant;

/// <summary>
/// The arguments a tool can take, checked: codes are kept only when they are on the library's lists, texts are clipped.
/// A new kind of argument (e.g. a cost range for a costing tool) = a property here, its line in Catalog, Describe and Read.
/// </summary>
public sealed class LibraryToolArgs
{
    /// <summary>Every argument name, with what it means (goes into the AI's instructions).</summary>
    public static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>
    {
        ["customer"] = "customer code",
        ["seasons"] = "season codes (YEAR-TERM)",
        ["productTypes"] = "product type codes; include every code that fits",
        ["businessUnits"] = "business unit codes",
        ["gender"] = "MALE, FEMALE, KIDS or UNISEX",
        ["weaveType"] = "weave code",
        ["material"] = "material words as written on a BOM (e.g. RECYCLED FLEECE, ZIPPER)",
        ["keywords"] = "a model name or colorway code",
        ["styleNo"] = "a style number as written",
        ["fromStyleNo"] = "the second style number to compare with",
        ["materialCode"] = "a material code as written",
        ["groupBy"] = "what to count by: " + string.Join(", ", StyleCountGroups.All),
        ["looks"] = "what the garment looks like in its pictures, as tags (hood:hooded, closure:fullZip, pattern:striped, colour:navy); only styles with pictures have them",
        ["look"] = "a short description of how the garment looks, in the question's words (e.g. navy hooded windbreaker with contrast zips)",
    };

    public StyleLookupsDto Lookups { get; private init; } = null!;
    public string? Customer { get; private init; }
    public IReadOnlyList<string> Seasons { get; private init; } = [];
    public IReadOnlyList<string> ProductTypes { get; private init; } = [];
    public IReadOnlyList<string> BusinessUnits { get; private init; } = [];
    public string? Gender { get; private init; }
    public string? WeaveType { get; private init; }
    public string? Material { get; private init; }
    public string? Keywords { get; private init; }
    public string? StyleNo { get; private init; }
    public string? FromStyleNo { get; private init; }
    public string? MaterialCode { get; private init; }
    public string? GroupBy { get; private init; }
    public IReadOnlyList<string> Looks { get; private init; } = [];
    public string? Look { get; private init; }

    /// <summary>The schema of one argument (codes limited to the library's lists).</summary>
    public static JsonObject Schema(string name, StyleLookupsDto l) => name switch
    {
        "customer" => Str(null, l.Customers.Select(c => c.Code)),
        "seasons" => Arr(Str(null, l.Seasons.Select(c => c.Code))),
        "productTypes" => Arr(Str(null, l.ProductTypes.Select(c => c.Code))),
        "businessUnits" => Arr(Str(null, l.BusinessUnits.Select(c => c.Code))),
        "gender" => Str(null, StyleValidation.Genders),
        "weaveType" => Str(null, l.WeaveTypes.Select(c => c.Code)),
        "groupBy" => Str(null, StyleCountGroups.All),
        "looks" => Arr(Str(null, PictureTagVocabulary.Codes)),
        _ => Str(),
    };

    /// <summary>Reads the AI's arguments; anything not on the library's lists is dropped.</summary>
    public static LibraryToolArgs Read(JsonElement e, StyleLookupsDto l)
    {
        var genders = StyleValidation.Genders.Select(g => new LookupItem(g, g)).ToList();
        return new LibraryToolArgs
        {
            Lookups = l,
            Customer = Known(Text(e, "customer", 60), l.Customers),
            Seasons = KnownAll(e, "seasons", l.Seasons),
            ProductTypes = KnownAll(e, "productTypes", l.ProductTypes),
            BusinessUnits = KnownAll(e, "businessUnits", l.BusinessUnits),
            Gender = Known(Text(e, "gender", 20), genders),
            WeaveType = Known(Text(e, "weaveType", 20), l.WeaveTypes),
            Material = Words(Text(e, "material", 60)),
            Keywords = Words(Text(e, "keywords", 60)),
            StyleNo = Words(Text(e, "styleNo", 60)),
            FromStyleNo = Words(Text(e, "fromStyleNo", 60)),
            MaterialCode = Words(Text(e, "materialCode", 64)),
            GroupBy = StyleCountGroups.All.FirstOrDefault(g => string.Equals(g, Text(e, "groupBy", 20), StringComparison.OrdinalIgnoreCase)),
            Looks = KnownAll(e, "looks", LookItems),
            Look = Words(Text(e, "look", 120)),
        };
    }

    private static readonly IReadOnlyList<LookupItem> LookItems = PictureTagVocabulary.Codes.Select(c => new LookupItem(c, c)).ToList();

    private static readonly HashSet<string> ListArguments = ["seasons", "productTypes", "businessUnits", "looks"];

    /// <summary>
    /// Arguments from their text form (Display(), as saved with a question or corrected by an Admin), checked like the AI's:
    /// unknown names and codes not on the library's lists are dropped.
    /// </summary>
    public static LibraryToolArgs FromDisplay(IReadOnlyDictionary<string, string> values, StyleLookupsDto l)
    {
        var o = new JsonObject();
        foreach (var (name, value) in values)
        {
            if (!Catalog.ContainsKey(name) || string.IsNullOrWhiteSpace(value)) continue;
            o[name] = ListArguments.Contains(name)
                ? new JsonArray(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => (JsonNode)JsonValue.Create(v)).ToArray())
                : JsonValue.Create(value.Trim());
        }
        using var doc = JsonDocument.Parse(o.ToJsonString());
        return Read(doc.RootElement, l);
    }

    /// <summary>Arguments from the phrase list's filters (no AI).</summary>
    public static LibraryToolArgs FromFilters(StyleSearchFiltersDto f, StyleLookupsDto l) => new()
    {
        Lookups = l,
        Customer = f.Customer,
        Seasons = f.Seasons,
        ProductTypes = f.ProductTypes,
        BusinessUnits = f.BusinessUnit?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
        Gender = f.Gender,
        WeaveType = f.WeaveType,
        Material = f.Material,
        StyleNo = f.Search,
        Looks = f.Looks ?? [],
    };

    /// <summary>
    /// Keeps only arguments the question (or an earlier turn) supports: a small model sometimes adds a customer or every season
    /// nobody asked for, which would answer a different question. Supported = found by the phrase list, written in the question,
    /// or used in an earlier turn. Dropped holds the names of the arguments removed.
    /// </summary>
    public LibraryToolArgs Ground(string question, IReadOnlyList<LibraryTurnDto> history, StyleQueryParser.Result parsed, out IReadOnlyList<string> dropped)
    {
        var q = " " + string.Join(' ', Regex.Split(question.ToLowerInvariant(), @"[^\p{L}\p{N}/\-_]+")) + " ";
        var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(StyleQueryParser.Singular).ToHashSet();
        var earlier = history.SelectMany(h => h.Arguments?.Values ?? []).SelectMany(v => v.Split(", ")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var f = parsed.Filters;
        var removed = new List<string>();

        bool Said(string? text) => text is not null && (q.Contains(" " + text.ToLowerInvariant() + " ", StringComparison.Ordinal) || earlier.Contains(text));
        bool AllWordsSaid(string? text) => text is not null && (earlier.Contains(text)
            || text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => q.Contains(w, StringComparison.Ordinal)));
        bool NameSaid(string code, IReadOnlyList<LookupItem> items) => Said(code)
            || items.FirstOrDefault(i => i.Code == code) is { } item && Regex.Replace(item.Name, @"\([^)]*\)", " ").ToLowerInvariant()
                .Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).Any(w => qWords.Contains(StyleQueryParser.Singular(w)));
        T? Keep<T>(string name, T? value, bool supported) where T : class
        {
            if (value is null || supported) return value;
            removed.Add(name);
            return null;
        }
        IReadOnlyList<string> KeepAll(string name, IReadOnlyList<string> codes, Func<string, bool> supported)
        {
            var kept = codes.Where(supported).ToList();
            if (kept.Count < codes.Count) removed.Add(name);
            return kept;
        }

        // Seasons: those the phrase list read, or written as codes; a relative "this year" / "next season" is left to the model.
        var relative = Regex.IsMatch(q, @" (this|next|last|current|coming|previous) (year|season)");
        // A look tag is supported when one of its words is in the question ("hooded", "navy", "full zip").
        bool LookSaid(string tag) => (f.Looks?.Contains(tag) ?? false) || earlier.Contains(tag)
            || PictureTagVocabulary.Phrases.Any(p => p.Tag == tag && q.Contains(" " + string.Join(' ', p.Words) + " ", StringComparison.Ordinal));
        var grounded = new LibraryToolArgs
        {
            Lookups = Lookups,
            Customer = Keep("customer", Customer, Customer is not null && (Customer == f.Customer || NameSaid(Customer, Lookups.Customers))),
            Seasons = relative ? Seasons : KeepAll("seasons", Seasons, s => f.Seasons.Contains(s) || Said(s)),
            ProductTypes = KeepAll("productTypes", ProductTypes, p => f.ProductTypes.Contains(p) || NameSaid(p, Lookups.ProductTypes)),
            BusinessUnits = KeepAll("businessUnits", BusinessUnits, b => (f.BusinessUnit?.Split(',').Contains(b) ?? false) || NameSaid(b, Lookups.BusinessUnits)),
            Gender = Keep("gender", Gender, Gender is not null && (Gender == f.Gender || Said(Gender))),
            WeaveType = Keep("weaveType", WeaveType, WeaveType is not null && (WeaveType == f.WeaveType || NameSaid(WeaveType, Lookups.WeaveTypes)
                || (WeaveType == "KNT" && q.Contains(" jersey", StringComparison.Ordinal)))),
            Material = Keep("material", Material, AllWordsSaid(Material)),
            Keywords = Keep("keywords", Keywords, AllWordsSaid(Keywords)),
            StyleNo = Keep("styleNo", StyleNo, AllWordsSaid(StyleNo)),
            FromStyleNo = Keep("fromStyleNo", FromStyleNo, AllWordsSaid(FromStyleNo)),
            MaterialCode = Keep("materialCode", MaterialCode, AllWordsSaid(MaterialCode)),
            GroupBy = GroupBy,
            Looks = KeepAll("looks", Looks, LookSaid),
            Look = Keep("look", Look, Look is not null && Look.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => q.Contains(w, StringComparison.Ordinal)) * 2 >= Look.Split(' ').Length),
        };
        dropped = removed;
        return grounded;
    }

    /// <summary>
    /// Fills what the model left out with what the phrase list read from the same question ("jackets" = the jacket product
    /// types), for the arguments the tool takes. The phrase list is exact for the words it knows; the model is not.
    /// </summary>
    public LibraryToolArgs Fill(StyleSearchFiltersDto f, IReadOnlyList<string> parameters)
    {
        bool Takes(string name) => parameters.Contains(name);
        return new LibraryToolArgs
        {
            Lookups = Lookups,
            Customer = Customer ?? (Takes("customer") ? f.Customer : null),
            Seasons = Seasons.Count > 0 || !Takes("seasons") ? Seasons : f.Seasons,
            ProductTypes = ProductTypes.Count > 0 || !Takes("productTypes") ? ProductTypes : f.ProductTypes,
            BusinessUnits = BusinessUnits.Count > 0 || !Takes("businessUnits") ? BusinessUnits : f.BusinessUnit?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
            Gender = Gender ?? (Takes("gender") ? f.Gender : null),
            WeaveType = WeaveType ?? (Takes("weaveType") ? f.WeaveType : null),
            Material = Material ?? (Takes("material") ? f.Material : null),
            Keywords = Keywords,
            StyleNo = StyleNo ?? (Takes("styleNo") ? f.Search : null),
            FromStyleNo = FromStyleNo,
            MaterialCode = MaterialCode,
            GroupBy = GroupBy,
            Looks = Looks.Count > 0 || !Takes("looks") ? Looks : f.Looks ?? [],
            Look = Look,
        };
    }

    /// <summary>The style list filters these arguments describe. A style number searches like the Styles list.</summary>
    public StyleListQuery ListQuery(int take) => new(
        StyleNo ?? Keywords, Customer, Join(Seasons), Join(BusinessUnits), Join(ProductTypes), WeaveType, Gender, 0, take, Material, Looks: Join(Looks));

    /// <summary>The same filters as Styles list address parameters.</summary>
    public string StylesUrl()
    {
        var p = new List<string>();
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) p.Add($"{key}={Uri.EscapeDataString(value)}"); }
        Add("search", StyleNo ?? Keywords);
        Add("customer", Customer);
        Add("season", Join(Seasons));
        Add("businessUnit", Join(BusinessUnits));
        Add("productType", Join(ProductTypes));
        Add("weaveType", WeaveType);
        Add("gender", Gender);
        Add("material", Material);
        Add("looks", Join(Looks));
        return "/styles" + (p.Count > 0 ? "?" + string.Join('&', p) : "");
    }

    /// <summary>The arguments that are set, as text (shown with the answer and sent back with follow-up questions).</summary>
    public IReadOnlyDictionary<string, string> Display()
    {
        var d = new Dictionary<string, string>();
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) d[key] = value; }
        Add("customer", Customer);
        Add("seasons", Join(Seasons, ", "));
        Add("productTypes", Join(ProductTypes, ", "));
        Add("businessUnits", Join(BusinessUnits, ", "));
        Add("gender", Gender);
        Add("weaveType", WeaveType);
        Add("material", Material);
        Add("keywords", Keywords);
        Add("styleNo", StyleNo);
        Add("fromStyleNo", FromStyleNo);
        Add("materialCode", MaterialCode);
        Add("groupBy", GroupBy);
        Add("looks", Join(Looks, ", "));
        Add("look", Look);
        return d;
    }

    /// <summary>A short description of the filters for the code-written answer ("ADI, 2027-SS, JACKET").</summary>
    public string Scope()
    {
        var parts = new[] { Customer, Join(Seasons, ", "), Join(BusinessUnits, ", "), Join(ProductTypes, ", "), Gender, WeaveType, Material, Join(Looks, ", "), StyleNo ?? Keywords }
            .Where(p => !string.IsNullOrEmpty(p)).ToList();
        return parts.Count == 0 ? "the whole library" : string.Join("; ", parts);
    }

    private static string? Join(IReadOnlyList<string> codes, string separator = ",") => codes.Count == 0 ? null : string.Join(separator, codes);

    private static string? Words(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IReadOnlyList<string> KnownAll(JsonElement e, string name, IReadOnlyList<LookupItem> items)
        => Items(e, name, 60).Where(i => i.ValueKind == JsonValueKind.String).Select(i => Known(i.GetString(), items)).OfType<string>().Distinct().ToList();

    /// <summary>The library's spelling of a code (or of a name used instead), else null.</summary>
    private static string? Known(string? value, IReadOnlyList<LookupItem> items)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return items.FirstOrDefault(i => string.Equals(i.Code, v, StringComparison.OrdinalIgnoreCase))?.Code
            ?? items.FirstOrDefault(i => string.Equals(i.Name, v, StringComparison.OrdinalIgnoreCase))?.Code;
    }
}
