using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LT.ODM.Application.Ai;
using LT.ODM.Application.ConceptStudio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>
/// Bound from "Ai": the server's default provider for AI Studio jobs that are not set in Settings > AI connections.
///   Gemini           = Google's cloud API (Ai:Gemini). Prompts, including style and BOM data, leave the company network.
///   OpenAiCompatible = an in-house server with an OpenAI-style API (vLLM, Ollama, LM Studio, LocalAI; Ai:OpenAiCompatible).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";
    public const string Gemini = "Gemini";
    public const string OpenAiCompatible = "OpenAiCompatible";

    /// <summary>Text answers (search filters, change summaries, BOM check notes, render prompts).</summary>
    public string Provider { get; set; } = Gemini;

    /// <summary>Style renders.</summary>
    public string ImageProvider { get; set; } = Gemini;
}

/// <summary>Bound from "Ai:OpenAiCompatible": an in-house model server (default when no connection is set in the app).</summary>
public sealed class OpenAiCompatibleOptions
{
    public const string SectionName = "Ai:OpenAiCompatible";

    /// <summary>Base address including the version, e.g. http://ai-server.lt.local:8000/v1 (vLLM) or http://ai-server:11434/v1 (Ollama).</summary>
    public string Endpoint { get; set; } = "";
    /// <summary>Optional; most in-house servers need none.</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>Text model name as the server knows it, e.g. qwen3-32b.</summary>
    public string Model { get; set; } = "";
    /// <summary>Image server base address when it is not the text server (e.g. LocalAI with a Stable Diffusion / FLUX backend).</summary>
    public string ImageEndpoint { get; set; } = "";
    public string ImageModel { get; set; } = "";
    /// <summary>Size asked of OpenAI-compatible image servers (connections set in the app use it too).</summary>
    public string ImageSize { get; set; } = "1024x1024";
    /// <summary>True when the server runs on the company network. Set false if it points at an outside service.</summary>
    public bool InHouse { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Shared HTTP handling: timeouts and failures become readable AiServiceExceptions; prompts are never logged.</summary>
internal static class AiHttp
{
    public static async Task<JsonNode?> SendAsync(HttpClient http, HttpRequestMessage message, int timeoutSeconds, ILogger logger, string provider, string purpose, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Log the status only: the body can echo the request.
                logger.LogWarning("{Provider} returned {Status} for {Purpose}.", provider, (int)response.StatusCode, purpose);
                throw new AiServiceException("The AI service could not answer. Try again in a minute.");
            }
            return await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiServiceException("The AI service took too long to answer. Try again.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "{Provider} request for {Purpose} failed.", provider, purpose);
            throw new AiServiceException("The AI service could not be reached. Try again in a minute.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new AiServiceException("The AI service returned an answer that could not be read. Try again.", ex);
        }
    }

    public static string Url(string endpoint, string path) => $"{endpoint.TrimEnd('/')}/{path}";

    public static AiProviderInfo Info(AiConnection? c) => AiConnectionInfo.Of(c);
}

/// <summary>Google's Gemini REST API (cloud).</summary>
public static class GeminiApi
{
    private static GeminiOptions Options(AiConnection c) => new() { ApiKey = c.ApiKey ?? "", Model = c.Model, Endpoint = c.Endpoint, TimeoutSeconds = c.TimeoutSeconds };

    public static Task<string> GenerateJsonAsync(HttpClient http, AiConnection c, ILogger logger, string purpose, string system, string user, JsonObject schema,
        double temperature, CancellationToken ct)
        => GeminiJson.GenerateAsync(http, Options(c), logger, purpose, system, user, ToGeminiSchema(schema), temperature, ct);

    public static async Task<GeneratedImage> GenerateImageAsync(HttpClient http, AiConnection c, ILogger logger, string purpose, string prompt,
        ReferenceImage? reference, CancellationToken ct)
    {
        var parts = new JsonArray(new JsonObject { ["text"] = prompt });
        if (reference is not null)
            parts.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = reference.ContentType, ["data"] = Convert.ToBase64String(reference.Content) } });
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts }),
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray("IMAGE"),
                ["imageConfig"] = new JsonObject { ["aspectRatio"] = "3:4" },
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, AiHttp.Url(c.Endpoint, $"models/{Uri.EscapeDataString(c.Model)}:generateContent"))
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("x-goog-api-key", c.ApiKey ?? "");

        var json = await AiHttp.SendAsync(http, message, Math.Max(c.TimeoutSeconds, 120), logger, "Gemini", purpose, ct);
        var data = (json?["candidates"] as JsonArray)?.FirstOrDefault()?["content"]?["parts"] is JsonArray answer
            ? answer.Select(p => p?["inlineData"]?["data"]).OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).FirstOrDefault(s => s is not null)
            : null;
        return AiImages.Decode(data);
    }

    /// <summary>Gemini's responseSchema spells types in upper case ("OBJECT"); property names are left alone.</summary>
    public static JsonObject ToGeminiSchema(JsonObject schema)
    {
        var copy = (JsonObject)schema.DeepClone();
        Visit(copy);
        return copy;

        static void Visit(JsonObject node)
        {
            if (node["type"] is JsonValue t && t.TryGetValue<string>(out var type)) node["type"] = type.ToUpperInvariant();
            if (node["items"] is JsonObject items) Visit(items);
            if (node["properties"] is JsonObject props)
                foreach (var (_, value) in props)
                    if (value is JsonObject child) Visit(child);
        }
    }
}

