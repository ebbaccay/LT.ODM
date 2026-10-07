using System.Text.RegularExpressions;

namespace LT.ODM.Application.Admin;

/// <summary>How a list stores its codes (matches the workbook import and the Styles procedures).</summary>
public enum CodeCase { AsTyped, Upper, Lower }

/// <summary>
/// One pick list maintained in Settings > Reference lists. Key is the @List value of ref.usp_RefList_* (the whitelist:
/// anything else is refused before it reaches the database). Lengths match the columns in db/tables/style.tables.sql.
/// </summary>
public sealed record RefListDefinition(
    string Key, int CodeMaxLength, CodeCase Case, string? CodePattern, string CodeRule,
    int NameMaxLength, bool NameRequired, bool HasSortOrder, bool HasIsActive);

/// <summary>Sent to the page so its form follows the same rules as the API.</summary>
public sealed record RefListInfoDto(
    string Key, int CodeMaxLength, string CodeCase, string? CodePattern, int NameMaxLength, bool NameRequired, bool HasSortOrder, bool HasIsActive);

/// <summary>Name is the season description for seasons (optional there). UsageCount = rows that use the code (used = cannot be deleted).</summary>
public sealed record RefListItemDto(string Code, string? Name, int? SortOrder, bool? IsActive, int UsageCount);

/// <summary>IsNew: insert; otherwise update the row with this Code (the code itself cannot change). SortOrder / IsActive only where the list has them.</summary>
public sealed record SaveRefListItemRequest(bool IsNew, string Code, string? Name, int? SortOrder, bool? IsActive);

public static class ReferenceLists
{
    private const string AnyCode = "Use at most {0} characters, without leading or trailing spaces.";

    /// <summary>In the order the page lists them.</summary>
    public static readonly IReadOnlyList<RefListDefinition> All =
    [
        new("customers", 32, CodeCase.AsTyped, null, AnyCode, 150, true, false, true),
        new("seasons", 16, CodeCase.Upper, "^[0-9]{4}-[A-Z]{2,4}$", "Use the year, a dash and the season term (e.g. 2027-SS).", 64, false, false, true),
        new("seasonTerms", 8, CodeCase.Upper, "^[A-Z]{2,4}$", "Use 2-4 letters (e.g. SS).", 32, true, true, false),
        new("businessUnits", 16, CodeCase.AsTyped, null, AnyCode, 100, true, false, true),
        new("productTypes", 40, CodeCase.AsTyped, null, AnyCode, 100, true, false, true),
        new("weaveTypes", 8, CodeCase.Upper, null, AnyCode, 50, true, false, false),
        new("contentClasses", 8, CodeCase.Upper, "^[A-Z0-9_]{1,8}$", "Use 1-8 letters, digits or underscores (e.g. FAB).", 50, true, true, false),
        new("materialTypes", 16, CodeCase.Upper, null, AnyCode, 100, true, false, false),
        new("uoms", 8, CodeCase.Lower, null, AnyCode, 50, true, false, false),
        new("suppliers", 32, CodeCase.AsTyped, null, AnyCode, 150, true, false, true),
    ];

    public static RefListDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    public static RefListInfoDto Info(RefListDefinition d) => new(
        d.Key, d.CodeMaxLength, d.Case switch { CodeCase.Upper => "upper", CodeCase.Lower => "lower", _ => "" },
        d.CodePattern, d.NameMaxLength, d.NameRequired, d.HasSortOrder, d.HasIsActive);

    public static string NormalizeCode(RefListDefinition d, string? code)
    {
        var c = (code ?? "").Trim();
        return d.Case switch { CodeCase.Upper => c.ToUpperInvariant(), CodeCase.Lower => c.ToLowerInvariant(), _ => c };
    }

    /// <summary>Trimmed code in the list's case and trimmed name; SortOrder / IsActive dropped where the list has none.</summary>
    public static SaveRefListItemRequest Normalize(RefListDefinition d, SaveRefListItemRequest r) => r with
    {
        Code = NormalizeCode(d, r.Code),
        Name = r.Name?.Trim() ?? "",
        SortOrder = d.HasSortOrder ? r.SortOrder : null,
        IsActive = d.HasIsActive ? r.IsActive ?? true : null,
    };

    public static Dictionary<string, string[]> Validate(RefListDefinition d, SaveRefListItemRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        var code = NormalizeCode(d, r.Code);
        var codeOk = code.Length > 0 && code.Length <= d.CodeMaxLength && !code.Any(char.IsControl)
                     && (d.CodePattern is null || Regex.IsMatch(code, d.CodePattern));
        if (!codeOk) errors["code"] = [string.Format(d.CodeRule, d.CodeMaxLength)];
        var name = r.Name?.Trim() ?? "";
        if ((d.NameRequired && name.Length == 0) || name.Length > d.NameMaxLength)
            errors["name"] = [d.NameRequired ? $"Enter a name (at most {d.NameMaxLength} characters)." : $"Use at most {d.NameMaxLength} characters."];
        if (d.HasSortOrder && r.SortOrder is not (>= 0 and <= short.MaxValue))
            errors["sortOrder"] = ["Use a whole number from 0 to 32767."];
        return errors;
    }
}
