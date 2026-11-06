using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Foundation;

namespace Naravel.Routing.Cache.Tests;

/// <summary>A clock that only moves when a test moves it, so no test waits for a window.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate) _now += by;
    }

    public void Set(DateTimeOffset value)
    {
        lock (_gate) _now = value;
    }
}

/// <summary>Passes every call to another store and records it, so a test can say what the adapter did and nothing more.</summary>
internal class RecordingStore(ICacheStore inner) : ICacheStore
{
    private readonly ConcurrentQueue<string> _calls = new();

    public IReadOnlyList<string> Calls => _calls.ToArray();

    public ConcurrentQueue<(string Key, TimeSpan Ttl)> Increments { get; } = new();

    public string Name => inner.Name;

    protected void Record(string call) => _calls.Enqueue(call);

    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Record("get");
        return inner.TryGetAsync<T>(key, cancellationToken);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        Record("set");
        return inner.SetAsync(key, value, ttl, cancellationToken);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Record("remove");
        return inner.RemoveAsync(key, cancellationToken);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Record("exists");
        return inner.ExistsAsync(key, cancellationToken);
    }

    public Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        Record("increment-without-ttl");
        return inner.IncrementAsync(key, by, cancellationToken);
    }

    public virtual Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        Record("increment");
        Increments.Enqueue((key, ttl));
        return inner.IncrementAsync(key, by, ttl, cancellationToken);
    }

    public Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default)
    {
        Record("decrement");
        return inner.DecrementAsync(key, by, cancellationToken);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Record("flush");
        return inner.FlushAsync(cancellationToken);
    }
}

/// <summary>Holds every increment until <c>release</c> of them are running at the same time. A store that serialises its callers never gets there.</summary>
internal sealed class MeetingStore(ICacheStore inner, int release) : RecordingStore(inner)
{
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inFlight;

    public override async Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _inFlight) >= release) _open.TrySetResult();
        await _open.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await base.IncrementAsync(key, by, ttl, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A store that can be told to fail, the way a Redis outage does.</summary>
internal sealed class SwitchableStore(ICacheStore inner) : RecordingStore(inner)
{
    public volatile bool Down;

    public override Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Down ? Task.FromException<long>(new InvalidOperationException("the shared store is down")) : base.IncrementAsync(key, by, ttl, cancellationToken);
}

internal static class Backends
{
    /// <summary>12:00:30 UTC: half way into a minute, so a test that does not move the clock never meets a one-minute boundary.</summary>
    public static readonly DateTimeOffset MidMinute = new(2026, 11, 4, 12, 0, 30, TimeSpan.Zero);

    public const string StoreName = "shared";

    /// <summary>A container with Naravel.Cache, the given store registered under <see cref="StoreName"/> and the shared throttle on it.</summary>
    public static ServiceProvider Provider(ICacheStore store, TimeProvider clock, string storeName = StoreName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddNaravelCache(new ConfigurationBuilder().Build());
        services.AddNaravelDriver<ICacheStore>(storeName, _ => store);
        services.AddNaravelSharedThrottle(storeName);
        return services.BuildServiceProvider();
    }

    /// <summary>Like <see cref="Provider"/>, but the store is made by <paramref name="open"/> the first time Naravel.Cache needs it, so a test controls that moment.</summary>
    public static ServiceProvider ProviderOpening(Func<IServiceProvider, ICacheStore> open, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddNaravelCache(new ConfigurationBuilder().Build());
        services.AddNaravelDriver<ICacheStore>(StoreName, open);
        services.AddNaravelSharedThrottle(StoreName);
        return services.BuildServiceProvider();
    }

    /// <summary>A container whose shared throttle counts in the real Memory store of Naravel.Cache (the <c>memory</c> store that <c>AddNaravelCache</c> registers).</summary>
    public static ServiceProvider MemoryProvider(TimeProvider clock, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        configure?.Invoke(services);
        services.AddNaravelCache(new ConfigurationBuilder().Build());
        services.AddNaravelSharedThrottle("memory");
        return services.BuildServiceProvider();
    }

    /// <summary>The service registrations of one application host that counts in <paramref name="store"/> (shared by every host that gets the same store).</summary>
    public static Action<IServiceCollection> Host(ICacheStore store, TimeProvider clock) => services =>
    {
        services.AddSingleton(clock);
        services.AddNaravelCache(new ConfigurationBuilder().Build());
        services.AddNaravelDriver<ICacheStore>(StoreName, _ => store);
        services.AddNaravelSharedThrottle(StoreName);
    };

    public static IThrottleStore Throttle(ServiceProvider provider) => provider.GetRequiredService<IThrottleStore>();
}
