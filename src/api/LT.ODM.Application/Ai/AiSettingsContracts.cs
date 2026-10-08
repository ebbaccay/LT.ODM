using System.Text.RegularExpressions;

namespace LT.ODM.Application.Ai;

// Settings > AI connections (/api/v1/admin/ai, Admin): which AI service each job uses, set in the app.
// A connection = where to call (kind, endpoint, API key, in-house or cloud). A job (purpose) = which connection and model.
// Jobs without a connection fall back to appsettings (Ai:Provider, Ai:Gemini, Ai:OpenAiCompatible) where the app has a default.

public static class AiPurposes
{
    /// <summary>AI Studio text answers: search filters, change summaries, BOM check notes, render prompts.</summary>
    public const string Text = "text";
    /// <summary>AI Studio style renders.</summary>
    public const string Image = "image";
    /// <summary>AI Lab: look-alike search (image / text embeddings).</summary>
    public const string Embedding = "embedding";
    /// <summary>AI Lab: colour reader, photo understanding.</summary>
    public const string Vision = "vision";
    /// <summary>AI Lab: tech pack reader (PDF pages).</summary>
    public const string Document = "document";
    /// <summary>AI Lab: cost and lead-time forecasts (an in-house prediction service).</summary>
    public const string Prediction = "prediction";

    public static readonly string[] All = [Text, Image, Embedding, Vision, Document, Prediction];

    /// <summary>Jobs the app calls today; the others are prepared for AI Lab capabilities.</summary>
    public static readonly string[] InUse = [Text, Image];
}

public static class AiConnectionKinds
{
    /// <summary>Google's cloud API (generativelanguage.googleapis.com). Always outside the LT network.</summary>
    public const string Gemini = "Gemini";
    /// <summary>A server with an OpenAI-style API: vLLM, Ollama, LM Studio, LocalAI (in-house), or a cloud service.</summary>
    public const string OpenAiCompatible = "OpenAiCompatible";
    /// <summary>Any other HTTP service (e.g. an in-house prediction service); the app only tests that it answers.</summary>
    public const string Custom = "Custom";

    public static readonly string[] All = [Gemini, OpenAiCompatible, Custom];
}

/// <summary>Where and how the app calls an AI service, resolved for one job (key in clear; server side only).</summary>
public sealed record AiConnection(
    string Name, string Kind, string Endpoint, string? ApiKey, string Model, bool InHouse, int TimeoutSeconds, string Source);

/// <summary>Why a job is blocked by the central switches (Settings > AI connections).</summary>
public static class AiBlocks
{
    /// <summary>The master switch is off: no AI call at all.</summary>
    public const string AiOff = "aiOff";
    /// <summary>This job is turned off.</summary>
    public const string JobOff = "jobOff";
    /// <summary>The job's service is outside the LT network and cloud AI is not allowed.</summary>
    public const string CloudBlocked = "cloudBlocked";
}

/// <summary>The message users see when an AI feature cannot run (HTTP 503).</summary>
public static class AiUnavailable
{
    public static string Message(string feature, AiProviderInfo info) => info.Blocked switch
    {
        AiBlocks.AiOff => "AI features are turned off on this server. An administrator can turn them on in Settings > AI connections.",
        AiBlocks.JobOff => $"{feature} is turned off on this server. An administrator can turn it on in Settings > AI connections.",
        AiBlocks.CloudBlocked => $"{feature} would use a cloud AI service ({info.Provider}), and cloud AI is not allowed on this server, so no data was sent. "
            + "An administrator can allow cloud AI or point the job at the in-house server in Settings > AI connections.",
        _ => $"{feature} is not set up on this server. Ask your administrator to set it in Settings > AI connections.",
    };

    /// <summary>For features that only know whether AI can run.</summary>
    public static string Generic(string feature)
        => $"{feature} is not available: AI is not set up, turned off, or set to a cloud service that is not allowed on this server (Settings > AI connections).";
}

/// <summary>A job's service and, if the central switches forbid using it, why (Connection is then still shown, not used).</summary>
public sealed record AiResolution(AiConnection? Connection, string? Blocked);

/// <summary>The central switches. AllowCloud = false blocks every service outside the LT network.</summary>
public sealed record AiPolicy(bool AiEnabled, bool AllowCloud, string? UpdatedBy, DateTime? UpdatedUtc)
{
    /// <summary>Before ai.Policy exists (older database): everything allowed, as before.</summary>
    public static readonly AiPolicy Open = new(true, true, null, null);
}

