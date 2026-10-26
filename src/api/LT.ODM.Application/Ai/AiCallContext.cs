namespace LT.ODM.Application.Ai;

/// <summary>
/// Interactive: someone is waiting on the screen (Library assistant, Smart search, image search). Background: an AI task
/// (renders, summaries, readers), followed in the tray. At a busy AI server interactive calls go first.
/// </summary>
public enum AiCallPriority
{
    Interactive = 0,
    Background = 1,
}

/// <summary>Who an AI call is for: the signed-in user (sign-in subject) and how urgent it is.</summary>
public sealed record AiCaller(string User, AiCallPriority Priority);

/// <summary>
/// The caller of the AI calls in this async flow: set for each web request (Interactive) and for each AI task
/// (Background, its owner), read by AiCallLimiter to take turns fairly. Unset = an anonymous interactive call.
/// </summary>
public static class AiCallContext
{
    private static readonly AsyncLocal<AiCaller?> CurrentCaller = new();

    public static AiCaller Current => CurrentCaller.Value ?? Anonymous;

    public static readonly AiCaller Anonymous = new("", AiCallPriority.Interactive);

    public static void Set(AiCaller? caller) => CurrentCaller.Value = caller;
}

/// <summary>One AI server's load right now (Settings > AI connections > AI tasks).</summary>
public sealed record AiServerLoadDto(string Name, int Limit, int Running, int WaitingInteractive, int WaitingBackground);
