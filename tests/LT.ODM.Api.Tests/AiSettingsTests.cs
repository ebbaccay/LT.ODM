using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>The Text job on a Gemini connection over a stub HTTP handler (exercises the real request and answer handling).</summary>
public static class TestAiText
{
    public static IAiJsonClient Gemini(HttpMessageHandler handler, string key = "k")
        => new LT.ODM.Infrastructure.Ai.RoutedAiJsonClient(new HttpClient(handler),
            new FixedResolver(new AiConnection("Gemini", AiConnectionKinds.Gemini, "https://generativelanguage.googleapis.com/v1beta", key, "gemini-2.5-flash", false, 60, "test")),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LT.ODM.Infrastructure.Ai.RoutedAiJsonClient>.Instance);

    private sealed class FixedResolver(AiConnection connection) : IAiConnectionResolver
    {
        public Task<AiConnection?> ResolveAsync(string purpose, CancellationToken ct = default) => Task.FromResult<AiConnection?>(connection);
        public Task<AiResolution> ExplainAsync(string purpose, CancellationToken ct = default) => Task.FromResult(new AiResolution(connection, null));
        public void Invalidate() { }
    }
}

/// <summary>Resolves every job to nothing (AI Studio unit tests do not need connections).</summary>
public sealed class FakeAiConnectionResolver : IAiConnectionResolver
{
    public Task<AiConnection?> ResolveAsync(string purpose, CancellationToken ct = default) => Task.FromResult<AiConnection?>(null);
    public Task<AiResolution> ExplainAsync(string purpose, CancellationToken ct = default) => Task.FromResult(new AiResolution(null, null));
    public void Invalidate() { }
}

/// <summary>In-memory ai.* tables with the procedures' rules.</summary>
public sealed class FakeAiSettingsRepository : IAiSettingsRepository
{
    private readonly object _lock = new();
    private readonly List<AiConnectionRow> _connections = [];
    private readonly Dictionary<string, AiPurposeRow> _purposes = AiPurposes.All.ToDictionary(p => p, p => new AiPurposeRow(p, null, null, null, null));
    private long _version;
    private AiPolicy _policy = new(true, true, null, null);

    private byte[] NextVersion() => BitConverter.GetBytes(Interlocked.Increment(ref _version));

    public Task<AiSettingsData> GetAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(new AiSettingsData(_connections.ToList(), _purposes.Values.ToList(), _policy));
    }

    public Task SavePolicyAsync(bool aiEnabled, bool allowCloud, string changedBy, CancellationToken ct = default)
    {
        lock (_lock) _policy = new AiPolicy(aiEnabled, allowCloud, changedBy, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task<int> SaveConnectionAsync(int? connectionId, SaveAiConnectionRequest r, string keyAction, string? apiKeyProtected, string? apiKeyHint,
        string changedBy, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_connections.Any(c => c.Name == r.Name.Trim() && c.ConnectionId != connectionId))
                throw new AiSettingsException(409, "Another connection already has this name.");
            if (connectionId is null)
            {
                var id = _connections.Count + 1;
                _connections.Add(new AiConnectionRow(id, r.Name.Trim(), r.Kind, r.Endpoint, keyAction == "set" ? apiKeyProtected : null,
                    keyAction == "set" ? apiKeyHint : null, r.InHouse, r.TimeoutSeconds, r.Notes, r.IsActive, changedBy, DateTime.UtcNow, NextVersion()));
                return Task.FromResult(id);
            }
            var i = _connections.FindIndex(c => c.ConnectionId == connectionId);
            if (i < 0) throw new AiSettingsException(404, "The connection was not found.");
            var old = _connections[i];
            if (!old.RowVer.SequenceEqual(r.RowVer!)) throw new AiSettingsException(409, "Someone else changed this connection.");
            _connections[i] = old with
            {
                Name = r.Name.Trim(), Kind = r.Kind, Endpoint = r.Endpoint, InHouse = r.InHouse, TimeoutSeconds = r.TimeoutSeconds, Notes = r.Notes,
                IsActive = r.IsActive, UpdatedBy = changedBy, RowVer = NextVersion(),
                ApiKeyProtected = keyAction switch { "set" => apiKeyProtected, "clear" => null, _ => old.ApiKeyProtected },
                ApiKeyHint = keyAction switch { "set" => apiKeyHint, "clear" => null, _ => old.ApiKeyHint },
            };
            return Task.FromResult(connectionId.Value);
        }
    }

    public Task DeleteConnectionAsync(int connectionId, byte[] rowVer, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_purposes.Values.Any(p => p.ConnectionId == connectionId)) throw new AiSettingsException(409, "A job uses this connection.");
            _connections.RemoveAll(c => c.ConnectionId == connectionId);
        }
        return Task.CompletedTask;
    }

    public Task SavePurposeAsync(string purpose, int? connectionId, string? model, bool disabled, string changedBy, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (disabled)
            {
                _purposes[purpose] = new AiPurposeRow(purpose, null, null, changedBy, DateTime.UtcNow, Disabled: true);
                return Task.CompletedTask;
            }
            if (connectionId is { } id && !_connections.Any(c => c.ConnectionId == id && c.IsActive)) throw new AiSettingsException(400, "Choose an active connection.");
            _purposes[purpose] = new AiPurposeRow(purpose, connectionId, connectionId is null ? null : model, changedBy, DateTime.UtcNow);
        }
        return Task.CompletedTask;
    }
}

