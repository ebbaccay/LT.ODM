using LT.ODM.Application.Ai;
using LT.ODM.Application.LibraryAssistant;
using LT.ODM.Application.StyleLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// AI Studio > Library assistant: questions about the Style Library answered from the database (phrase list, then saved
/// questions, then the Text job picks one read-only tool). Readers of the Style Library; the saved-question list is Admin.
/// Asking is AI rate limited. The conversation is not stored; questions the AI answered are (ai.SavedQuestion, no names).
/// Closing the page or Cancel aborts the request: the token cancels the call to the AI server, which stops generating.
/// </summary>
[ApiController]
[Route("api/v1/ai/library-assistant")]
[Authorize(Roles = LibraryAssistantRoles.Readers)]
public sealed class LibraryAssistantController(LibraryAssistantService assistant, ISavedQuestionRepository saved) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    /// <summary>What the assistant can look up (for "What can I ask?").</summary>
    [HttpGet("tools")]
    public IReadOnlyList<LibraryToolInfoDto> Tools() => assistant.Tools();

    /// <summary>The most used saved questions (answered at once), as suggestions.</summary>
    [HttpGet("popular")]
    public Task<IReadOnlyList<string>> Popular(CancellationToken ct) => saved.PopularAsync(6, ct);

    [HttpPost("ask")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<IActionResult> Ask(LibraryAskRequest request, CancellationToken ct)
    {
        var question = request.Question?.Trim() ?? "";
        if (question.Length is < 3 or > 500)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["question"] = ["Ask a question of 3 to 500 characters."] }));
        // Earlier turns come from the browser: keep the last few, clipped.
        var history = (request.History ?? []).TakeLast(4)
            .Select(h => new LibraryTurnDto(Clip(h.Question, 500), Clip(h.Tool, 40),
                h.Arguments?.Take(12).ToDictionary(a => Clip(a.Key, 30) ?? "", a => Clip(a.Value, 200) ?? "")))
            .ToList();
        return await Run(async () => Ok(await assistant.AskAsync(question, history, ct)), ct);
    }

    /// <summary>Thumbs up / down on an answer (any reader).</summary>
    [HttpPost("feedback")]
    public Task<IActionResult> Feedback(LibraryFeedbackRequest request, CancellationToken ct)
        => Run(async () =>
        {
            await assistant.FeedbackAsync(request, CurrentUserName, ct);
            return NoContent();
        }, ct);

    // ----- Admin: saved questions -----

    [HttpGet("saved")]
    [Authorize(Roles = SavedQuestionRoles.Admins)]
    public async Task<IActionResult> List(string? status, string? search, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        if (status is not null && !SavedQuestionStatuses.All.Contains(status))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Status must be Recorded, Rule or Removed."] }));
        return Ok(await saved.ListAsync(status, Clip(search, 100), Math.Max(0, skip), Math.Clamp(take, 1, 200), ct));
    }

    [HttpPut("saved/{id:int}")]
    [Authorize(Roles = SavedQuestionRoles.Admins)]
    public Task<IActionResult> Save(int id, SaveSavedQuestionRequest request, CancellationToken ct)
        => Run(async () =>
        {
            await assistant.SaveAsync(id, request, CurrentUserName, ct);
            return NoContent();
        }, ct);

    [HttpDelete("saved/{id:int}")]
    [Authorize(Roles = SavedQuestionRoles.Admins)]
    public Task<IActionResult> Delete(int id, CancellationToken ct)
        => Run(async () =>
        {
            await saved.DeleteAsync(id, ct);
            return NoContent();
        }, ct);

    /// <summary>Rule errors -> their status; AI failures -> 502; the browser gone (page closed, Cancel) -> 499, nothing more is done.</summary>
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (StyleRuleException ex)
        {
            return Problem(statusCode: ex.StatusCode, title: ex.Message);
        }
        catch (AiServiceException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(499);
        }
    }

    private static string? Clip(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : v;
}
