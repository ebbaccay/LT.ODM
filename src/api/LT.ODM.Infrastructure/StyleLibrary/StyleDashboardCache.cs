using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Infrastructure.StyleLibrary;

/// <summary>
/// The landing-page dashboard (style.usp_Dashboard_Get scans every BOM line: about 0.6-0.9 s for 30k lines, growing with
/// the library) kept in memory for every user. Cleared by every write through StyleRepository, an import commit and
/// reference-list changes; <see cref="CacheFor"/> covers changes made outside the app (SQL scripts). Concurrent first
/// loads share one database call; a load that overlaps a write is returned but not kept.
/// </summary>
public sealed class StyleDashboardCache(TimeProvider clock)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (StyleDashboardDto Value, DateTimeOffset LoadedAt, long Generation)? _entry;
    private long _generation;

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _entry = null;
    }

    public async Task<StyleDashboardDto> GetAsync(Func<CancellationToken, Task<StyleDashboardDto>> load, CancellationToken ct)
    {
        if (Fresh() is { } hit) return hit;
        await _gate.WaitAsync(ct);
        try
        {
            if (Fresh() is { } again) return again;
            var generation = Interlocked.Read(ref _generation);
            var value = await load(ct);
            if (Interlocked.Read(ref _generation) == generation) _entry = (value, clock.GetUtcNow(), generation);
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private StyleDashboardDto? Fresh()
        => _entry is { } e && e.Generation == Interlocked.Read(ref _generation) && clock.GetUtcNow() - e.LoadedAt < CacheFor ? e.Value : null;
}
