using LT.ODM.Application.Abstractions;
using LT.ODM.Application.StyleAi;
using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Application.LibraryAssistant.Tools;

/// <summary>
/// Styles whose pictures look like a description or like another style, with the image model built into the API
/// (Marqo-FashionSigLIP): words or a style's picture against every indexed picture, in milliseconds and without an AI
/// server. Only styles with indexed pictures can be found (AI Studio > Image search > Index pictures).
/// </summary>
public sealed class LookAlikeTool(ImageSearchService images, IStyleRepository styles) : ILibraryTool
{
    public string Name => "look_alike";
    public string Description => "Finds styles whose pictures look like a description (look, e.g. 'navy hooded windbreaker with contrast zips') or like "
        + "another style (styleNo); answers 'styles that look like ...', 'show me something similar to <style>', 'do we have a garment like ...'. "
        + "Only styles with pictures.";
    public string Example => "Show me styles that look like a navy hooded jacket with contrast zips";
    public IReadOnlyList<string> Parameters => ["look", "styleNo", "customer"];

    public async Task<LibraryToolResult> RunAsync(LibraryToolArgs a, CancellationToken ct = default)
    {
        ImageSearchResultDto r;
        string about;
        if (a.StyleNo is not null)
        {
            var style = await ToolKit.FindStyleAsync(styles, a.StyleNo, ct);
            r = await images.SimilarAsync(style.StyleId, a.Customer, ct);
            about = style.StyleNo;
        }
        else if (a.Look is not null)
        {
            if (!(await images.ReadinessAsync(ct)).LookAlikeWords)
                throw new StyleRuleException(400, "Search by look is not available: the image model's text part is not installed or AI features are off.");
            r = await images.SearchByWordsAsync(a.Look, a.Customer, useAiServer: false, ct);
            about = $"“{a.Look}”";
        }
        else throw new StyleRuleException(400, "Describe what the garment looks like, or name a style to compare with.");

        return Result(r.Matches, about, r.Weak, byPicture: r.Mode == "similar");
    }

    /// <summary>The answer for matched pictures (also used by the assistant's picture step, which finds them before any AI).</summary>
    public static LibraryToolResult Result(IReadOnlyList<ImageMatchDto> found, string about, bool weak, bool byPicture)
    {
        var matches = found.Take(12).ToList();
        // Picture-to-picture closeness reads well as a percentage; words-to-picture scores are small, so only the order is shown.
        var facts = matches.Count == 0 ? $"No indexed style picture to compare with {about}. Pictures are searchable after Image search > Index pictures."
            : weak ? $"No style picture looks close to {about}; the nearest are {string.Join(", ", matches.Take(3).Select(m => m.StyleNo))}."
            : $"Styles that look most like {about}: {string.Join(", ", matches.Take(3).Select(m => m.StyleNo))}.";
        return new LibraryToolResult(facts,
            ToolKit.Table(["styleNo", "season", "customer", "productType", "picture", "match"],
                matches.Select((m, i) => ((IReadOnlyList<string?>)[m.StyleNo, m.SeasonCode, m.CustomerCode, m.ProductTypeName, m.Source,
                    byPicture ? $"{Math.Round(m.Similarity * 100)}%" : $"#{i + 1}"], (string?)$"/styles/{m.StyleId}", (string?)m.ImageUrl)),
                matches.Count),
            [new LibraryLinkDto("imageSearch", null, "/ai/image-search")],
            new
            {
                lookedFor = about, weak,
                closest = matches.Take(8).Select((m, i) => new { rank = i + 1, m.StyleNo, season = m.SeasonCode, customer = m.CustomerCode, productType = m.ProductTypeName, m.Garment }),
            });
    }
}
