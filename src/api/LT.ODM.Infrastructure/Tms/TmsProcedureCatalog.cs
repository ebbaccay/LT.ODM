using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Tms;

/// <summary>A procedure the signed-in user may run, resolved against the allowlist.</summary>
public sealed record TmsProcedure(string Name, string QualifiedName, IReadOnlyDictionary<string, string> BoundParameters);

/// <summary>Resolves client procedure names against the per-module allowlist.</summary>
public sealed class TmsProcedureCatalog
{
    private sealed record Entry(string Name, List<(string Module, List<string> Roles)> Modules, IReadOnlyDictionary<string, string> BoundParameters);

    private readonly TmsProcedureOptions _options;
    private readonly Dictionary<string, Entry> _procedures = new(StringComparer.OrdinalIgnoreCase);

    public TmsProcedureCatalog(IOptions<TmsProcedureOptions> options)
    {
        _options = options.Value;
        foreach (var (module, m) in _options.Modules)
        {
            var bound = m.BoundParameters ?? _options.BoundParameters;
            foreach (var proc in m.Procedures)
            {
                if (!_procedures.TryGetValue(proc, out var entry))
                    _procedures[proc] = entry = new Entry(proc, [], new Dictionary<string, string>(bound, StringComparer.OrdinalIgnoreCase));
                entry.Modules.Add((module, m.Roles));
            }
        }

        // Per-procedure bindings apply wherever the procedure is used, whatever module lists it first.
        foreach (var (proc, overrides) in _options.ProcedureBoundParameters)
        {
            if (!_procedures.TryGetValue(proc, out var entry)) continue;
            var bound = new Dictionary<string, string>(entry.BoundParameters, StringComparer.OrdinalIgnoreCase);
            foreach (var (parameter, spec) in overrides)
            {
                if (spec == TmsProcedureOptions.NotBound) bound.Remove(parameter);
                else bound[parameter] = spec;
            }
            _procedures[proc] = entry with { BoundParameters = bound };
        }
    }

    public int Count => _procedures.Count;

    /// <summary>
    /// "iplex_srcdb01.dbo.web_rd_x", "[dbo].[web_rd_x]" and "web_rd_x" all resolve to web_rd_x in the configured schema.
    /// Returns null when the procedure is not allowlisted or the user has none of the module roles.
    /// </summary>
    public TmsProcedure? Resolve(string? requestedName, ClaimsPrincipal user)
    {
        var name = NormalizeName(requestedName);
        if (name is null || !_procedures.TryGetValue(name, out var entry)) return null;

        var allowed = entry.Modules.Any(m => m.Roles.Count == 0 || m.Roles.Any(user.IsInRole));
        return allowed ? new TmsProcedure(entry.Name, $"[{_options.Schema}].[{entry.Name}]", entry.BoundParameters) : null;
    }

    internal static string? NormalizeName(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;
        var last = requested.Trim().Split('.')[^1].Trim().TrimStart('[').TrimEnd(']');
        // Procedure names are plain identifiers; anything else is rejected outright.
        return last.Length is > 0 and <= 128 && last.All(c => char.IsLetterOrDigit(c) || c == '_') ? last : null;
    }
}
