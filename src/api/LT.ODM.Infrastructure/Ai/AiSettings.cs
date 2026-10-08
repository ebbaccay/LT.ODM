using System.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>
/// Finds the service for an AI job: Settings > AI connections (ai.* tables) first, then appsettings for the jobs the app
/// has a default for (text, image). Then the central switches: AI off, the job off, or (cloud not allowed) a service
/// outside the LT network all block the call, whatever the job or appsettings say. The stored settings are cached for a
/// minute and dropped on every save. If the ai tables are not deployed yet, the app uses appsettings with no switches.
/// </summary>
public sealed class AiConnectionResolver(
    IServiceScopeFactory scopes, IAiSecretProtector protector, IOptions<AiOptions> ai, IOptions<GeminiOptions> gemini,
    IOptions<OpenAiCompatibleOptions> openAi, ILogger<AiConnectionResolver> logger) : IAiConnectionResolver
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;

    private sealed record Snapshot(IReadOnlyDictionary<string, AiConnection> ByPurpose, IReadOnlySet<string> Disabled, AiPolicy Policy, DateTime LoadedUtc);

    public void Invalidate() => _snapshot = null;

    public async Task<AiConnection?> ResolveAsync(string purpose, CancellationToken ct = default)
        => await ExplainAsync(purpose, ct) is { Blocked: null } r ? r.Connection : null;

    public async Task<AiResolution> ExplainAsync(string purpose, CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        if (s.Disabled.Contains(purpose)) return new AiResolution(null, AiBlocks.JobOff);
        var c = s.ByPurpose.TryGetValue(purpose, out var set) ? set : Fallback(purpose);
        if (!s.Policy.AiEnabled) return new AiResolution(c, AiBlocks.AiOff);
        if (c is not null && !s.Policy.AllowCloud && (c.Kind == AiConnectionKinds.Gemini || !c.InHouse)) return new AiResolution(c, AiBlocks.CloudBlocked);
        return new AiResolution(c, null);
    }

    /// <summary>The appsettings default for a job, or null when the app has none (AI Lab jobs).</summary>
    public AiConnection? Fallback(string purpose)
    {
        var o = openAi.Value;
        var g = gemini.Value;
        bool InHouseProvider(string p) => string.Equals(p, AiOptions.OpenAiCompatible, StringComparison.OrdinalIgnoreCase);
        return purpose switch
        {
            AiPurposes.Text when InHouseProvider(ai.Value.Provider)
                => new AiConnection("In-house (OpenAI-compatible)", AiConnectionKinds.OpenAiCompatible, o.Endpoint, o.ApiKey, o.Model, o.InHouse, o.TimeoutSeconds, "appsettings"),
            AiPurposes.Text
                => new AiConnection("Gemini", AiConnectionKinds.Gemini, g.Endpoint, g.ApiKey, g.Model, false, g.TimeoutSeconds, "appsettings"),
            AiPurposes.Image when InHouseProvider(ai.Value.ImageProvider)
                => new AiConnection("In-house (OpenAI-compatible)", AiConnectionKinds.OpenAiCompatible,
                    string.IsNullOrWhiteSpace(o.ImageEndpoint) ? o.Endpoint : o.ImageEndpoint, o.ApiKey, o.ImageModel, o.InHouse, o.TimeoutSeconds, "appsettings"),
            AiPurposes.Image
                => new AiConnection("Gemini", AiConnectionKinds.Gemini, g.Endpoint, g.ApiKey, g.ImageModel, false, g.TimeoutSeconds, "appsettings"),
            _ => null,
        };
    }

    private async Task<Snapshot> LoadAsync(CancellationToken ct)
    {
        if (_snapshot is { } s && DateTime.UtcNow - s.LoadedUtc < CacheFor) return s;
        await _gate.WaitAsync(ct);
        try
        {
            if (_snapshot is { } again && DateTime.UtcNow - again.LoadedUtc < CacheFor) return again;
            var byPurpose = new Dictionary<string, AiConnection>(StringComparer.OrdinalIgnoreCase);
            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var policy = AiPolicy.Open;
            try
            {
                using var scope = scopes.CreateScope();
                var data = await scope.ServiceProvider.GetRequiredService<IAiSettingsRepository>().GetAsync(ct);
                policy = data.Policy;
                var byId = data.Connections.Where(c => c.IsActive).ToDictionary(c => c.ConnectionId);
                foreach (var p in data.Purposes)
                {
                    if (p.Disabled)
                    {
                        disabled.Add(p.Purpose);
                        continue;
                    }
                    if (p.ConnectionId is not { } id || !byId.TryGetValue(id, out var c) || string.IsNullOrWhiteSpace(p.Model)) continue;
                    var key = c.ApiKeyProtected is null ? null : protector.Unprotect(c.ApiKeyProtected);
                    if (c.ApiKeyProtected is not null && key is null)
                        logger.LogWarning("The API key of AI connection {Name} cannot be decrypted; enter it again in Settings > AI connections.", c.Name);
                    byPurpose[p.Purpose] = new AiConnection(c.Name, c.Kind, c.Endpoint, key, p.Model.Trim(), c.InHouse, c.TimeoutSeconds, "settings");
                }
            }
            catch (SqlException ex)
            {
                logger.LogWarning(ex, "AI connections could not be read (are db/tables/ai.tables.sql and db/procedures/ai.procedures.sql deployed?); using appsettings.");
            }
            return _snapshot = new Snapshot(byPurpose, disabled, policy, DateTime.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>"Test" on Settings > AI connections: one harmless call that lists the models the service offers.</summary>
public sealed class AiConnectionTester(HttpClient http, ILogger<AiConnectionTester> logger)
{
    public async Task<AiTestResultDto> TestAsync(string kind, string endpoint, string? apiKey, int timeoutSeconds, CancellationToken ct)
    {
        using var message = kind switch
        {
            AiConnectionKinds.Gemini => new HttpRequestMessage(HttpMethod.Get, AiHttp.Url(endpoint, "models?pageSize=200")),
            AiConnectionKinds.OpenAiCompatible => new HttpRequestMessage(HttpMethod.Get, AiHttp.Url(endpoint, "models")),
            _ => new HttpRequestMessage(HttpMethod.Get, endpoint),
        };
        if (kind == AiConnectionKinds.Gemini) message.Headers.Add("x-goog-api-key", apiKey ?? "");
        else if (!string.IsNullOrWhiteSpace(apiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 30)));
        try
        {
            using var response = await http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return new AiTestResultDto(false, (int)response.StatusCode switch
                {
                    401 or 403 => $"The service refused the API key ({(int)response.StatusCode}).",
                    404 => "The service answered, but this address has no model list (404). Check the address (it usually ends in /v1).",
                    _ => $"The service answered with an error ({(int)response.StatusCode}).",
                }, []);
            if (kind == AiConnectionKinds.Custom) return new AiTestResultDto(true, "The service answered.", []);

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
            var models = kind == AiConnectionKinds.Gemini
                ? (json?["models"] as JsonArray ?? []).Select(m => (string?)m?["name"]).OfType<string>().Select(n => n.StartsWith("models/") ? n[7..] : n)
                : (json?["data"] as JsonArray ?? []).Select(m => (string?)m?["id"]).OfType<string>();
            var list = models.Distinct().Order().Take(300).ToList();
            return new AiTestResultDto(true, list.Count > 0 ? $"Connected. The service offers {list.Count} models." : "Connected.", list);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AiTestResultDto(false, "No answer in time. Check the address and that the server is running.", []);
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation("AI connection test failed: {Error}", ex.Message);
            return new AiTestResultDto(false, "The service could not be reached. Check the address, the port and the firewall.", []);
        }
        catch (System.Text.Json.JsonException)
        {
            return new AiTestResultDto(true, "Connected, but the model list could not be read.", []);
        }
    }
}

/// <summary>ai.usp_* procedures (db/procedures/ai.procedures.sql).</summary>
public sealed class AiSettingsRepository(IDbConnectionFactory connectionFactory) : IAiSettingsRepository
{
    public async Task<AiSettingsData> GetAsync(CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        using var grid = await conn.QueryMultipleAsync(Proc("ai.usp_Settings_Get", null, ct));
        var connections = (await grid.ReadAsync<AiConnectionRow>()).Select(c => c with { UpdatedUtc = DateTime.SpecifyKind(c.UpdatedUtc, DateTimeKind.Utc) }).ToList();
        var purposes = (await grid.ReadAsync<AiPurposeRow>())
            .Select(p => p with { UpdatedUtc = p.UpdatedUtc is { } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : null }).ToList();
        var policy = (await grid.ReadAsync<AiPolicy>()).FirstOrDefault();
        return new AiSettingsData(connections, purposes,
            policy is null ? AiPolicy.Open : policy with { UpdatedUtc = policy.UpdatedUtc is { } pu ? DateTime.SpecifyKind(pu, DateTimeKind.Utc) : null });
    }

    public async Task<int> SaveConnectionAsync(int? connectionId, SaveAiConnectionRequest r, string keyAction, string? apiKeyProtected, string? apiKeyHint,
        string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        return await RuleErrors(() => conn.QuerySingleAsync<int>(Proc("ai.usp_Connection_Save", new
        {
            ConnectionId = connectionId, RowVer = connectionId is null ? null : r.RowVer, Name = r.Name.Trim(), r.Kind, Endpoint = r.Endpoint.Trim().TrimEnd('/'),
            KeyAction = keyAction, ApiKeyProtected = apiKeyProtected, ApiKeyHint = apiKeyHint,
            InHouse = r.Kind != AiConnectionKinds.Gemini && r.InHouse, r.TimeoutSeconds, Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(),
            r.IsActive, ChangedBy = changedBy,
        }, ct)));
    }

    public async Task DeleteConnectionAsync(int connectionId, byte[] rowVer, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await RuleErrors(() => conn.ExecuteAsync(Proc("ai.usp_Connection_Delete", new { ConnectionId = connectionId, RowVer = rowVer }, ct)));
    }

    public async Task SavePurposeAsync(string purpose, int? connectionId, string? model, bool disabled, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await RuleErrors(() => conn.ExecuteAsync(Proc("ai.usp_Purpose_Save", new
        {
            Purpose = purpose, ConnectionId = disabled ? null : connectionId,
            Model = disabled || connectionId is null || string.IsNullOrWhiteSpace(model) ? null : model.Trim(), Disabled = disabled, ChangedBy = changedBy,
        }, ct)));
    }

    public async Task SavePolicyAsync(bool aiEnabled, bool allowCloud, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.OpenAsync(ct);
        await conn.ExecuteAsync(Proc("ai.usp_Policy_Save", new { AiEnabled = aiEnabled, AllowCloud = allowCloud, ChangedBy = changedBy }, ct));
    }

    /// <summary>THROW 50400 / 50404 / 50409 -> AiSettingsException with that HTTP status.</summary>
    private static async Task<T> RuleErrors<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new AiSettingsException(ex.Number is >= 50400 and <= 50499 ? ex.Number - 50000 : 400, ex.Message);
        }
    }

    private static CommandDefinition Proc(string name, object? parameters, CancellationToken ct)
        => new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
