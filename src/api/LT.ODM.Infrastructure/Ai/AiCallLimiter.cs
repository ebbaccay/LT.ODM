using System.Collections.Concurrent;
using LT.ODM.Application.Ai;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>
/// AI call limits (Settings > AI connections > Limit AI calls): each AI server gets at most its connection's MaxConcurrent
/// calls at once; the others wait here. Servers are told apart by address, so jobs pointing at the same server (text and
/// images on one machine, or two connections to it) share one limit, the lowest of their connections. A free place goes
/// to an interactive call first (someone waiting on the screen), then users take turns (round-robin), each user's calls
/// in the order they came. A waiting AI task's progress shows how many calls are ahead. MaxConcurrent 0 = no limit (the
/// switch is off). In memory, for one API server.
/// </summary>
public sealed class AiCallLimiter
{
    private sealed class Waiter(AiCaller caller, long seq, AiProgressEntry? progress)
    {
        public AiCaller Caller { get; } = caller;
        public long Seq { get; } = seq;
        public AiProgressEntry? Progress { get; } = progress;
        public TaskCompletionSource<IDisposable> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Server
    {
        public string Name = "";
        public readonly Dictionary<string, int> Limits = new(StringComparer.OrdinalIgnoreCase);
        public int Limit => Limits.Count == 0 ? 1 : Limits.Values.Min();
        public int Running;
        public readonly List<Waiter> Waiting = [];
        /// <summary>When each user was last given a place (round-robin: the longest-waiting user goes next).</summary>
        public readonly Dictionary<string, long> LastServed = new(StringComparer.Ordinal);
        public long Clock;
    }

    private sealed class Place(AiCallLimiter limiter, Server server) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) limiter.Release(server);
        }
    }

    private sealed class NoPlace : IDisposable
    {
        public void Dispose() { }
    }

    private static readonly IDisposable Unlimited = new NoPlace();
    private readonly ConcurrentDictionary<string, Server> _servers = new(StringComparer.OrdinalIgnoreCase);
    private long _seq;

    /// <summary>The server's address as the limit key: same host and path = same server.</summary>
    public static string Key(AiConnection c) => c.Endpoint.Trim().TrimEnd('/').ToLowerInvariant();

    /// <summary>Waits for a place at the connection's server; dispose the result when the call has finished.</summary>
    public Task<IDisposable> EnterAsync(AiConnection c, CancellationToken ct)
    {
        if (c.MaxConcurrent <= 0) return Task.FromResult(Unlimited);
        var server = _servers.GetOrAdd(Key(c), _ => new Server());
        var caller = AiCallContext.Current;
        Waiter waiter;
        lock (server)
        {
            server.Name = c.Name;
            server.Limits[c.Name] = c.MaxConcurrent;
            if (server.Running < server.Limit && server.Waiting.Count == 0)
            {
                server.Running++;
                server.LastServed[caller.User] = ++server.Clock;
                return Task.FromResult<IDisposable>(new Place(this, server));
            }
            waiter = new Waiter(caller, Interlocked.Increment(ref _seq), AiProgress.Current);
            server.Waiting.Add(waiter);
            Report(server);
        }

        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(() =>
            {
                lock (server)
                {
                    // Already given a place: the caller gets it and releases it as usual.
                    if (!server.Waiting.Remove(waiter)) return;
                    Report(server);
                }
                waiter.Ready.TrySetCanceled(ct);
            });
            waiter.Ready.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        }
        return waiter.Ready.Task;
    }

    /// <summary>What each AI server is doing now (servers with no call yet since the API started are not listed).</summary>
    public IReadOnlyList<AiServerLoadDto> Load()
        => _servers.Values.Select(s =>
        {
            lock (s)
                return new AiServerLoadDto(s.Name, s.Limit, s.Running, s.Waiting.Count(w => w.Caller.Priority == AiCallPriority.Interactive),
                    s.Waiting.Count(w => w.Caller.Priority == AiCallPriority.Background));
        }).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private void Release(Server server)
    {
        var ready = new List<(Waiter Waiter, IDisposable Place)>();
        lock (server)
        {
            server.Running--;
            while (server.Running < server.Limit && server.Waiting.Count > 0)
            {
                var next = Order(server)[0];
                server.Waiting.Remove(next);
                server.Running++;
                server.LastServed[next.Caller.User] = ++server.Clock;
                ready.Add((next, new Place(this, server)));
            }
            Report(server);
        }
        foreach (var (waiter, place) in ready)
        {
            waiter.Progress?.Sent();
            if (!waiter.Ready.TrySetResult(place)) place.Dispose();   // cancelled at the same moment: give the place on
        }
    }

    /// <summary>
    /// Who goes next: interactive before background; then each user's first waiting call, the user served longest ago first
    /// (a user never served yet goes before everyone); then their second call, and so on.
    /// </summary>
    private static List<Waiter> Order(Server server)
    {
        var turn = new Dictionary<string, int>(StringComparer.Ordinal);
        return server.Waiting
            .OrderBy(w => w.Seq)
            .Select(w => (Waiter: w, Turn: turn[w.Caller.User] = turn.GetValueOrDefault(w.Caller.User, -1) + 1))
            .OrderBy(x => x.Waiter.Caller.Priority)
            .ThenBy(x => x.Turn)
            .ThenBy(x => server.LastServed.GetValueOrDefault(x.Waiter.Caller.User, 0))
            .ThenBy(x => x.Waiter.Seq)
            .Select(x => x.Waiter)
            .ToList();
    }

    /// <summary>Tells each waiting call how many are ahead of it (shown on its AI task).</summary>
    private static void Report(Server server)
    {
        var order = Order(server);
        for (var i = 0; i < order.Count; i++) order[i].Progress?.Queued(i);
    }
}
