using LT.ODM.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace LT.ODM.Application.Translations;

/// <summary>
/// Settings > Translations. The deployed files stay the base; a correction is stored only while it differs from the
/// deployed text, so keys a release adds or rewords still come through. Placeholders ({{name}}) must match the English
/// deployed text, otherwise the screen would show a broken sentence.
/// </summary>
public sealed class TranslationService(ITranslationStore store, ITranslationWorkbook workbook, ILogger<TranslationService> logger)
{
    private const string Reference = "en";

    /// <summary>The corrections of one language as key -> text (what the web app layers on the deployed file).</summary>
    public async Task<IReadOnlyDictionary<string, string>> OverridesAsync(string lang, CancellationToken ct)
    {
        if (!store.Languages().Contains(lang, StringComparer.Ordinal)) return new Dictionary<string, string>();
        var current = await store.LoadAsync(ct);
        return current.Languages.TryGetValue(lang, out var entries)
            ? entries.ToDictionary(e => e.Key, e => e.Value.Value, StringComparer.Ordinal)
            : new Dictionary<string, string>();
    }

    public async Task<TranslationTableDto> TableAsync(CancellationToken ct)
    {
        var languages = store.Languages();
        var current = await store.LoadAsync(ct);
        return new TranslationTableDto(languages.Select(Language).ToList(), Rows(languages, current), current.UpdatedBy, current.UpdatedUtc);
    }

    public async Task<TranslationRowDto> SetAsync(SetTranslationRequest request, string user, CancellationToken ct)
    {
        var languages = store.Languages();
        var lang = languages.FirstOrDefault(l => string.Equals(l, request.Lang, StringComparison.OrdinalIgnoreCase))
            ?? throw new TranslationException(400, "Choose one of the app's languages.");
        var key = request.Key?.Trim() ?? "";
        var baseTexts = store.Base(lang);
        if (!AllKeys(languages).Contains(key)) throw new TranslationException(404, "The app does not use this key.");

        var value = TranslationRules.Normalize(request.Value);
        baseTexts.TryGetValue(key, out var baseText);
        if (value is not null && !TranslationRules.SameText(value, baseText)
            && TranslationRules.Check(value, ReferenceText(key, lang)) is { } problem)
            throw new TranslationException(400, problem);

        var saved = await store.UpdateAsync(o => Apply(o, lang, key, value, baseText), user, value is null ? $"Reset {lang} {key}" : $"Edited {lang} {key}", ct);
        logger.LogInformation("Translation {Lang} {Key} {Action} by {User}", lang, key, value is null ? "reset" : "edited", user);
        return Row(key, languages, saved);
    }

    private byte[] Export(TranslationOverrides current)
    {
        var languages = store.Languages();
        var rows = Rows(languages, current).Select((r, i) => new TranslationSheetRow(
            i + 2,
            r.Key,
            languages.ToDictionary(l => l, l => r.Overrides.TryGetValue(l, out var o) ? o : r.Base.GetValueOrDefault(l)),
            r.Overrides.Keys.ToList())).ToList();
        return workbook.Write(new TranslationSheet(languages.Select(Language).ToList(), rows));
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct) => Export(await store.LoadAsync(ct));

    /// <summary>Checks an uploaded workbook against the current corrections; with apply, saves the changes that passed.</summary>
    public async Task<TranslationImportResultDto> ImportAsync(Stream xlsx, string fileName, bool apply, string user, CancellationToken ct)
    {
        var languages = store.Languages();
        var read = workbook.Read(xlsx, languages);
        if (read.Languages is null)
            throw new TranslationException(400, string.Join(" ", read.Problems));

        var problems = read.Problems.Select(p => new TranslationProblemDto(0, null, null, "Error", p)).ToList();
        var changes = Compare(read, Snapshot(await store.LoadAsync(ct)), problems);

        var applied = false;
        if (apply && changes.Count > 0)
        {
            // Compared again under the lock, so a correction saved meanwhile by someone else is not lost.
            var saved = await store.UpdateAsync(o =>
            {
                var fresh = Compare(read, Snapshot(o), []);
                foreach (var c in fresh) Apply(o, c.Lang, c.Key, c.Revert ? null : c.To, store.Base(c.Lang).GetValueOrDefault(c.Key));
                changes = fresh;
                return fresh.Count > 0;
            }, user, $"Uploaded {fileName}", ct);
            applied = changes.Count > 0;
            logger.LogInformation("Translations uploaded by {User} from {File}: {Count} change(s), {Total} correction(s) now", user, fileName, changes.Count, saved.Count);
        }

        return new TranslationImportResultDto(applied, read.Rows.Count, read.Languages, changes,
            problems.Take(TranslationRules.MaxProblemsListed).ToList(), problems.Count);
    }

    public Task<IReadOnlyList<TranslationVersionDto>> HistoryAsync(CancellationToken ct) => store.HistoryAsync(ct);

