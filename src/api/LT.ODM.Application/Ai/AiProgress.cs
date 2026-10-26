namespace LT.ODM.Application.Ai;

/// <summary>
/// How far one AI call has got: an AI job's (AiJobQueue) running text answer, shown with the job
/// (GET /api/v1/ai/jobs/{id}). Phase: "waiting" (sent, nothing back yet: the model is reading the request) or
/// "writing" (the answer is streaming in). Words counts runs of letters and digits in the streamed text, field names
/// included: a sign of life, not a percentage (nobody knows the answer's length in advance). Image calls give no
/// progress; the render page estimates from earlier render times instead.
/// </summary>
public sealed class AiProgressEntry
{
    private readonly object _lock = new();
    private bool _inWord;

    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    /// <summary>queued (waiting for the AI server, AiCallLimiter), waiting (sent, nothing back yet) or writing.</summary>
    public string Phase { get; private set; } = "waiting";
    public int Words { get; private set; }
    /// <summary>While queued: calls ahead of this one at the AI server.</summary>
    public int? Ahead { get; private set; }

    /// <summary>Waiting for a free place at the AI server, with this many calls ahead.</summary>
    public void Queued(int ahead)
    {
        lock (_lock)
        {
            Phase = "queued";
            Ahead = ahead;
        }
    }

    /// <summary>The call got its place and was sent.</summary>
    public void Sent()
    {
        lock (_lock)
        {
            if (Phase == "queued") Phase = "waiting";
            Ahead = null;
        }
    }

    /// <summary>A piece of the streamed answer arrived.</summary>
    public void Wrote(string part)
    {
        lock (_lock)
        {
            Phase = "writing";
            foreach (var ch in part)
            {
                var wordChar = char.IsLetterOrDigit(ch);
                if (wordChar && !_inWord) Words++;
                _inWord = wordChar;
            }
        }
    }

    public AiProgressDto Snapshot()
    {
        lock (_lock) return new AiProgressDto(Phase, Words, (int)(DateTime.UtcNow - StartedUtc).TotalSeconds, Ahead);
    }
}

public sealed record AiProgressDto(string Phase, int Words, int Seconds, int? Ahead = null);

/// <summary>The progress entry of the AI job being run (flows with its async calls), or null.</summary>
public static class AiProgress
{
    private static readonly AsyncLocal<AiProgressEntry?> CurrentEntry = new();

    public static AiProgressEntry? Current => CurrentEntry.Value;

    /// <summary>For the rest of this async flow, AI calls report to <paramref name="entry"/>.</summary>
    public static void Follow(AiProgressEntry? entry) => CurrentEntry.Value = entry;
}
