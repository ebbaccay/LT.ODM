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
    /// <summary>
    /// Renders with a sketch on OpenAI-compatible servers go to images/edits (image-to-image): how far the picture may move
    /// away from the sketch, 0-1. 0.85 turns a flat sketch into a photo that keeps its silhouette and seams (FLUX.1-schnell
    /// on OpenVINO Model Server, tested 2026-10-13; 0.6 still looked like a drawing). 0 = never send the sketch, for servers
    /// without images/edits.
    /// </summary>
    public double ImageSketchStrength { get; set; } = 0.85;
    /// <summary>
    /// Sketch renders on a canvas shaped like the drawing: a tall one (a dress) at ImageSize width x 1.5 height, a wide one
    /// at 1.5 width x height (256x384 / 384x256 for 256x256). The image server must allow those sizes (OpenVINO Model
    /// Server: in the model's graph.pbtxt "resolution" list). False = always ImageSize.
    /// </summary>
    public bool ImageFitShape { get; set; } = true;
    /// <summary>True when the server runs on the company network. Set false if it points at an outside service.</summary>
    public bool InHouse { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Shared HTTP handling: timeouts and failures become readable AiServiceExceptions; prompts are never logged.</summary>
internal static class AiHttp
{
    /// <summary>
    /// The message for a refused request. 404 from an in-house server means the job's model is not loaded there (e.g.
    /// OpenVINO started with -ImageOnly serves only the image model: every Text job got 404, 2026-10-20); retrying
    /// does not help, so say so. Only the status is used: the body can echo the request.
    /// </summary>
    public static AiServiceException Failed(System.Net.HttpStatusCode status)
        => status == System.Net.HttpStatusCode.NotFound
            ? new AiServiceException("The AI service does not have this job's model loaded. Check the model in Settings > AI connections, " +
                "and that the AI server runs it (an OpenVINO server started for images only answers no text jobs).")
            : new AiServiceException("The AI service could not answer. Try again in a minute.");

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
                throw Failed(response.StatusCode);
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

    /// <summary>
    /// A chat completion read as a stream (stream=true), returning the answer text. Reading stops as soon as the text holds
    /// one complete JSON value: some servers (OpenVINO Model Server 2026.4 with Qwen2.5) apply the schema but do not end
    /// the answer after the JSON, and would otherwise generate until max_tokens; closing the connection stops them.
    /// Servers that ignore stream=true and answer with one JSON body are read the usual way.
    /// </summary>
    public static async Task<string?> ChatTextAsync(HttpClient http, HttpRequestMessage message, int timeoutSeconds, ILogger logger, string provider,
        string purpose, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{Provider} returned {Status} for {Purpose}.", provider, (int)response.StatusCode, purpose);
                throw Failed(response.StatusCode);
            }
            if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
            {
                var json = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
                return json?["choices"] is JsonArray { Count: > 0 } choices && choices[0]?["message"]?["content"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);
            var text = new System.Text.StringBuilder();
            var watch = new JsonStreamWatch(OpenAiApi.MaxStringLength);
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                var chunk = JsonNode.Parse(data);
                if (chunk?["error"] is not null)
                {
                    logger.LogWarning("{Provider} reported an error while answering {Purpose}.", provider, purpose);
                    throw new AiServiceException("The AI service could not answer. Try again in a minute.");
                }
                if (chunk?["choices"] is JsonArray { Count: > 0 } c && c[0]?["delta"]?["content"] is JsonValue d && d.TryGetValue<string>(out var part))
                {
                    text.Append(part);
                    AiProgress.Current?.Wrote(part);   // the browser can follow the answer as it is written
                    watch.Feed(part);
                    if (watch.Complete) break;   // the rest would be noise: closing the connection stops the server
                    if (watch.RanOn)
                    {
                        // A model stuck inside one text value (seen with OpenVINO Model Server 2026.4 and a vague instruction).
                        logger.LogWarning("{Provider} answer for {Purpose} ran on inside one value; stopped after {Chars} characters.", provider, purpose, text.Length);
                        throw new AiServiceException("The AI service's answer ran on without finishing. Try again, or use a stronger model for this job.");
                    }
                }
            }
            return text.ToString();
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
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            // The server closed the connection while answering (e.g. it stopped or ran out of memory).
            logger.LogWarning(ex, "{Provider} stopped answering {Purpose}.", provider, purpose);
            throw new AiServiceException("The AI service stopped answering. Try again in a minute.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new AiServiceException("The AI service returned an answer that could not be read. Try again.", ex);
        }
    }

    public static string Url(string endpoint, string path) => $"{endpoint.TrimEnd('/')}/{path}";

    /// <summary>
    /// Follows a streamed answer one character at a time: skips a leading &lt;think&gt; block, then tracks the first JSON
    /// value (depth, strings, escapes). Complete: the value has closed. RanOn: one string grew past the limit (the model
    /// is stuck writing inside it, so the JSON would never close).
    /// </summary>
    internal sealed class JsonStreamWatch(int maxStringLength)
    {
        private readonly System.Text.StringBuilder _before = new();
        private bool _started, _inThink, _inString, _escape;
        private int _depth, _stringLength;

        public bool Complete { get; private set; }
        public bool RanOn { get; private set; }

        public void Feed(string part)
        {
            foreach (var ch in part)
            {
                if (Complete || RanOn) return;
                if (!_started)
                {
                    _before.Append(ch);
                    if (_before.Length > 16) _before.Remove(0, _before.Length - 16);
                    var tail = _before.ToString();
                    if (!_inThink && tail.EndsWith("<think>", StringComparison.Ordinal)) _inThink = true;
                    else if (_inThink && tail.EndsWith("</think>", StringComparison.Ordinal)) _inThink = false;
                    else if (!_inThink && ch is '{' or '[') { _started = true; _depth = 1; }
                    continue;
                }
                if (_inString)
                {
                    if (_escape) _escape = false;
                    else if (ch == '\\') _escape = true;
                    else if (ch == '"') { _inString = false; continue; }
                    if (++_stringLength > maxStringLength) RanOn = true;
                    continue;
                }
                switch (ch)
                {
                    case '"': _inString = true; _stringLength = 0; break;
                    case '{' or '[': _depth++; break;
                    case '}' or ']': if (--_depth == 0) Complete = true; break;
                }
            }
        }
    }

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

    /// <summary>
    /// Longest answer asked for (tokens). The app's longest answers (25 material readings, BOM check notes) need about
    /// 2,000; the cap stops a model that does not end its answer from running until the timeout.
    /// </summary>
    public const int MaxAnswerTokens = 4096;

    /// <summary>
    /// Longest single text value accepted in a streamed answer (characters). The app's longest field is a 1,000-character
    /// description; beyond this the model is taken to be stuck inside the value and the answer is stopped.
    /// </summary>
    public const int MaxStringLength = 3000;

    public static async Task<string> GenerateJsonAsync(HttpClient http, AiConnection c, ILogger logger, string purpose, string system, string user,
        JsonObject schema, double temperature, CancellationToken ct, int maxTokens = 0)
    {
        var body = new JsonObject
        {
            ["model"] = c.Model,
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens > 0 ? Math.Min(maxTokens, MaxAnswerTokens) : MaxAnswerTokens,
            ["stream"] = true,
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

        var text = await AiHttp.ChatTextAsync(http, message, c.TimeoutSeconds, logger, c.Name, purpose, ct);
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

    /// <summary>
    /// Image-to-image (OpenAI images/edits, multipart): the sketch is laid on a white canvas of the requested size, so the
    /// server neither stretches nor crops it, and the model redraws it following the prompt.
    /// </summary>
    public static async Task<GeneratedImage> EditImageAsync(HttpClient http, AiConnection c, string size, double strength, ILogger logger, string purpose,
        string prompt, ReferenceImage sketch, CancellationToken ct, bool fitShape = false)
    {
        var (width, height) = ParseSize(size);
        byte[] canvas;
        try { (canvas, width, height) = Files.ImageOptimizer.SketchCanvas(sketch.Content, width, height, fitShape); }
        catch (UnreadableImageException ex) { throw new AiServiceException("The sketch could not be read. Upload it again or render without it.", ex); }

        using var form = new MultipartFormDataContent
        {
            { new StringContent(c.Model), "model" },
            { new StringContent(prompt), "prompt" },
            { new StringContent("1"), "n" },
            { new StringContent($"{width}x{height}"), "size" },
            { new StringContent(strength.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)), "strength" },
            { new StringContent("b64_json"), "response_format" },
        };
        var image = new ByteArrayContent(canvas);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(image, "image", "sketch.png");
        using var message = new HttpRequestMessage(HttpMethod.Post, AiHttp.Url(c.Endpoint, "images/edits")) { Content = form };
        Auth(message, c);

        var json = await AiHttp.SendAsync(http, message, c.TimeoutSeconds, logger, c.Name, purpose, ct);
        var data = json?["data"] is JsonArray { Count: > 0 } items && items[0]?["b64_json"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        return AiImages.Decode(data);
    }

    /// <summary>"1024x1024" -> (1024, 1024); anything unreadable -> 1024 square.</summary>
    public static (int Width, int Height) ParseSize(string size)
    {
        var parts = size.Split('x', 'X');
        return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w is >= 64 and <= 4096 && h is >= 64 and <= 4096
            ? (w, h)
            : (1024, 1024);
    }

    /// <summary>
    /// One picture plus text to a vision model (Ollama, vLLM, LM Studio: OpenAI image_url content with a data: URL), answer
    /// constrained to the schema like <see cref="GenerateJsonAsync"/>.
    /// </summary>
    public static async Task<string> DescribeJsonAsync(HttpClient http, AiConnection c, ILogger logger, string purpose, string system, string user,
        ReferenceImage image, JsonObject schema, CancellationToken ct)
    {
        var dataUrl = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Content)}";
        var body = new JsonObject
        {
            ["model"] = c.Model,
            ["temperature"] = 0.1,
            ["max_tokens"] = MaxAnswerTokens,
            ["stream"] = true,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "text", ["text"] = user },
                        new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = dataUrl } }),
                }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "answer", ["schema"] = ToStandardSchema(schema), ["strict"] = false },
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, AiHttp.Url(c.Endpoint, "chat/completions")) { Content = JsonContent.Create(body) };
        Auth(message, c);

        var text = await AiHttp.ChatTextAsync(http, message, c.TimeoutSeconds, logger, c.Name, purpose, ct);
        return CleanJson(text) ?? throw new AiServiceException("The AI service returned an empty answer. Try again.");
    }

    /// <summary>POST /embeddings with one text; returns the first vector.</summary>
    public static async Task<float[]> EmbedAsync(HttpClient http, AiConnection c, ILogger logger, string purpose, string text, CancellationToken ct)
    {
        var body = new JsonObject { ["model"] = c.Model, ["input"] = text };
        using var message = new HttpRequestMessage(HttpMethod.Post, AiHttp.Url(c.Endpoint, "embeddings")) { Content = JsonContent.Create(body) };
        Auth(message, c);

        var json = await AiHttp.SendAsync(http, message, c.TimeoutSeconds, logger, c.Name, purpose, ct);
        if (json?["data"] is not JsonArray { Count: > 0 } data || data[0]?["embedding"] is not JsonArray numbers || numbers.Count == 0)
            throw new AiServiceException("The embedding model returned no vector. Check that the Embedding job uses an embedding model.");
        try
        {
            return numbers.Select(n => n!.GetValue<float>()).ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or NullReferenceException)
        {
            throw new AiServiceException("The embedding model returned a vector that could not be read.", ex);
        }
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

    /// <summary>
    /// Reasoning models can prefix a &lt;think&gt; block, some wrap the JSON in a ``` fence, and some go on writing after
    /// it: the first complete JSON value is kept (or the cleaned text when there is none, which then fails to parse).
    /// </summary>
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
        if (FirstJsonValue(t) is { } json) return json;
        return t.Length == 0 ? null : t;
    }

    /// <summary>The first complete top-level JSON object or array in the text (strings and escapes respected), or null.</summary>
    public static string? FirstJsonValue(string text)
    {
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0) return null;
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (ch == '\\') i++;
                else if (ch == '"') inString = false;
                continue;
            }
            switch (ch)
            {
                case '"': inString = true; break;
                case '{' or '[': depth++; break;
                case '}' or ']':
                    if (--depth == 0) return text[start..(i + 1)];
                    break;
            }
        }
        return null;
    }
}