    /// <summary>Makes an earlier version current again (the current one goes to the history, so this can be undone).</summary>
    public async Task<TranslationVersionDto> RestoreAsync(string id, string user, CancellationToken ct)
    {
        var version = await store.LoadVersionAsync(id, ct) ?? throw new TranslationException(404, "That version is no longer kept.");
        var saved = await store.UpdateAsync(o =>
        {
            o.Languages = version.Languages;
            return true;
        }, user, $"Restored the version of {version.UpdatedUtc:yyyy-MM-dd HH:mm} UTC", ct);
        logger.LogInformation("Translations version {Id} restored by {User}", id, user);
        return new TranslationVersionDto("current", saved.UpdatedUtc, saved.UpdatedBy, saved.Source, saved.Count, true);
    }

    // ----- helpers -----

    private static Dictionary<(string Lang, string Key), string> Snapshot(TranslationOverrides o)
        => o.Languages.SelectMany(l => l.Value.Select(e => (Key: (l.Key, e.Key), e.Value.Value))).ToDictionary(x => x.Key, x => x.Value);

    private static TranslationLanguageDto Language(string code) => new(code, TranslationRules.Label(code));

    private HashSet<string> AllKeys(IReadOnlyList<string> languages)
        => languages.SelectMany(l => store.Base(l).Keys).ToHashSet(StringComparer.Ordinal);

    private string? ReferenceText(string key, string lang)
        => store.Base(Reference).TryGetValue(key, out var en) ? en : store.Base(lang).GetValueOrDefault(key);

    private List<TranslationRowDto> Rows(IReadOnlyList<string> languages, TranslationOverrides current)
    {
        // Keys in the order of the English file (the order developers wrote them), then any only in other files.
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lang in languages)
            foreach (var key in store.Base(lang).Keys)
                if (seen.Add(key)) keys.Add(key);
        return keys.Select(k => Row(k, languages, current)).ToList();
    }

    private TranslationRowDto Row(string key, IReadOnlyList<string> languages, TranslationOverrides current)
    {
        var baseTexts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new List<string>();
        foreach (var lang in languages)
        {
            var baseText = store.Base(lang).GetValueOrDefault(key);
            baseTexts[lang] = baseText;
            if (current.Languages.TryGetValue(lang, out var entries) && entries.TryGetValue(key, out var o))
            {
                overrides[lang] = o.Value;
                if (!string.Equals(o.Base, baseText, StringComparison.Ordinal)) changed.Add(lang);
            }
        }
        return new TranslationRowDto(key, baseTexts, overrides, changed);
    }

    /// <summary>Sets (value) or removes (null, or the deployed text) one correction. True when something changed.</summary>
    private static bool Apply(TranslationOverrides o, string lang, string key, string? value, string? baseText)
    {
        if (!o.Languages.TryGetValue(lang, out var entries))
            o.Languages[lang] = entries = new Dictionary<string, TranslationOverride>(StringComparer.Ordinal);
        if (value is null || TranslationRules.SameText(value, baseText))
            return entries.Remove(key);
        if (entries.TryGetValue(key, out var existing) && existing.Value == value && existing.Base == baseText) return false;
        entries[key] = new TranslationOverride(value, baseText);
        return true;
    }

    /// <summary>What the workbook would change. Problems found on the way are added to <paramref name="problems"/>.</summary>
    private List<TranslationChangeDto> Compare(TranslationSheetReadResult read, IReadOnlyDictionary<(string Lang, string Key), string> current, List<TranslationProblemDto> problems)
    {
        var languages = read.Languages!;
        var known = AllKeys(store.Languages());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<TranslationChangeDto>();

        foreach (var row in read.Rows)
        {
            if (!seen.Add(row.Key))
            {
                problems.Add(new(row.RowNo, row.Key, null, "Error", "This key is on an earlier row too; only the first row is used."));
                continue;
            }
            if (!known.Contains(row.Key))
            {
                problems.Add(new(row.RowNo, row.Key, null, "Warning", "The app does not use this key (renamed or removed in a release); the row is skipped."));
                continue;
            }
            foreach (var lang in languages)
            {
                var baseText = store.Base(lang).GetValueOrDefault(row.Key);
                var value = TranslationRules.Normalize(row.Values.GetValueOrDefault(lang));
                current.TryGetValue((lang, row.Key), out var corrected);
                var before = corrected ?? baseText;

                if (value is null || TranslationRules.SameText(value, baseText))
                {
                    // Blank or back to the deployed text: drop the correction, if there is one.
                    if (corrected is not null) changes.Add(new(row.RowNo, row.Key, lang, before, baseText, true));
                    continue;
                }
                if (TranslationRules.SameText(value, before)) continue;
                if (TranslationRules.Check(value, ReferenceText(row.Key, lang)) is { } problem)
                {
                    problems.Add(new(row.RowNo, row.Key, lang, "Error", problem));
                    continue;
                }
                changes.Add(new(row.RowNo, row.Key, lang, before, value, false));
            }
        }
        return changes;
    }
}
