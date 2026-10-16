using System.Collections.Concurrent;

namespace LT.ODM.Application.StyleAi;

/// <summary>
/// How long recent renders took, per image service and model, with and without the sketch: the render page shows an
/// estimated progress bar from it, since image servers report nothing until the picture is ready. The median of the
/// last 5 is used, so one unusually slow render (a cold start) does not skew it. Memory only: after the API restarts
/// there is no estimate until the next render.
/// </summary>
public sealed class RenderTimings
{
    private const int Keep = 5;
    private readonly ConcurrentDictionary<string, Queue<int>> _seconds = new();

    public void Add(string provider, string model, bool withSketch, TimeSpan took)
    {
        var queue = _seconds.GetOrAdd(Key(provider, model, withSketch), _ => new Queue<int>());
        lock (queue)
        {
            queue.Enqueue(Math.Max(1, (int)Math.Round(took.TotalSeconds)));
            while (queue.Count > Keep) queue.Dequeue();
        }
    }

    /// <summary>Median seconds of the recent renders, or null when there are none yet.</summary>
    public int? Estimate(string provider, string model, bool withSketch)
    {
        if (!_seconds.TryGetValue(Key(provider, model, withSketch), out var queue)) return null;
        int[] sorted;
        lock (queue) sorted = [.. queue.Order()];
        return sorted.Length == 0 ? null : sorted[sorted.Length / 2];
    }

    private static string Key(string provider, string model, bool withSketch) => $"{provider}|{model}|{withSketch}";
}
