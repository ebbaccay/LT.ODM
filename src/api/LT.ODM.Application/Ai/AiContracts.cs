using System.Text.Json.Nodes;
using LT.ODM.Application.ConceptStudio;

namespace LT.ODM.Application.Ai;

/// <summary>
/// Where AI requests go. LeavesNetwork is true for cloud services (Gemini): whatever goes in the prompt leaves LT.
/// An in-house server keeps the data on the company network. Source: "settings" (Settings > AI connections) or
/// "appsettings" (the server's configuration file).
/// </summary>
public sealed record AiProviderInfo(
    string Provider, string Model, bool IsConfigured, bool LeavesNetwork, bool SupportsReferenceImage = false, string? Source = null);

/// <summary>A text model that must answer with JSON matching a schema. The service is chosen per call (Settings > AI connections).</summary>
public interface IAiJsonClient
{
    Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// One request. <paramref name="schema"/> is standard JSON Schema with lower-case types ("object", "string", ...);
    /// each provider converts it to its own dialect. Returns the model's JSON text; failures become AiServiceExceptions.
    /// </summary>
    Task<string> GenerateJsonAsync(string purpose, string systemInstruction, string userText, JsonObject schema, double temperature, CancellationToken ct = default);
}

/// <summary>An image model. The service is chosen per call (Settings > AI connections).</summary>
public interface IAiImageClient
{
    /// <summary>SupportsReferenceImage false = the provider ignores reference images (the prompt alone is used).</summary>
    Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default);

    /// <summary>Draws one image from the prompt, guided by the reference image (e.g. a flat sketch) when given and supported.</summary>
    Task<GeneratedImage> GenerateImageAsync(string purpose, string prompt, ReferenceImage? reference, CancellationToken ct = default);
}

public sealed record ReferenceImage(byte[] Content, string ContentType);

public sealed record GeneratedImage(byte[] Content, ImageKind Kind);

/// <summary>What the AI pages show about the providers (GET /api/v1/ai/status): the two jobs in use, and every job's status.</summary>
public sealed record AiStatusDto(AiProviderInfo Text, AiProviderInfo Image, IReadOnlyList<AiPurposeStatusDto> Purposes);
