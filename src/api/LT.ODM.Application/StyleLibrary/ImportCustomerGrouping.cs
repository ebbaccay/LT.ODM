using System.Text.RegularExpressions;

namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// New customer accounts that look like one customer: the same brand word in their names ("POLO - FOA Men's Knit",
/// "POLO MEN'S WOVEN SHIRT FOA" -> POLO), or a brand word that starts with an existing customer's code or matches its
/// name ("TJ Skechers Mens Export" -> SKE). Only a suggestion: the import asks before anything is grouped.
/// </summary>
public static partial class ImportCustomerGrouping
{
    /// <summary>Words that say nothing about the brand (account prefixes, ranges, fabrics, regions).</summary>
    private static readonly HashSet<string> Noise =
    [
        "THE", "AND", "VHKL", "MEN", "MENS", "WOMEN", "WOMENS", "LADIES", "KIDS", "KNIT", "KNITS", "WOVEN", "WOVENS", "SHIRT",
        "SHIRTS", "EXPORT", "DOMESTIC", "LINE", "PARENT", "TEAM", "OTHER", "MERCH", "ORDERS", "INC", "LTD", "LIMITED",
        "COMPANY", "HOLDINGS", "GROUP", "WEAR", "LICENSEE", "LICENSED",
    ];

    [GeneratedRegex("[A-Z]+")]
    private static partial Regex Words();

    /// <summary>The first brand-like word of an account name (3+ letters, not a noise word), or null.</summary>
    public static string? BrandKey(string? name)
        => name is null ? null : Words().Matches(name.ToUpperInvariant()).Select(m => m.Value).FirstOrDefault(w => w.Length >= 3 && !Noise.Contains(w));

    /// <summary>
    /// Groups of new customer accounts (list "customer", Label = the file's customer name) worth asking about: two or
    /// more accounts with the same brand word (a word that starts another, DIL / DILLARDS, counts as the same), or any
    /// account whose brand word points at an existing customer.
    /// </summary>
    public static IReadOnlyList<ImportCustomerGroupDto> Build(IReadOnlyList<ImportNewCodeDto> codes, IReadOnlyList<ImportCodeOptionDto> options)
    {
        // Accounts already given a brand (file or answer) are answered.
        var accounts = codes.Where(c => c.List == "customer" && c.SuggestedCode is null && c.Brand is null)
            .Select(c => (Account: c, Key: BrandKey(c.Label ?? c.Value)))
            .Where(x => x.Key is not null)
            .ToList();

        // Shortest keys first, so DIL absorbs DILLARDS.
        var keys = new List<string>();
        foreach (var key in accounts.Select(a => a.Key!).Distinct().OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal))
            if (!keys.Any(k => key.StartsWith(k, StringComparison.Ordinal))) keys.Add(key);
        string Root(string key) => keys.First(k => key.StartsWith(k, StringComparison.Ordinal));

        var customers = options.Where(o => o.List == "customer").ToList();
        var groups = new List<ImportCustomerGroupDto>();
        foreach (var g in accounts.GroupBy(a => Root(a.Key!)))
        {
            // The longest brand word of the group names it (DILLARDS rather than DIL).
            var name = g.Select(a => a.Key!).OrderByDescending(k => k.Length).First();
            var existing = customers.FirstOrDefault(o =>
                (o.Code.Length >= 3 && g.Any(a => a.Key!.StartsWith(o.Code.ToUpperInvariant(), StringComparison.Ordinal)))
                || (BrandKey(o.Name) is { } k && g.Any(a => a.Key == k)));
            var list = g.Select(a => a.Account).OrderByDescending(a => a.Rows).ToList();
            if (list.Count >= 2 || existing is not null)
                groups.Add(new ImportCustomerGroupDto(name, list, existing?.Code, existing?.Name));
        }
        return groups.OrderByDescending(g => g.SuggestedCode is not null).ThenByDescending(g => g.Accounts.Sum(a => a.Rows)).ToList();
    }
}
