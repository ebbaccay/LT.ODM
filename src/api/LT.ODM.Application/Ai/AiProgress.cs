namespace LT.ODM.Application.Ai;

/// <summary>
/// How far one AI call has got, for a browser that asked to follow it (header X-Ai-Progress on the request; it polls
/// GET /api/v1/ai/progress/{id}). Phase: "waiting" (sent, nothing back yet: the model is reading the request) or
/// "writing" (the answer is streaming in). Words counts runs of letters and digits in the streamed text, field names
/// included: a sign of life, not a percentage (nobody knows the answer's length in advance). Image calls give no
/// progress; the render page estimates from earlier render times instead.
/// </summary>
public sealed class AiProgressEntry
{
    private readonly object _lock = new();
    private bool _inWord;

    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public string Phase { get; private set; } = "waiting";
    public int Words { get; private set; }

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
        lock (_lock) return new AiProgressDto(Phase, Words, (int)(DateTime.UtcNow - StartedUtc).TotalSeconds);
    }
}

public sealed record AiProgressDto(string Phase, int Words, int Seconds);

/// <summary>The progress entry of the request being handled (flows with the request's async calls), or null.</summary>
public static class AiProgress
{
    private static readonly AsyncLocal<AiProgressEntry?> CurrentEntry = new();

    public static AiProgressEntry? Current => CurrentEntry.Value;

    /// <summary>For the rest of this async flow, AI calls report to <paramref name="entry"/>.</summary>
    public static void Follow(AiProgressEntry? entry) => CurrentEntry.Value = entry;
}
