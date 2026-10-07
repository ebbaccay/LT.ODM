using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace LT.ODM.Infrastructure.Tms;

/// <summary>Raised for requests the caller must fix (shown to the client as-is).</summary>
public sealed class TmsRequestException(string message) : Exception(message);

/// <summary>
/// Turns the TMS JSON parameter object into SqlParameters.
/// Values are converted exactly like the TMS hub did (so tested screens behave the same), but:
/// - only parameters the procedure declares are accepted;
/// - identity parameters (username, created_by, ...) are always taken from the signed-in user.
/// </summary>
public static class TmsParameterBinder
{
    public static List<SqlParameter> Bind(TmsProcedure procedure, IReadOnlyCollection<string> declaredParameters, JsonElement? parameters, ClaimsPrincipal user)
    {
        var declared = new HashSet<string>(declaredParameters.Select(p => p.TrimStart('@')), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, SqlParameter>(StringComparer.OrdinalIgnoreCase);

        if (parameters is { ValueKind: JsonValueKind.Object } obj)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                var name = prop.Name.TrimStart('@');
                if (!declared.Contains(name))
                    throw new TmsRequestException($"Procedure {procedure.Name} has no parameter @{name}.");
                result[name] = new SqlParameter($"@{name}", ToValue(prop.Value) ?? DBNull.Value);
            }
        }
        else if (parameters is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            throw new TmsRequestException("Parameters must be a JSON object.");
        }

        foreach (var (parameter, claimSpec) in procedure.BoundParameters)
        {
            if (!declared.Contains(parameter)) continue;
            result[parameter] = new SqlParameter($"@{parameter}", BoundValue(claimSpec, parameter, user));
        }

        return [.. result.Values];
    }

    private const string FactoryPrefix = "factory:";
    private const string RolePrefix = "role:";

    /// <summary>Factory users ('FTY' user group in TMS; some screens also checked 'factory').</summary>
    public static bool IsFactoryUser(ClaimsPrincipal user)
        => user.FindFirst("user_group")?.Value?.Trim().ToUpperInvariant() is "FTY" or "FACTORY";

    /// <summary>See TmsProcedureOptions.BoundParameters for the claim spec forms.</summary>
    private static object BoundValue(string claimSpec, string parameter, ClaimsPrincipal user)
    {
        // "factory:location" = a filter the TMS screens set only for factory users. Enforced here, so a factory
        // user cannot see other factories' data; everyone else gets NULL (no filter), whatever the browser sent.
        if (claimSpec.StartsWith(FactoryPrefix, StringComparison.OrdinalIgnoreCase))
            return IsFactoryUser(user) ? user.FindFirst(claimSpec[FactoryPrefix.Length..])?.Value ?? "" : DBNull.Value;

        // "role:Admin" = 1 when the user has that role, else 0 (e.g. Admins may see and change every concept).
        if (claimSpec.StartsWith(RolePrefix, StringComparison.OrdinalIgnoreCase))
            return user.IsInRole(claimSpec[RolePrefix.Length..]);

        // "location?" = optional claim: users without it get '' (what the TMS screens sent for them).
        var optional = claimSpec.EndsWith('?');
        var claimType = optional ? claimSpec[..^1] : claimSpec;
        return user.FindFirst(claimType)?.Value
            ?? (optional ? "" : throw new TmsRequestException($"Your sign-in does not provide {claimType}, needed for @{parameter}."));
    }

    /// <summary>Same mapping as the TMS ProcedureHub (string, int, decimal, bool, null, raw JSON).</summary>
    internal static object? ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt32(out var i) ? i : value.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText(),
    };
}
