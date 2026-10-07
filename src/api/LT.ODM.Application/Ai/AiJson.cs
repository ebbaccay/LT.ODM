using System.Text.Json;
using System.Text.Json.Nodes;

namespace LT.ODM.Application.Ai;

/// <summary>Small helpers for building response schemas and reading model answers defensively.</summary>
public static class AiJson
{
    public static JsonObject Str(string? description = null, IEnumerable<string>? values = null)
    {
        var o = new JsonObject { ["type"] = "string" };
        if (description is not null) o["description"] = description;
        if (values?.ToList() is { Count: > 0 } list) o["enum"] = new JsonArray(list.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        return o;
    }

    public static JsonObject Int(string? description = null)
        => description is null ? new JsonObject { ["type"] = "integer" } : new JsonObject { ["type"] = "integer", ["description"] = description };

    public static JsonObject Num() => new() { ["type"] = "number" };

    public static JsonObject Bool() => new() { ["type"] = "boolean" };

    public static JsonObject Arr(JsonObject items) => new() { ["type"] = "array", ["items"] = items };

    public static JsonObject Obj(JsonObject properties, params string[] required)
        => new() { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()) };

    /// <summary>Parses the model's answer; anything but a JSON object becomes a readable AiServiceException.</summary>
    public static JsonDocument Parse(string text)
    {
        try
        {
            var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
        }
        catch (JsonException) { }
        throw new AiServiceException("The AI service returned an answer that could not be read. Try again.");
    }

    public static string Text(JsonElement e, string name, int max)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clip(v.GetString(), max) : "";

    public static IEnumerable<JsonElement> Items(JsonElement e, string name, int max = 100)
        => e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Take(max) : [];

    public static IEnumerable<string> Texts(JsonElement e, string name, int maxItems, int maxLength)
        => Items(e, name, maxItems).Where(i => i.ValueKind == JsonValueKind.String).Select(i => Clip(i.GetString(), maxLength)).Where(s => s.Length > 0);

    public static int? Integer(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return (int)Math.Round(d);
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i) ? i : null;
    }

    public static string Clip(string? value, int max) => (value ?? "").Trim() is var t && t.Length > max ? t[..max].TrimEnd() : t;
}
