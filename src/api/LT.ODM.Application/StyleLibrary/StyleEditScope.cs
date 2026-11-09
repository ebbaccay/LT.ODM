namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// Business unit teams: the business units whose styles a user may change (style.usp_EditScope_Get). Everyone still reads
/// the whole library. The database enforces it on every write (style.usp_AssertCanEdit); the API uses it only to show
/// what can be changed (pick lists, "can edit" flags).
/// </summary>
public interface IStyleEditScopeRepository
{
    /// <summary>The user's business units; empty = may change every style (no business units, or Admin).</summary>
    Task<IReadOnlyList<string>> GetBusinessUnitsAsync(string userName, CancellationToken ct = default);
}

/// <summary>BusinessUnits empty = not limited.</summary>
public sealed record StyleEditScope(IReadOnlyList<string> BusinessUnits)
{
    public static readonly StyleEditScope Unlimited = new([]);

    public bool Limited => BusinessUnits.Count > 0;

    public bool Allows(string? businessUnitCode)
        => !Limited || (businessUnitCode is not null && BusinessUnits.Contains(businessUnitCode.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Same rule as style.fn_CanEditStyle: a style without a business unit only by the person who created it.</summary>
    public bool CanEdit(string? businessUnitCode, string? createdBy, string userName)
        => !Limited || (string.IsNullOrWhiteSpace(businessUnitCode)
            ? string.Equals(createdBy, userName, StringComparison.OrdinalIgnoreCase)
            : Allows(businessUnitCode));
}
