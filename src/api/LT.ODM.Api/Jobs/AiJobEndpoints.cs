using LT.ODM.Application.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Jobs;

/// <summary>202 answer of an endpoint that queued a job: follow it at GET api/v1/ai/jobs/{jobId}.</summary>
public sealed record AiJobStartedDto(int JobId);

/// <summary>How endpoints hand slow AI work to <see cref="AiJobQueue"/>.</summary>
public static class AiJobEndpoints
{
    /// <summary>Sent by the AI task tray so it can show the job again after a reload (URI-encoded: style numbers may not be ASCII).</summary>
    public const string KeyHeader = "X-Ai-Task-Key", LabelHeader = "X-Ai-Task-Label", UrlHeader = "X-Ai-Task-Url";

    public static string UserName(this ControllerBase c) => c.User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    public static string UserId(this ControllerBase c) => c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? c.User.FindFirst("sub")?.Value ?? "";

    /// <summary>
    /// Validation and the AI checks the endpoint makes stay in the request (so "AI is not set up" still answers 503 at once);
    /// the slow part is queued and the request answers 202 with the job id.
    /// </summary>
    public static async Task<IActionResult> QueueAiJobAsync(this ControllerBase c, AiJobQueue queue, string kind, string lane, object? input, AiJobWork work,
        CancellationToken ct)
    {
        try
        {
            var id = await queue.QueueAsync(kind, lane, Owner(c), input, work, ct);
            return c.Accepted($"/api/v1/ai/jobs/{id}", new AiJobStartedDto(id));
        }
        catch (AiJobLimitException ex)
        {
            return c.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: ex.Message);
        }
    }

    private static AiJobOwner Owner(ControllerBase c)
    {
        var headers = c.Request.Headers;
        string? Header(string name, int max)
        {
            var raw = headers[name].ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string value;
            try { value = Uri.UnescapeDataString(raw).Trim(); }
            catch (UriFormatException) { return null; }
            return value.Length == 0 ? null : value.Length > max ? value[..max] : value;
        }
        // Only pages of this app: a path, never another site ("//host" or "https://...").
        var url = Header(UrlHeader, 400) is { } u && u.StartsWith('/') && !u.StartsWith("//", StringComparison.Ordinal) && !u.Contains('\\') ? u : null;
        return new AiJobOwner(c.UserName(), c.UserId(), Header(KeyHeader, 150), Header(LabelHeader, 150), url);
    }
}
