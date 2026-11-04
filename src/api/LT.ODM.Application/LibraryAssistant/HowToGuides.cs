using System.Text.RegularExpressions;
using LT.ODM.Application.StyleAi;

namespace LT.ODM.Application.LibraryAssistant;

// How-to guides (ai.Guide): fixed processes in LT ODM ("how do I create a style package?") written once as numbered steps,
// each with a link to the screen. Answered without AI: a question that fits one of a guide's questions (saved question
// patterns with tool how_to, seeded by db/seed/ai.saved-questions.sql or added by an Admin), or a "how do I ..." question
// whose words clearly point to one guide. The assistant still changes nothing: the steps link to the screens, the user acts.
// Admins write and correct guides in AI Studio > Library assistant > How-to guides. See docs/components/library-assistant.md.

/// <summary>One step: what to do, and the app address that does it (null = no link). Url may hold {customer} and {season}.</summary>
public sealed record GuideStepDto(string Text, string? Url);

/// <summary>Questions: the wordings that open this guide at once (ai.SavedQuestion rows with tool how_to and this guide).</summary>
public sealed record GuideDto(
    string Code, string Title, string? Who, string? Intro, IReadOnlyList<GuideStepDto> Steps, bool IsActive,
    IReadOnlyList<string> Questions, string? ChangedBy = null, DateTime? ChangedUtc = null, string? ChangedHow = null);

/// <summary>Admin: a guide as edited (Questions: one wording per item).</summary>
public sealed record SaveGuideRequest(string? Title, string? Who, string? Intro, IReadOnlyList<GuideStepDto>? Steps, bool? IsActive, IReadOnlyList<string>? Questions);

/// <summary>A wording with the pattern key it is matched by (QuestionPatterns).</summary>
public sealed record GuideQuestionKey(string QuestionKey, string Question);

/// <summary>ai.usp_Guide_* (db/procedures/ai.assistant.procedures.sql). 50400 / 50404 -> StyleRuleException.</summary>
public interface IGuideRepository
{
    /// <summary>Every guide (inactive ones too, for the Admin page), with its questions.</summary>
    Task<IReadOnlyList<GuideDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Adds or changes the guide and replaces its questions; a wording another look-up already answers is refused (400).</summary>
    Task SaveAsync(GuideDto guide, IReadOnlyList<GuideQuestionKey> questions, string changedBy, CancellationToken ct = default);

    /// <summary>The guide and its questions.</summary>
    Task DeleteAsync(string code, CancellationToken ct = default);
}

public static partial class HowToGuides
{
    public const string ToolName = "how_to";
    public const int MaxSteps = 20, MaxQuestions = 30;

