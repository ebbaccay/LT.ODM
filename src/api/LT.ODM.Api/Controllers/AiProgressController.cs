using System.Collections.Concurrent;
using LT.ODM.Application.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Progress of slow AI calls (AI Studio summaries, explanations, prompt rewrites) while they run. The browser sends a
/// random id in the X-Ai-Progress header with the AI request and polls GET api/v1/ai/progress/{id} with it every few
/// seconds; each user only sees their own entries. Entries live only while the request runs (memory only).
/// </summary>
[ApiController]
[Route("api/v1/ai/progress")]
[Authorize]
public sealed class AiProgressController(AiProgressRegistry registry) : ControllerBase
{
    /// <summary>204 when the call has not started yet or has already finished (the answer itself comes with the request).</summary>
    [HttpGet("{id}")]
    public IActionResult Get(string id)
        => registry.Find(AiProgressRegistry.Owner(User), id) is { } entry ? Ok(entry.Snapshot()) : NoContent();
}

/// <summary>The running AI calls that browsers follow, by user and the browser's id.</summary>
public sealed class AiProgressRegistry
{
    public const string Header = "X-Ai-Progress";
    private readonly ConcurrentDictionary<string, AiProgressEntry> _entries = new();

    public static string? Owner(System.Security.Claims.ClaimsPrincipal user) => user.FindFirst("sub")?.Value;

    /// <summary>Ids are the browser's random UUIDs; anything else is ignored.</summary>
    public static bool ValidId(string? id) => Guid.TryParse(id, out _) && id!.Length <= 36;

    public AiProgressEntry? Find(string? owner, string id) => owner is null || !ValidId(id) ? null : _entries.GetValueOrDefault(Key(owner, id));

    /// <summary>Runs the request with its AI calls reporting to a new entry, removed when the request ends.</summary>
    public async Task FollowAsync(string owner, string id, Func<Task> request)
    {
        var key = Key(owner, id);
        var entry = new AiProgressEntry();
        _entries[key] = entry;
        AiProgress.Follow(entry);
        try { await request(); }
        finally
        {
            AiProgress.Follow(null);
            _entries.TryRemove(key, out _);
        }
    }

    private static string Key(string owner, string id) => owner + "|" + id.ToLowerInvariant();
}
