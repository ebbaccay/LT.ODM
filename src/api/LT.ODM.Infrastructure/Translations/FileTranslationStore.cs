using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Translations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Translations;

/// <summary>
/// Base = the deployed &lt;lang&gt;.json files (re-read when a release replaces them). Corrections = OverridesFolder/overrides.json,
/// written to a temp file and moved into place; the version replaced is copied to OverridesFolder/history first.
/// One web server only: the lock is per process.
/// </summary>
public sealed partial class FileTranslationStore(IOptions<TranslationOptions> options, TimeProvider clock, ILogger<FileTranslationStore> logger) : ITranslationStore
{
    private const string FileName = "overrides.json";
    private const string HistoryFolder = "history";
    private const string IdFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly TranslationOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Lock _baseLock = new();
    private readonly Dictionary<string, (DateTime Written, IReadOnlyDictionary<string, string> Texts)> _base = new(StringComparer.Ordinal);

    [GeneratedRegex(@"^\d{8}T\d{9}Z$")]
    private static partial Regex VersionId();

    private string CurrentPath => Path.Combine(_options.OverridesFolder, FileName);
    private string HistoryPath => Path.Combine(_options.OverridesFolder, HistoryFolder);

    public IReadOnlyList<string> Languages()
    {
        if (!Directory.Exists(_options.BaseFolder))
        {
            logger.LogWarning("Translations folder {Folder} not found (Translations:BaseFolder)", _options.BaseFolder);
            return [];
        }
        return Directory.EnumerateFiles(_options.BaseFolder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(code => TranslationRules.LanguageCode().IsMatch(code))
            .OrderBy(code => code == "en" ? 0 : 1).ThenBy(code => code, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyDictionary<string, string> Base(string lang)
    {
        if (!TranslationRules.LanguageCode().IsMatch(lang)) return new Dictionary<string, string>();
        var path = Path.Combine(_options.BaseFolder, lang + ".json");
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (_baseLock)
        {
            if (_base.TryGetValue(lang, out var cached) && cached.Written == written) return cached.Texts;
            var texts = written == DateTime.MinValue ? new Dictionary<string, string>() : Flatten(path);
            _base[lang] = (written, texts);
            return texts;
        }
    }

    public async Task<TranslationOverrides> LoadAsync(CancellationToken ct = default) => await ReadAsync(CurrentPath, ct) ?? new TranslationOverrides();

    public async Task<TranslationOverrides> UpdateAsync(Func<TranslationOverrides, bool> change, string updatedBy, string source, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = await LoadAsync(ct);
            if (!change(current)) return current;

            foreach (var empty in current.Languages.Where(l => l.Value.Count == 0).Select(l => l.Key).ToList())
                current.Languages.Remove(empty);
            var now = clock.GetUtcNow().UtcDateTime;
            current.UpdatedBy = updatedBy;
            current.UpdatedUtc = now;
            current.Source = source.Length > 300 ? source[..300] : source;

            Directory.CreateDirectory(_options.OverridesFolder);
            if (File.Exists(CurrentPath))
            {
                Directory.CreateDirectory(HistoryPath);
                File.Copy(CurrentPath, Path.Combine(HistoryPath, $"overrides.{now.ToString(IdFormat, CultureInfo.InvariantCulture)}.json"), overwrite: true);
                Prune();
            }
            var temp = CurrentPath + ".tmp";
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(current, Json), ct);
            File.Move(temp, CurrentPath, overwrite: true);
            return current;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TranslationVersionDto>> HistoryAsync(CancellationToken ct = default)
    {
        var versions = new List<TranslationVersionDto>();
        if (await ReadAsync(CurrentPath, ct) is { } current)
            versions.Add(new("current", current.UpdatedUtc, current.UpdatedBy, current.Source, current.Count, true));
        foreach (var id in HistoryIds())
            if (await ReadAsync(VersionPath(id), ct) is { } v)
                versions.Add(new(id, v.UpdatedUtc, v.UpdatedBy, v.Source, v.Count, false));
        return versions;
    }

    public Task<TranslationOverrides?> LoadVersionAsync(string id, CancellationToken ct = default)
        => VersionId().IsMatch(id) ? ReadAsync(VersionPath(id), ct) : Task.FromResult<TranslationOverrides?>(null);

    private string VersionPath(string id) => Path.Combine(HistoryPath, $"overrides.{id}.json");

    /// <summary>History ids, newest first (the id is the time the version was replaced).</summary>
    private List<string> HistoryIds()
        => !Directory.Exists(HistoryPath) ? [] : Directory.EnumerateFiles(HistoryPath, "overrides.*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)["overrides.".Length..])
            .Where(id => VersionId().IsMatch(id))
            .OrderDescending(StringComparer.Ordinal)
            .ToList();

    private void Prune()
    {
        foreach (var id in HistoryIds().Skip(Math.Max(1, _options.HistoryCount)))
        {
            try { File.Delete(VersionPath(id)); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not delete old translations version {Id}", id); }
        }
    }

    private async Task<TranslationOverrides?> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TranslationOverrides>(stream, Json, ct) ?? new TranslationOverrides();
    }

    /// <summary>{"a": {"b": "x"}} -> a.b = x (the keys the web app uses).</summary>
    internal static Dictionary<string, string> Flatten(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(doc.RootElement, "", texts);
        return texts;

        static void Walk(JsonElement e, string prefix, Dictionary<string, string> texts)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Walk(p.Value, prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}", texts);
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in e.EnumerateArray()) Walk(item, $"{prefix}.{i++}", texts);
                    break;
                case JsonValueKind.String:
                    texts[prefix] = e.GetString() ?? "";
                    break;
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    break;
                default:
                    texts[prefix] = e.GetRawText();
                    break;
            }
        }
    }
}