/// <summary>What the AI pages show about a connection (no endpoint or key).</summary>
public static class AiConnectionInfo
{
    public static AiProviderInfo Of(AiConnection? c) => Of(new AiResolution(c, null));

    public static AiProviderInfo Of(AiResolution r)
    {
        var c = r.Connection;
        if (c is null) return new AiProviderInfo("—", "—", false, false, Blocked: r.Blocked);
        var gemini = c.Kind == AiConnectionKinds.Gemini;
        var configured = !string.IsNullOrWhiteSpace(c.Endpoint) && !string.IsNullOrWhiteSpace(c.Model) && c.Kind != AiConnectionKinds.Custom
                         && (!gemini || !string.IsNullOrWhiteSpace(c.ApiKey));
        return new AiProviderInfo(c.Name, c.Model, configured && r.Blocked is null, LeavesNetwork: gemini || !c.InHouse, SupportsReferenceImage: gemini,
            c.Source, r.Blocked);
    }
}

/// <summary>Finds the connection for a job: Settings > AI connections first, then appsettings. Null = not set up.</summary>
public interface IAiConnectionResolver
{
    /// <summary>The service to call, or null when none is set up or the central switches block it.</summary>
    Task<AiConnection?> ResolveAsync(string purpose, CancellationToken ct = default);

    /// <summary>The job's service (even when blocked, for display) and whether the switches block it.</summary>
    Task<AiResolution> ExplainAsync(string purpose, CancellationToken ct = default);

    /// <summary>Drops the cached settings (after a save) so the next call reads them again.</summary>
    void Invalidate();
}

/// <summary>Encrypts API keys before they are stored (ASP.NET Data Protection in the API).</summary>
public interface IAiSecretProtector
{
    string Protect(string secret);

    /// <summary>Null when the value cannot be decrypted (e.g. the key ring was lost): the key must be entered again.</summary>
    string? Unprotect(string protectedValue);
}

// ----- Admin screen -----

/// <summary>ApiKeyHint is the key's last 4 characters; the key itself never leaves the server.</summary>
public sealed record AiConnectionDto(
    int ConnectionId, string Name, string Kind, string Endpoint, bool HasApiKey, string? ApiKeyHint, bool InHouse, int TimeoutSeconds,
    string? Notes, bool IsActive, IReadOnlyList<string> UsedBy, string UpdatedBy, DateTime UpdatedUtc, byte[] RowVer);

/// <summary>A job with its connection and model (null = not set in the app).</summary>
public sealed record AiPurposeDto(
    string Purpose, bool InUse, int? ConnectionId, string? ConnectionName, string? Model, string? UpdatedBy, DateTime? UpdatedUtc, bool Disabled,
    string? Blocked);

/// <summary>What the job uses when it is not set in the app (from appsettings), or null when there is no default.</summary>
public sealed record AiFallbackDto(string Purpose, string Provider, string Model, bool IsConfigured, bool LeavesNetwork);

public sealed record AiSettingsDto(
    IReadOnlyList<AiConnectionDto> Connections, IReadOnlyList<AiPurposeDto> Purposes, IReadOnlyList<AiFallbackDto> Fallbacks, IReadOnlyList<string> Kinds, AiPolicy Policy);

/// <summary>
/// RowVer null = new connection. ApiKey: null or blank keeps the saved key; a value replaces it; ClearApiKey removes it.
/// </summary>
public sealed record SaveAiConnectionRequest(
    byte[]? RowVer, string Name, string Kind, string Endpoint, string? ApiKey, bool ClearApiKey, bool InHouse, int TimeoutSeconds, string? Notes,
    bool IsActive);

/// <summary>ConnectionId null = use the app default (appsettings) or nothing; Off = the job is turned off.</summary>
public sealed record SaveAiPurposeRequest(int? ConnectionId, string? Model, bool Off = false);

public sealed record SaveAiPolicyRequest(bool AiEnabled, bool AllowCloud);

/// <summary>Tests a saved connection (ConnectionId) or a draft from the form; a blank ApiKey uses the saved one.</summary>
public sealed record TestAiConnectionRequest(int? ConnectionId, string Kind, string Endpoint, string? ApiKey, int TimeoutSeconds);

