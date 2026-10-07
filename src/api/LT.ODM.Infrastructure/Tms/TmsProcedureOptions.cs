namespace LT.ODM.Infrastructure.Tms;

/// <summary>
/// Allowlist and settings for the TMS compatibility hub, bound from "TmsProcedures"
/// (src/api/LT.ODM.Api/tms-procedures.json).
/// </summary>
public sealed class TmsProcedureOptions
{
    public const string SectionName = "TmsProcedures";

    /// <summary>Connection string name for the database holding the ported TMS procedures.</summary>
    public string ConnectionStringName { get; set; } = "StyleLibrary";

    /// <summary>Schema the procedures live in. Any database/schema prefix sent by the client is ignored.</summary>
    public string Schema { get; set; } = "dbo";

    public int CommandTimeoutSeconds { get; set; } = 300;

    /// <summary>Largest array of parameter rows accepted in one call (TMS bulk calls run one row at a time).</summary>
    public int MaxBatchRows { get; set; } = 5000;

    /// <summary>
    /// Procedure parameter name -> claim type. These parameters are always filled from the signed-in user,
    /// whatever the client sends, so a user cannot act as someone else.
    /// Claim specs: "claim" = required; "claim?" = optional, users without it get '' (e.g. "location?" for office users);
    /// "factory:claim" = factory users (user_group FTY) get their claim value ('' if missing), everyone else NULL (no filter);
    /// "role:Name" = 1 when the user has that role, else 0.
    /// </summary>
    public Dictionary<string, string> BoundParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Procedure name -> (parameter -> claim spec), applied on top of the global/module bindings for that procedure only.
    /// "-" removes a binding (the client's value is used, e.g. a username that is only a read filter).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ProcedureBoundParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public const string NotBound = "-";

    public Dictionary<string, TmsModuleOptions> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TmsModuleOptions
{
    /// <summary>Roles allowed to call this module's procedures; empty = any signed-in user.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>Exact procedure names (no prefix wildcards).</summary>
    public List<string> Procedures { get; set; } = [];

    /// <summary>Optional per-module replacement for the global BoundParameters (e.g. admin screens that pass another user's name).</summary>
    public Dictionary<string, string>? BoundParameters { get; set; }
}
