namespace LT.ODM.Application.LibraryAssistant.Tools;

/// <summary>
/// How-to guides (ai.Guide): the numbered steps of a fixed process, each linked to its screen. A customer or season in the
/// question fills the links (e.g. New package opens with them). Reads only; the user does the steps.
/// </summary>
public sealed class HowToTool(IGuideRepository guides) : ILibraryTool
{
    public string Name => HowToGuides.ToolName;
    public string Description => "Step-by-step how-to guides for doing things in LT ODM (not looking up data): create a style package and pitch it, add styles to a package, "
        + "share a package with a client, set up a client profile, find what to offer a client, work up a concept, add or reuse a style, compare styles, import styles, "
        + "check BOMs, find styles by picture. Answers 'how do I ...', 'how to ...', 'steps to ...'.";
    public string Example => "How do I create a style package?";
    public IReadOnlyList<string> Parameters => ["guide", "customer", "seasons"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        var all = await guides.ListAsync(ct);
        var guide = a.Guide is null ? null : HowToGuides.Match(a.Guide, all);
        if (guide is null)
        {
            var active = all.Where(g => g.IsActive).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
            var facts = active.Count == 0 ? "There are no how-to guides yet. An Admin adds them in AI Studio > Library assistant > How-to guides."
                : $"I have {ToolKit.N(active.Count)} how-to guides; ask for one by name, e.g. \"how do I {Lower(active[0].Title)}?\"";
            return new LibraryToolResult(facts,
                ToolKit.Table(["guide", "who"], active.Select(g => ((IReadOnlyList<string?>)[g.Title, g.Who], (string?)null)), active.Count),
                [], new { guides = active.Select(g => g.Title) });
        }

        var season = a.Seasons.FirstOrDefault();
        var steps = guide.Steps.Select((s, i) => (Row: (IReadOnlyList<string?>)[(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), s.Text],
            Link: HowToGuides.FillUrl(s.Url, a.Customer, season))).ToList();
        var text = guide.Title + (string.IsNullOrWhiteSpace(guide.Who) ? "" : $" ({guide.Who})") + ":"
            + (string.IsNullOrWhiteSpace(guide.Intro) ? $" {ToolKit.N(steps.Count)} steps." : " " + guide.Intro.Trim());
        IReadOnlyList<LibraryLinkDto> links = steps.FirstOrDefault(s => s.Link is not null).Link is { } first ? [new LibraryLinkDto("howToStart", guide.Title, first)] : [];
        return new LibraryToolResult(text, ToolKit.Table(["step", "what"], steps, steps.Count), links,
            new { guide = guide.Title, who = guide.Who, steps = guide.Steps.Select(s => s.Text) });
    }

    private static string Lower(string title) => title.Length == 0 ? title : char.ToLowerInvariant(title[0]) + title[1..];
}