public sealed class AiSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";
    private const string Key = "sk-test-0123456789-ABCD";

    private async Task<HttpClient> SignInAsync(string role)
    {
        var name = "ais" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, [role]);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static object Connection(string name, string? apiKey = null, byte[]? rowVer = null, bool clear = false, string endpoint = "http://ai-server.lt.local:8000/v1")
        => new { rowVer, name, kind = "OpenAiCompatible", endpoint, apiKey, clearApiKey = clear, inHouse = true, timeoutSeconds = 120, notes = (string?)null, isActive = true };

    [Fact]
    public async Task Only_admins_manage_connections_and_keys_never_come_back()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Merchandiser")).GetAsync("/api/v1/admin/ai")).StatusCode);

        var admin = await SignInAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/ai/connections", Connection("Bad", endpoint: "ftp://x"))).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/v1/admin/ai/connections", Connection("LT AI server", Key));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await (await admin.GetAsync("/api/v1/admin/ai")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(Key, body);                           // not in clear
        Assert.DoesNotContain("apiKeyProtected", body, StringComparison.OrdinalIgnoreCase);   // nor encrypted

        var settings = await admin.GetFromJsonAsync<AiSettingsDto>("/api/v1/admin/ai");
        var c = Assert.Single(settings!.Connections, x => x.Name == "LT AI server");
        Assert.Equal((true, "ABCD"), (c.HasApiKey, c.ApiKeyHint));
        Assert.Equal(6, settings.Purposes.Count);

        // A blank key keeps the saved one; ClearApiKey removes it.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/admin/ai/connections/{c.ConnectionId}", Connection("LT AI server", null, c.RowVer))).StatusCode);
        c = (await admin.GetFromJsonAsync<AiSettingsDto>("/api/v1/admin/ai"))!.Connections.Single(x => x.ConnectionId == c.ConnectionId);
        Assert.True(c.HasApiKey);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/admin/ai/connections/{c.ConnectionId}", Connection("LT AI server", null, [9, 9, 9, 9, 9, 9, 9, 9]))).StatusCode);
        await admin.PutAsJsonAsync($"/api/v1/admin/ai/connections/{c.ConnectionId}", Connection("LT AI server", null, c.RowVer, clear: true));
        Assert.False((await admin.GetFromJsonAsync<AiSettingsDto>("/api/v1/admin/ai"))!.Connections.Single(x => x.ConnectionId == c.ConnectionId).HasApiKey);
    }

    [Fact]
    public async Task Jobs_point_at_a_connection_and_the_app_uses_it_with_the_decrypted_key()
    {
        var admin = await SignInAsync("Admin");
        var id = (await (await admin.PostAsJsonAsync("/api/v1/admin/ai/connections", Connection("Vision box", Key))).Content.ReadFromJsonAsync<IdBody>())!.ConnectionId;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/vision", new { connectionId = id, model = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/telepathy", new { connectionId = id, model = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/vision", new { connectionId = id, model = "qwen2.5-vl-7b" })).StatusCode);

        var resolver = factory.Services.GetRequiredService<IAiConnectionResolver>();
        var c = await resolver.ResolveAsync(AiPurposes.Vision);
        Assert.Equal(("Vision box", "qwen2.5-vl-7b", Key, "settings", true), (c!.Name, c.Model, c.ApiKey, c.Source, c.InHouse));
        Assert.False(AiConnectionInfo.Of(c).LeavesNetwork);

        // The job is in use: the connection cannot be deleted until the job points elsewhere.
        var rowVer = (await admin.GetFromJsonAsync<AiSettingsDto>("/api/v1/admin/ai"))!.Connections.Single(x => x.ConnectionId == id).RowVer;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/admin/ai/connections/{id}?rowVer={Uri.EscapeDataString(Convert.ToBase64String(rowVer))}")).StatusCode);
        await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/vision", new { connectionId = (int?)null, model = (string?)null });
        Assert.Null(await resolver.ResolveAsync(AiPurposes.Vision));   // AI Lab jobs have no appsettings default
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/admin/ai/connections/{id}?rowVer={Uri.EscapeDataString(Convert.ToBase64String(rowVer))}")).StatusCode);
    }

    [Fact]
    public async Task Central_switches_block_cloud_services_and_turn_jobs_or_all_ai_off()
    {
        var admin = await SignInAsync("Admin");
        var resolver = factory.Services.GetRequiredService<IAiConnectionResolver>();
        var cloud = (await (await admin.PostAsJsonAsync("/api/v1/admin/ai/connections",
            new { name = "Cloud box", kind = "Gemini", endpoint = "https://generativelanguage.googleapis.com/v1beta", apiKey = "g-key-1234",
                  clearApiKey = false, inHouse = false, timeoutSeconds = 60, isActive = true })).Content.ReadFromJsonAsync<IdBody>())!.ConnectionId;
        var local = (await (await admin.PostAsJsonAsync("/api/v1/admin/ai/connections", Connection("LT box"))).Content.ReadFromJsonAsync<IdBody>())!.ConnectionId;
        await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/embedding", new { connectionId = cloud, model = "text-embedding" });
        await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/document", new { connectionId = local, model = "qwen2.5-vl" });
        try
        {
            // Cloud not allowed: the cloud job is blocked (nothing would be sent), the in-house one still runs.
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/admin/ai/policy", new { aiEnabled = true, allowCloud = false })).StatusCode);
            Assert.Null(await resolver.ResolveAsync(AiPurposes.Embedding));
            Assert.Equal(AiBlocks.CloudBlocked, (await resolver.ExplainAsync(AiPurposes.Embedding)).Blocked);
            Assert.Equal("LT box", (await resolver.ResolveAsync(AiPurposes.Document))!.Name);
            Assert.Contains("cloud AI is not allowed", AiUnavailable.Message("Search", AiConnectionInfo.Of(await resolver.ExplainAsync(AiPurposes.Embedding))));

            // A job turned off has no service at all, not even the app default.
            await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/document", new { connectionId = (int?)null, model = (string?)null, off = true });
            Assert.Equal(AiBlocks.JobOff, (await resolver.ExplainAsync(AiPurposes.Document)).Blocked);
            var settings = await admin.GetFromJsonAsync<AiSettingsDto>("/api/v1/admin/ai");
            Assert.True(settings!.Purposes.Single(p => p.Purpose == "document").Disabled);
            Assert.False(settings.Policy.AllowCloud);

            // Master switch off: everything is blocked.
            await admin.PutAsJsonAsync("/api/v1/admin/ai/policy", new { aiEnabled = false, allowCloud = true });
            Assert.Equal(AiBlocks.AiOff, (await resolver.ExplainAsync(AiPurposes.Embedding)).Blocked);
            Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("Merchandiser")).PutAsJsonAsync("/api/v1/admin/ai/policy", new { aiEnabled = true, allowCloud = true })).StatusCode);
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/v1/admin/ai/policy", new { aiEnabled = true, allowCloud = true });
            await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/embedding", new { connectionId = (int?)null, model = (string?)null });
            await admin.PutAsJsonAsync("/api/v1/admin/ai/purposes/document", new { connectionId = (int?)null, model = (string?)null });
        }
    }

    private sealed record IdBody(int ConnectionId);
}
