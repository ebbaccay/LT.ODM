using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LT.ODM.Application.Ai;
using Microsoft.Extensions.Logging;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>
/// Bound from "Ai:Gemini": the server's default Gemini connection, used by AI jobs not set in Settings > AI connections.
/// The API key comes from user secrets / the Ai__Gemini__ApiKey environment variable, never a file.
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Ai:Gemini";

    public string ApiKey { get; set; } = "";
    /// <summary>Same model as TMS.</summary>
    public string Model { get; set; } = "gemini-2.5-flash";
    /// <summary>AI Studio style renders (Ai:ImageProvider = Gemini).</summary>
    public string ImageModel { get; set; } = "gemini-2.5-flash-image";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// One Gemini generateContent call that must answer with JSON matching <paramref name="schema"/>.
/// The API key goes in the x-goog-api-key header (never the URL); errors become readable AiServiceExceptions.
/// </summary>
internal static class GeminiJson
{
    public static async Task<string> GenerateAsync(
        HttpClient http, GeminiOptions options, ILogger logger, string purpose,
        string systemInstruction, string userText, JsonNode schema, double temperature, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = systemInstruction }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = userText }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = temperature,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = schema.DeepClone(),
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.Endpoint.TrimEnd('/')}/models/{Uri.EscapeDataString(options.Model)}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add("x-goog-api-key", options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Log the status only: the body can echo the request.
                logger.LogWarning("Gemini returned {Status} for {Purpose}.", (int)response.StatusCode, purpose);
                throw new AiServiceException("The AI service could not answer. Try again in a minute.");
            }
            var json = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
            return FirstText(json) ?? throw new AiServiceException("The AI service returned an empty answer. Try again.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiServiceException("The AI service took too long to answer. Try again.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Gemini request for {Purpose} failed.", purpose);
            throw new AiServiceException("The AI service could not be reached. Try again in a minute.", ex);
        }
    }

    /// <summary>candidates[0].content.parts[0].text, or null when any part is missing (e.g. a blocked response has no candidates).</summary>
    private static string? FirstText(JsonNode? json)
        => json?["candidates"] is JsonArray { Count: > 0 } candidates
           && candidates[0]?["content"]?["parts"] is JsonArray { Count: > 0 } parts
           && parts[0]?["text"] is JsonValue text && text.TryGetValue<string>(out var value)
            ? value
            : null;

    public static string Clip(string? value, int max) => (value ?? "").Trim() is var t && t.Length > max ? t[..max] : (value ?? "").Trim();
}
