using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.LibraryAssistant;

// Saved questions (ai.SavedQuestion): questions the AI answered, kept with the look-up and arguments it chose - never the
// answer. A Rule answers the same question (after normalising) at once, without AI, by running that look-up on today's data.
// A thumbs up makes a Rule; a thumbs down on any answer removes it; Admins see, correct and delete them.

public static class SavedQuestionStatuses
{
    /// <summary>The AI answered it; the AI is still used.</summary>
    public const string Recorded = "Recorded";
    /// <summary>Answered from the saved look-up, without AI.</summary>
    public const string Rule = "Rule";
    /// <summary>Taken out (thumbs down or an Admin); the AI is used.</summary>
    public const string Removed = "Removed";
    public static readonly string[] All = [Recorded, Rule, Removed];
}

/// <summary>Who may see the saved-question list and change it.</summary>
public static class SavedQuestionRoles
{
    public const string Admins = "Admin";
}

public sealed record SavedRuleDto(int SavedQuestionId, string Question, string Tool, IReadOnlyDictionary<string, string> Arguments);

/// <summary>AskCount: answered by the AI; RuleHits: answered from the saved look-up. ChangedHow: Feedback or Admin.</summary>
public sealed record SavedQuestionDto(
    int SavedQuestionId, string Question, string Tool, IReadOnlyDictionary<string, string> Arguments, string Status, int AskCount, int RuleHits,
    DateTime LastAskedUtc, DateTime CreatedUtc, string? ChangedBy, DateTime? ChangedUtc, string? ChangedHow);

/// <summary>Helpful: thumbs up (make it a Rule with the tool and arguments shown) or down (remove it).</summary>
public sealed record LibraryFeedbackRequest(int SavedQuestionId, bool Helpful, string? Tool, IReadOnlyDictionary<string, string>? Arguments);

/// <summary>Admin: the status and corrected arguments (argument name -> text, as shown with answers).</summary>
public sealed record SaveSavedQuestionRequest(string? Status, IReadOnlyDictionary<string, string>? Arguments);

/// <summary>ai.usp_SavedQuestion_* (db/procedures/ai.assistant.procedures.sql). 50400 / 50404 -> StyleRuleException.</summary>
public interface ISavedQuestionRepository
{
    /// <summary>The Rule for this question key (counts the hit), or null.</summary>
    Task<SavedRuleDto?> UseRuleAsync(string questionKey, CancellationToken ct = default);

    /// <summary>After the AI answered: records or counts the question; returns its id.</summary>
    Task<int> RecordAsync(string questionKey, string question, string tool, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default);

    Task FeedbackAsync(int savedQuestionId, bool helpful, string tool, IReadOnlyDictionary<string, string> arguments, string changedBy, CancellationToken ct = default);

    Task<PagedResult<SavedQuestionDto>> ListAsync(string? status, string? search, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<string>> PopularAsync(int take, CancellationToken ct = default);

    Task SaveAsync(int savedQuestionId, string status, IReadOnlyDictionary<string, string> arguments, string changedBy, CancellationToken ct = default);

    Task DeleteAsync(int savedQuestionId, CancellationToken ct = default);
}
