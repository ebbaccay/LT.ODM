using LT.ODM.Application.Translations;

namespace LT.ODM.Application.Abstractions;

/// <summary>The deployed translation files (read only) and the corrections file with its history.</summary>
public interface ITranslationStore
{
    /// <summary>Language codes with a deployed &lt;lang&gt;.json, English first.</summary>
    IReadOnlyList<string> Languages();

    /// <summary>The deployed texts of a language, flattened to dotted keys (empty when the file is missing).</summary>
    IReadOnlyDictionary<string, string> Base(string lang);

    Task<TranslationOverrides> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Read-change-write under one lock. <paramref name="change"/> edits the current corrections and returns true to save
    /// (the version replaced goes to the history). Returns the corrections after the call.
    /// </summary>
    Task<TranslationOverrides> UpdateAsync(Func<TranslationOverrides, bool> change, string updatedBy, string source, CancellationToken ct = default);

    /// <summary>Earlier versions, newest first.</summary>
    Task<IReadOnlyList<TranslationVersionDto>> HistoryAsync(CancellationToken ct = default);

    /// <summary>An earlier version (id from HistoryAsync), or null when there is no such version.</summary>
    Task<TranslationOverrides?> LoadVersionAsync(string id, CancellationToken ct = default);
}

/// <summary>Writes and reads the translations workbook (.xlsx): Key, one column per language.</summary>
public interface ITranslationWorkbook
{
    byte[] Write(TranslationSheet sheet);

    /// <summary>Reads the first sheet (or the one named Translations). Language columns are matched to <paramref name="languages"/>.</summary>
    TranslationSheetReadResult Read(Stream xlsx, IReadOnlyList<string> languages);
}
