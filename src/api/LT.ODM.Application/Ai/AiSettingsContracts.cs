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

/// <summary>What the AI pages show about a connection (no endpoint or key).</summary>
public static class AiConnectionInfo
{
    public static AiProviderInfo Of(AiConnection? c)
    {
        if (c is null) return new AiProviderInfo("—", "—", false, false);
        var gemini = c.Kind == AiConnectionKinds.Gemini;
        var configured = !string.IsNullOrWhiteSpace(c.Endpoint) && !string.IsNullOrWhiteSpace(c.Model) && c.Kind != AiConnectionKinds.Custom
                         && (!gemini || !string.IsNullOrWhiteSpace(c.ApiKey));
        return new AiProviderInfo(c.Name, c.Model, configured, LeavesNetwork: gemini || !c.InHouse, SupportsReferenceImage: gemini, c.Source);
    }
}

/// <summary>Finds the connection for a job: Settings > AI connections first, then appsettings. Null = not set up.</summary>
public interface IAiConnectionResolver
{
    Task<AiConnection?> ResolveAsync(string purpose, CancellationToken ct = default);

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
public sealed record AiPurposeDto(string Purpose, bool InUse, int? ConnectionId, string? ConnectionName, string? Model, string? UpdatedBy, DateTime? UpdatedUtc);

/// <summary>What the job uses when it is not set in the app (from appsettings), or null when there is no default.</summary>
public sealed record AiFallbackDto(string Purpose, string Provider, string Model, bool IsConfigured, bool LeavesNetwork);

public sealed record AiSettingsDto(
    IReadOnlyList<AiConnectionDto> Connections, IReadOnlyList<AiPurposeDto> Purposes, IReadOnlyList<AiFallbackDto> Fallbacks, IReadOnlyList<string> Kinds);

/// <summary>
/// RowVer null = new connection. ApiKey: null or blank keeps the saved key; a value replaces it; ClearApiKey removes it.
/// </summary>
public sealed record SaveAiConnectionRequest(
    byte[]? RowVer, string Name, string Kind, string Endpoint, string? ApiKey, bool ClearApiKey, bool InHouse, int TimeoutSeconds, string? Notes,
    bool IsActive);

/// <summary>ConnectionId null = use the app default (appsettings) or nothing.</summary>
public sealed record SaveAiPurposeRequest(int? ConnectionId, string? Model);

/// <summary>Tests a saved connection (ConnectionId) or a draft from the form; a blank ApiKey uses the saved one.</summary>
public sealed record TestAiConnectionRequest(int? ConnectionId, string Kind, string Endpoint, string? ApiKey, int TimeoutSeconds);

/// <summary>Models: what the service says it offers (OpenAI-compatible and Gemini), to pick from.</summary>
public sealed record AiTestResultDto(bool Ok, string Message, IReadOnlyList<string> Models);

/// <summary>For AI pages (readers): which service each job uses, without endpoints or keys.</summary>
public sealed record AiPurposeStatusDto(string Purpose, bool InUse, bool IsConfigured, string? Provider, string? Model, bool LeavesNetwork, string? Source);

/// <summary>Stored connection row (API key still encrypted).</summary>
public sealed record AiConnectionRow(
    int ConnectionId, string Name, string Kind, string Endpoint, string? ApiKeyProtected, string? ApiKeyHint, bool InHouse, int TimeoutSeconds,
    string? Notes, bool IsActive, string UpdatedBy, DateTime UpdatedUtc, byte[] RowVer);

public sealed record AiPurposeRow(string Purpose, int? ConnectionId, string? Model, string? UpdatedBy, DateTime? UpdatedUtc);

public interface IAiSettingsRepository
{
    Task<(IReadOnlyList<AiConnectionRow> Connections, IReadOnlyList<AiPurposeRow> Purposes)> GetAsync(CancellationToken ct = default);

    /// <summary>KeyAction: keep, set (ApiKeyProtected + hint) or clear. Returns the id. 404 / 409 as AiSettingsException.</summary>
    Task<int> SaveConnectionAsync(
        int? connectionId, SaveAiConnectionRequest request, string keyAction, string? apiKeyProtected, string? apiKeyHint, string changedBy,
        CancellationToken ct = default);

    Task DeleteConnectionAsync(int connectionId, byte[] rowVer, CancellationToken ct = default);
    Task SavePurposeAsync(string purpose, int? connectionId, string? model, string changedBy, CancellationToken ct = default);
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
        if (r.ConnectionId is not null && string.IsNullOrWhiteSpace(r.Model)) e["model"] = ["Enter the model name the service uses."];
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