/// <summary>Text answers from the service set for the "text" job (Settings > AI connections, else appsettings).</summary>
public sealed class RoutedAiJsonClient(HttpClient http, IAiConnectionResolver resolver, AiCallLimiter limiter, ILogger<RoutedAiJsonClient> logger) : IAiJsonClient
{
    public async Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => AiConnectionInfo.Of(await resolver.ExplainAsync(AiPurposes.Text, ct));

    public async Task<string> GenerateJsonAsync(string purpose, string systemInstruction, string userText, JsonObject schema, double temperature, CancellationToken ct = default,
        int maxTokens = 0)
    {
        var c = await resolver.ResolveAsync(AiPurposes.Text, ct);
        if (!AiHttp.Info(c).IsConfigured) throw new AiServiceException("AI text answers are not set up on this server.");
        using var place = await limiter.EnterAsync(c!, ct);
        return c!.Kind == AiConnectionKinds.Gemini
            ? await GeminiApi.GenerateJsonAsync(http, c, logger, purpose, systemInstruction, userText, schema, temperature, ct)
            : await OpenAiApi.GenerateJsonAsync(http, c, logger, purpose, systemInstruction, userText, schema, temperature, ct, maxTokens);
    }
}

/// <summary>Images from the service set for the "image" job. Only Gemini uses the reference image (the sketch).</summary>
public sealed class RoutedAiImageClient(
    HttpClient http, IAiConnectionResolver resolver, AiCallLimiter limiter, IOptions<OpenAiCompatibleOptions> openAi, ILogger<RoutedAiImageClient> logger) : IAiImageClient
{
    /// <summary>OpenAI-compatible servers take the sketch too (images/edits) unless ImageSketchStrength is 0.</summary>
    public async Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var r = await resolver.ExplainAsync(AiPurposes.Image, ct);
        var info = AiConnectionInfo.Of(r);
        return r.Connection?.Kind == AiConnectionKinds.OpenAiCompatible && SketchStrength > 0 ? info with { SupportsReferenceImage = true, SketchAsStartImage = true } : info;
    }

    public async Task<GeneratedImage> GenerateImageAsync(string purpose, string prompt, ReferenceImage? reference, CancellationToken ct = default)
    {
        var c = await resolver.ResolveAsync(AiPurposes.Image, ct);
        if (!AiHttp.Info(c).IsConfigured) throw new AiServiceException("Image generation is not set up on this server.");
        using var place = await limiter.EnterAsync(c!, ct);
        if (c!.Kind == AiConnectionKinds.Gemini) return await GeminiApi.GenerateImageAsync(http, c, logger, purpose, prompt, reference, ct);
        return reference is not null && SketchStrength > 0
            ? await OpenAiApi.EditImageAsync(http, c, openAi.Value.ImageSize, SketchStrength, logger, purpose, prompt, reference, ct, openAi.Value.ImageFitShape)
            : await OpenAiApi.GenerateImageAsync(http, c, openAi.Value.ImageSize, logger, purpose, prompt, ct);
    }

    private double SketchStrength => Math.Clamp(openAi.Value.ImageSketchStrength, 0, 1);
}