/// <summary>Models: what the service says it offers (OpenAI-compatible and Gemini), to pick from.</summary>
public sealed record AiTestResultDto(bool Ok, string Message, IReadOnlyList<string> Models);

/// <summary>For AI pages (readers): which service each job uses, without endpoints or keys.</summary>
public sealed record AiPurposeStatusDto(
    string Purpose, bool InUse, bool IsConfigured, string? Provider, string? Model, bool LeavesNetwork, string? Source, string? Blocked = null);

/// <summary>Stored connection row (API key still encrypted).</summary>
public sealed record AiConnectionRow(
    int ConnectionId, string Name, string Kind, string Endpoint, string? ApiKeyProtected, string? ApiKeyHint, bool InHouse, int TimeoutSeconds,
    string? Notes, bool IsActive, string UpdatedBy, DateTime UpdatedUtc, byte[] RowVer);

public sealed record AiPurposeRow(string Purpose, int? ConnectionId, string? Model, string? UpdatedBy, DateTime? UpdatedUtc, bool Disabled = false);

/// <summary>Everything stored in Settings > AI connections.</summary>
public sealed record AiSettingsData(IReadOnlyList<AiConnectionRow> Connections, IReadOnlyList<AiPurposeRow> Purposes, AiPolicy Policy);

public interface IAiSettingsRepository
{
    Task<AiSettingsData> GetAsync(CancellationToken ct = default);

    /// <summary>KeyAction: keep, set (ApiKeyProtected + hint) or clear. Returns the id. 404 / 409 as AiSettingsException.</summary>
    Task<int> SaveConnectionAsync(
        int? connectionId, SaveAiConnectionRequest request, string keyAction, string? apiKeyProtected, string? apiKeyHint, string changedBy,
        CancellationToken ct = default);

    Task DeleteConnectionAsync(int connectionId, byte[] rowVer, CancellationToken ct = default);
    Task SavePurposeAsync(string purpose, int? connectionId, string? model, bool disabled, string changedBy, CancellationToken ct = default);
    Task SavePolicyAsync(bool aiEnabled, bool allowCloud, string changedBy, CancellationToken ct = default);
}

/// <summary>A rule the database refused (in use, changed by someone else, not found). StatusCode is 400, 404 or 409.</summary>
public sealed class AiSettingsException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static partial class AiSettingsValidation
{
    [GeneratedRegex(@"^https?://[^\s/$.?#][^\s]*$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public static Dictionary<string, string[]> Connection(SaveAiConnectionRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 100) e["name"] = ["Enter a name of up to 100 characters."];
        if (!AiConnectionKinds.All.Contains(r.Kind)) e["kind"] = ["Choose Gemini, OpenAI-compatible or Custom."];
        Endpoint(e, r.Endpoint);
        if ((r.ApiKey?.Length ?? 0) > 500) e["apiKey"] = ["The API key is too long."];
        if (r.TimeoutSeconds is < 5 or > 600) e["timeoutSeconds"] = ["Use 5 to 600 seconds."];
        if ((r.Notes?.Length ?? 0) > 400) e["notes"] = ["At most 400 characters."];
        return e;
    }

    public static Dictionary<string, string[]> Test(TestAiConnectionRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (!AiConnectionKinds.All.Contains(r.Kind)) e["kind"] = ["Choose Gemini, OpenAI-compatible or Custom."];
        Endpoint(e, r.Endpoint);
        return e;
    }

    public static Dictionary<string, string[]> Purpose(string purpose, SaveAiPurposeRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (!AiPurposes.All.Contains(purpose)) e["purpose"] = ["Unknown job."];
        if (!r.Off && r.ConnectionId is not null && string.IsNullOrWhiteSpace(r.Model)) e["model"] = ["Enter the model name the service uses."];
        if ((r.Model?.Trim().Length ?? 0) > 200) e["model"] = ["At most 200 characters."];
        return e;
    }

    private static void Endpoint(Dictionary<string, string[]> e, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Trim().Length > 400 || !UrlRegex().IsMatch(endpoint.Trim()))
            e["endpoint"] = ["Enter the service address, starting with http:// or https://."];
    }

    /// <summary>Last 4 characters, for recognising which key is saved.</summary>
    public static string Hint(string key) => key.Length <= 4 ? new string('•', key.Length) : key[^4..];
}