/// <summary>Servers with an OpenAI-style API (vLLM, Ollama, LM Studio, LocalAI...).</summary>
public static partial class OpenAiApi
{
    private static void Auth(HttpRequestMessage m, AiConnection c)
    {
        if (!string.IsNullOrWhiteSpace(c.ApiKey)) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.ApiKey);
    }

    public static async Task<string> GenerateJsonAsync(HttpClient http, AiConnection c, ILogger logger, string purpose, string system, string user,
        JsonObject schema, double temperature, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = c.Model,
            ["temperature"] = temperature,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
            // vLLM, Ollama and LM Studio constrain the answer to the schema; strict mode is off because not every server supports it.
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "answer", ["schema"] = ToStandardSchema(schema), ["strict"] = false },
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, AiHttp.Url(c.Endpoint, "chat/completions")) { Content = JsonContent.Create(body) };
        Auth(message, c);

        var json = await AiHttp.SendAsync(http, message, c.TimeoutSeconds, logger, c.Name, purpose, ct);
        var text = json?["choices"] is JsonArray { Count: > 0 } choices && choices[0]?["message"]?["content"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        return CleanJson(text) ?? throw new AiServiceException("The AI service returned an empty answer. Try again.");
    }

    public static async Task<GeneratedImage> GenerateImageAsync(HttpClient http, AiConnection c, string size, ILogger logger, string purpose, string prompt,
        CancellationToken ct)
    {
        var body = new JsonObject { ["model"] = c.Model, ["prompt"] = prompt, ["n"] = 1, ["size"] = size, ["response_format"] = "b64_json" };
        using var message = new HttpRequestMessage(HttpMethod.Post, AiHttp.Url(c.Endpoint, "images/generations")) { Content = JsonContent.Create(body) };
        Auth(message, c);

        var json = await AiHttp.SendAsync(http, message, c.TimeoutSeconds, logger, c.Name, purpose, ct);
        var data = json?["data"] is JsonArray { Count: > 0 } items && items[0]?["b64_json"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        return AiImages.Decode(data);
    }

    /// <summary>Standard JSON Schema spells types in lower case ("object"); schemas written for Gemini use upper case.</summary>
    public static JsonObject ToStandardSchema(JsonObject schema)
    {
        var copy = (JsonObject)schema.DeepClone();
        Visit(copy);
        return copy;

        static void Visit(JsonObject node)
        {
            if (node["type"] is JsonValue t && t.TryGetValue<string>(out var type)) node["type"] = type.ToLowerInvariant();
            if (node["items"] is JsonObject items) Visit(items);
            if (node["properties"] is JsonObject props)
                foreach (var (_, value) in props)
                    if (value is JsonObject child) Visit(child);
        }
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkRegex();

    /// <summary>Reasoning models can prefix a &lt;think&gt; block, and some wrap the JSON in a ``` fence.</summary>
    public static string? CleanJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = ThinkRegex().Replace(text, "").Trim();
        if (t.StartsWith("```"))
        {
            var start = t.IndexOf('\n');
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start) t = t[(start + 1)..end].Trim();
        }
        return t.Length == 0 ? null : t;
    }
}

/// <summary>Text answers from the service set for the "text" job (Settings > AI connections, else appsettings).</summary>
public sealed class RoutedAiJsonClient(HttpClient http, IAiConnectionResolver resolver, ILogger<RoutedAiJsonClient> logger) : IAiJsonClient
{
    public async Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => AiConnectionInfo.Of(await resolver.ExplainAsync(AiPurposes.Text, ct));

    public async Task<string> GenerateJsonAsync(string purpose, string systemInstruction, string userText, JsonObject schema, double temperature, CancellationToken ct = default)
    {
        var c = await resolver.ResolveAsync(AiPurposes.Text, ct);
        if (!AiHttp.Info(c).IsConfigured) throw new AiServiceException("AI text answers are not set up on this server.");
        return c!.Kind == AiConnectionKinds.Gemini
            ? await GeminiApi.GenerateJsonAsync(http, c, logger, purpose, systemInstruction, userText, schema, temperature, ct)
            : await OpenAiApi.GenerateJsonAsync(http, c, logger, purpose, systemInstruction, userText, schema, temperature, ct);
    }
}

/// <summary>Images from the service set for the "image" job. Only Gemini uses the reference image (the sketch).</summary>
public sealed class RoutedAiImageClient(HttpClient http, IAiConnectionResolver resolver, IOptions<OpenAiCompatibleOptions> openAi, ILogger<RoutedAiImageClient> logger)
    : IAiImageClient
{
    public async Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => AiConnectionInfo.Of(await resolver.ExplainAsync(AiPurposes.Image, ct));

    public async Task<GeneratedImage> GenerateImageAsync(string purpose, string prompt, ReferenceImage? reference, CancellationToken ct = default)
    {
        var c = await resolver.ResolveAsync(AiPurposes.Image, ct);
        if (!AiHttp.Info(c).IsConfigured) throw new AiServiceException("Image generation is not set up on this server.");
        return c!.Kind == AiConnectionKinds.Gemini
            ? await GeminiApi.GenerateImageAsync(http, c, logger, purpose, prompt, reference, ct)
            : await OpenAiApi.GenerateImageAsync(http, c, openAi.Value.ImageSize, logger, purpose, prompt, ct);
    }
}

internal static class AiImages
{
    /// <summary>Base64 from the model -> a checked PNG / JPEG / WebP (anything else is refused).</summary>
    public static GeneratedImage Decode(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
            throw new AiServiceException("The AI service did not return an image. Try a different description.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new AiServiceException("The AI service returned an image that could not be read. Try again.", ex); }
        if (bytes.Length > ConceptStudioRules.MaxImageBytes || ConceptStudioRules.DetectImage(bytes) is not { } kind || kind == ImageKind.Gif)
            throw new AiServiceException("The AI service returned an image that could not be used. Try again.");
        return new GeneratedImage(bytes, kind);
    }
}