/// <summary>
/// Picture reading from the service set for the "vision" job. In-house (OpenAI-compatible) servers only: image search must
/// not send library pictures to a cloud service, and no Gemini path is built for it.
/// </summary>
public sealed class RoutedAiVisionClient(HttpClient http, IAiConnectionResolver resolver, AiCallLimiter limiter, ILogger<RoutedAiVisionClient> logger) : IAiVisionClient
{
    /// <summary>Longest side sent to the model: enough to see garment details, and far fewer image tokens on a CPU server.</summary>
    public const int MaxDimension = 768;

    public async Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => AiConnectionInfo.Of(await resolver.ExplainAsync(AiPurposes.Vision, ct));

    public async Task<string> DescribeJsonAsync(string purpose, string systemInstruction, string userText, ReferenceImage image, JsonObject schema,
        CancellationToken ct = default)
    {
        var c = await AiRouting.InHouseAsync(resolver, AiPurposes.Vision, "Picture reading", ct);
        var kind = ConceptStudioRules.DetectImage(image.Content) ?? throw new AiServiceException("The picture could not be read.");
        byte[] small;
        ImageKind smallKind;
        try
        {
            (small, smallKind) = Files.ImageOptimizer.Optimize(image.Content, kind, MaxDimension);
        }
        catch (UnreadableImageException ex)
        {
            throw new AiServiceException("The picture could not be read.", ex);
        }
        var sent = new ReferenceImage(small, ConceptStudioRules.ContentType(ConceptStudioRules.Extension(smallKind)));
        using var place = await limiter.EnterAsync(c, ct);
        return await OpenAiApi.DescribeJsonAsync(http, c, logger, purpose, systemInstruction, userText, sent, schema, ct);
    }
}