    /// <summary>"how do I ...", "how to ...", "steps to ...", "what is the process for ...", "where do I ..."</summary>
    [GeneratedRegex(@"^\s*(?:please\s+)?(?:how\s+(?:do|can|should|would|could|will|does)\s+(?:i|we|you|one|someone)\b|how\s+to\b|how\s+is\b|steps?\s+(?:to|for)\b|what\s+(?:are|is)\s+the\s+(?:steps|process|way)\b|where\s+(?:do|can|should)\s+(?:i|we)\b|(?:show|tell|teach)\s+me\s+how\b|(?:can|could)\s+you\s+(?:show|tell)\s+me\s+how\b|guide\s+(?:to|for)\b|process\s+(?:to|for)\b|help\s+me\s+(?:to\s+)?(?:create|make|add|set|share|import|reuse|check|find|send|start)\b|i\s+(?:want|need)\s+to\s+(?:create|make|add|set|share|import|reuse|start|send)\b)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HowToRx();

    public static bool IsHowTo(string question) => HowToRx().IsMatch(question);

    /// <summary>Words that say nothing about which guide: question words, joining words.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "how", "do", "doe", "i", "we", "you", "one", "someone", "can", "could", "should", "would", "will", "to", "a", "an", "the", "is", "are", "what",
        "where", "step", "process", "way", "guide", "please", "show", "tell", "teach", "me", "help", "want", "need", "my", "our", "your", "in", "for",
        "of", "on", "with", "from", "at", "by", "and", "or", "it", "this", "that", "some", "new", "lt", "odm", "app", "system", "up", "into", "go",
        "get", "use", "there", "here", "which", "screen", "page",
    };

    /// <summary>Words that mean the same for finding a guide (singular, lower case).</summary>
    private static readonly Dictionary<string, string> Same = new(StringComparer.Ordinal)
    {
        ["make"] = "create", ["start"] = "create", ["build"] = "create", ["set"] = "create", ["setup"] = "create",
        ["pitch"] = "package", ["proposal"] = "package", ["collection"] = "package", ["presentation"] = "package",
        ["send"] = "share", ["link"] = "share", ["client"] = "customer", ["brand"] = "customer", ["buyer"] = "customer",
        ["copy"] = "reuse", ["duplicate"] = "reuse", ["carry"] = "reuse", ["carryover"] = "reuse", ["upload"] = "import", ["load"] = "import",
        ["excel"] = "import", ["workbook"] = "import", ["idea"] = "concept", ["design"] = "concept", ["bill"] = "bom", ["picture"] = "image",
        ["photo"] = "image", ["sketch"] = "image",
    };

    /// <summary>The words of a text that can tell guides apart.</summary>
    public static IReadOnlySet<string> Words(string text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in LibraryAssistantService.QuestionKey(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!Filler.Contains(w) && w.Length > 1) words.Add(Same.GetValueOrDefault(w, w));
        return words;
    }

    /// <summary>
    /// The guide a text asks for: its code, else the active guide whose title and questions share the most words with it, when
    /// it clearly stands out (more shared words than the next guide, and at least half of the text's words). Null otherwise.
    /// </summary>
    public static GuideDto? Match(string text, IReadOnlyList<GuideDto> guides)
    {
        var active = guides.Where(g => g.IsActive).ToList();
        if (active.FirstOrDefault(g => string.Equals(g.Code, text.Trim(), StringComparison.OrdinalIgnoreCase)) is { } byCode) return byCode;
        var asked = Words(text);
        if (asked.Count == 0) return null;
        var scored = active
            .Select(g => (Guide: g, Score: asked.Count(w => GuideWords(g).Contains(w)), TitleScore: asked.Count(w => Words(g.Title).Contains(w))))
            .OrderByDescending(s => s.Score).ThenByDescending(s => s.TitleScore).ToList();
        if (scored.Count == 0 || scored[0].Score == 0) return null;
        var best = scored[0];
        var next = scored.Count > 1 ? scored[1] : default;
        if (next.Guide is not null && (next.Score, next.TitleScore) == (best.Score, best.TitleScore)) return null;
        return best.Score * 2 >= asked.Count ? best.Guide : null;
    }

    private static HashSet<string> GuideWords(GuideDto g)
    {
        var words = new HashSet<string>(Words(g.Title), StringComparer.Ordinal);
        foreach (var w in Words(g.Code.Replace('_', ' '))) words.Add(w);
        foreach (var q in g.Questions) foreach (var w in Words(q)) words.Add(w);
        return words;
    }

    [GeneratedRegex(@"\{(customer|season)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRx();

    /// <summary>
    /// A step's address with {customer} and {season} filled in (escaped); a query parameter left empty is dropped
    /// ("/packages?new=1&amp;customer=&amp;season=" -> "/packages?new=1").
    /// </summary>
    public static string? FillUrl(string? url, string? customer, string? season)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var filled = PlaceholderRx().Replace(url.Trim(), m => Uri.EscapeDataString((m.Groups[1].Value == "customer" ? customer : season) ?? ""));
        var q = filled.IndexOf('?');
        if (q < 0) return filled;
        var kept = filled[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.EndsWith('=')).ToList();
        return filled[..q] + (kept.Count > 0 ? "?" + string.Join('&', kept) : "");
    }

    /// <summary>Only addresses inside the app: "/..." (not "//host"), no scheme, no spaces.</summary>
    public static bool SafeUrl(string url) => url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\')
        && !url.Any(char.IsWhiteSpace) && !url.Contains(':');

    [GeneratedRegex(@"^[a-z][a-z0-9_]{1,39}$", RegexOptions.CultureInvariant)]
    public static partial Regex CodeRx();
}
