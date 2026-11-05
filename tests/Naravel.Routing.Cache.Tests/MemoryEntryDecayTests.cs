using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Routing.Cache.Tests;

/// <summary>
/// The shared throttle over the Memory store must not keep a key per client and window for ever (AUDIT D-74, PDR-024). It takes no lock
/// and no <c>MemoryLock</c>; the counters expire with their windows. <c>MemoryCacheStore</c> reads the system clock, so this test uses a
/// real window of a few hundred milliseconds.
/// </summary>
public sealed class MemoryEntryDecayTests
{
    [Fact]
    public async Task Many_subjects_over_several_short_windows_leave_the_entry_count_falling_once_the_windows_have_expired()
    {
        var window = TimeSpan.FromMilliseconds(300);
        using var provider = Backends.MemoryProvider(TimeProvider.System, services =>
            services.AddMemoryCache(options =>
            {
                options.TrackStatistics = true;
                options.ExpirationScanFrequency = TimeSpan.FromMilliseconds(20);
            }));
        var memory = (MemoryCache)provider.GetRequiredService<IMemoryCache>();
        var store = Backends.Throttle(provider);

        long peak = 0;
        for (var wave = 0; wave < 3; wave++)
        {
            for (var subject = 0; subject < 200; subject++)
            {
                await store.AttemptAsync($"decay|/x|{wave}-{subject}", 5, window, CancellationToken.None);
            }

            peak = Math.Max(peak, memory.GetCurrentStatistics()!.CurrentEntryCount);
            await Task.Delay(window + TimeSpan.FromMilliseconds(150));
        }

        peak.Should().BeGreaterThan(150, "each wave of 200 subjects leaves a counter per subject while its window is open");

        // Any later cache operation lets the cache sweep what has expired; poll instead of sleeping for a guess.
        long last = long.MaxValue;
        for (var attempt = 0; attempt < 100 && last > 10; attempt++)
        {
            await store.AttemptAsync("decay|/x|probe", 5, window, CancellationToken.None);
            last = memory.GetCurrentStatistics()!.CurrentEntryCount;
            if (last > 10) await Task.Delay(100);
        }

        last.Should().BeLessThanOrEqualTo(10, "the counters of the expired windows are gone; only the probe's own counter is left");
    }
}