/// <summary>Vectors from the service set for the "embedding" job (in-house OpenAI-compatible servers only).</summary>
public sealed class RoutedAiEmbeddingClient(HttpClient http, IAiConnectionResolver resolver, AiCallLimiter limiter, ILogger<RoutedAiEmbeddingClient> logger)
    : IAiEmbeddingClient
{
    public async Task<AiProviderInfo> GetInfoAsync(CancellationToken ct = default) => AiConnectionInfo.Of(await resolver.ExplainAsync(AiPurposes.Embedding, ct));

    public async Task<float[]> EmbedAsync(string purpose, string text, CancellationToken ct = default)
    {
        var c = await AiRouting.InHouseAsync(resolver, AiPurposes.Embedding, "Embeddings", ct);
        using var place = await limiter.EnterAsync(c, ct);
        return await OpenAiApi.EmbedAsync(http, c, logger, purpose, text, ct);
    }
}

internal static class AiRouting
{
    /// <summary>The job's connection when it can run on an OpenAI-compatible server; otherwise a readable AiServiceException.</summary>
    public static async Task<AiConnection> InHouseAsync(IAiConnectionResolver resolver, string purpose, string feature, CancellationToken ct)
    {
        var c = await resolver.ResolveAsync(purpose, ct);
        if (!AiHttp.Info(c).IsConfigured) throw new AiServiceException($"{feature} is not set up on this server.");
        if (c!.Kind != AiConnectionKinds.OpenAiCompatible)
            throw new AiServiceException($"{feature} runs only on an OpenAI-compatible server (in-house Ollama, vLLM or LM Studio). Change the job in Settings > AI connections.");
        return c;
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
