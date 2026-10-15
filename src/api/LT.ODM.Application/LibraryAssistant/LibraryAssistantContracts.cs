using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.LibraryAssistant;

// AI Studio > Library assistant (/api/v1/ai/library-assistant): questions about the library answered from the database.
// The AI never writes SQL: it picks one read-only tool (ILibraryTool) and its arguments, the app checks them and runs the
// tool's stored procedures, and the AI words a short answer from the rows (numbers are checked against them).
// Adding a capability (costing, workmanship, ...) = one ILibraryTool class registered in DI; see docs/components/library-assistant.md.

/// <summary>Same readers as the Style Library.</summary>
public static class LibraryAssistantRoles
{
    public const string Readers = StyleRoles.Readers;
}

/// <summary>An earlier question in the conversation (kept by the browser, not stored), so follow-ups like "and for women?" work.</summary>
public sealed record LibraryTurnDto(string? Question, string? Tool, IReadOnlyDictionary<string, string>? Arguments);

public sealed record LibraryAskRequest(string? Question, IReadOnlyList<LibraryTurnDto>? History);

/// <summary>
/// Columns are keys the screen translates (ai.assistant.col.*); RowLinks: an app address per row, or null. RowImages: a picture
/// per row (style photo, sketch, matched picture, BOM line picture) shown as a thumbnail, or null when the table has none.
/// </summary>
public sealed record LibraryTableDto(
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, IReadOnlyList<string?> RowLinks, int Total,
    IReadOnlyList<string?>? RowImages = null);

/// <summary>Kind is a key the screen translates (ai.assistant.link.*) with Text as its parameter; Url is an app address.</summary>
public sealed record LibraryLinkDto(string Kind, string? Text, string Url);

/// <summary>What a tool found: Facts is a plain sentence written by code (shown when AI is not used or its answer fails the check);
/// ForModel is the data the AI may word its answer from.</summary>
public sealed record LibraryToolResult(string Facts, LibraryTableDto? Table, IReadOnlyList<LibraryLinkDto> Links, object ForModel);

public static class LibrarySources
{
    /// <summary>The phrase list understood the question; no AI call.</summary>
    public const string Rules = "rules";
    /// <summary>The AI picked the tool (and worded the answer).</summary>
    public const string Ai = "ai";
    /// <summary>A saved question (Rule): its look-up ran again on today's data; no AI call.</summary>
    public const string Saved = "saved";
    /// <summary>Matched by how the pictures look (the image model built into the API); no AI call.</summary>
    public const string Look = "look";
    /// <summary>Not answered: AI could not run and the phrase list understood nothing.</summary>
    public const string None = "none";
}

/// <summary>
/// Source: rules / saved / ai / none. Understood: the AI's one-line reading. Arguments: the checked arguments, for display and follow-ups.
/// AnswerFromFacts: the AI's wording failed the number check (or AI was not used), so the code's sentence is shown.
/// SavedQuestionId: set when the answer can get a thumbs up / down (an AI answer that was recorded, or a saved Rule).
/// </summary>
public sealed record LibraryAnswerDto(
    string Answer, string Source, string? Tool, string? Understood, IReadOnlyDictionary<string, string> Arguments, LibraryTableDto? Table,
    IReadOnlyList<LibraryLinkDto> Links, IReadOnlyList<string> Unmatched, bool AnswerFromFacts, int? SavedQuestionId = null);

/// <summary>A capability, for the screen's "What can I ask?" list.</summary>
public sealed record LibraryToolInfoDto(string Name, string Description, string Example, IReadOnlyList<string> Parameters);

/// <summary>
/// One read-only capability of the assistant. Name, Description and Parameters go into the AI's instructions as they are,
/// so describe which questions it answers in plain words. RunAsync gets checked arguments (only library codes) and must
/// only read. Results go to the AI service in the Text job: list what ForModel holds in docs/ai/data-sent.md.
/// </summary>
public interface ILibraryTool
{
    /// <summary>snake_case, unique; also the key of its screen texts (ai.assistant.tool.{Name}).</summary>
    string Name { get; }

    /// <summary>For the AI: what it looks up and which questions it answers.</summary>
    string Description { get; }

    /// <summary>A question it answers (shown on the screen).</summary>
    string Example { get; }

    /// <summary>Names from LibraryToolArgs.Catalog.</summary>
    IReadOnlyList<string> Parameters { get; }

    Task<LibraryToolResult> RunAsync(LibraryToolArgs args, CancellationToken ct = default);
}

public static class LibraryToolNames
{
    public const string StyleSearch = "style_search";
    public const string LookAlike = "look_alike";
    public const string None = "none";
}
