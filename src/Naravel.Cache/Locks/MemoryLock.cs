using System.Collections.Concurrent;
using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Locks;

/// <summary>Process-local token-owned lock implementation.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the array cache lock; locks are not shared between processes.</para>
/// <para><b>Lifetime:</b> a lock's state is dropped when it is released, and states whose time-to-live has passed without a release
/// (a holder that crashed or ran too long) are swept every 1,024 acquisitions, so the table holds the locks that are
/// held now rather than every name ever used. A name used once per client address, such as a rate-limit bucket, therefore does not
/// grow it without bound.</para>
/// </remarks>
public sealed class MemoryLock : ICacheLock
{
    private const int SweepInterval = 1024;

    private sealed class LockState
    {
        public object Gate { get; } = new();
        public string? Token { get; set; }
        public DateTime ExpiresAtUtc { get; set; }

        // Set under Gate when the state leaves the table. A caller that fetched the state just before it was removed sees this
        // and fetches or creates the current state, so two callers can never hold the same name through two different states.
        public bool Retired { get; set; }
    }

    private static readonly ConcurrentDictionary<string, LockState> Locks = new(StringComparer.Ordinal);
    private static int _acquisitions;
    private readonly string _prefix;

    /// <summary>Creates a process-local lock provider with an optional key prefix.</summary>
    public MemoryLock(string? prefix = null) => _prefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.TrimEnd(':') + ":";

    /// <summary>Always <c>false</c>: the lock lives in this process only.</summary>
    public bool IsShared => false;

    /// <summary>Number of lock states kept for names that start with <paramref name="prefix"/> (tests).</summary>
    internal static int TrackedCount(string prefix)
    {
        var count = 0;
        foreach (var key in Locks.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)) count++;
        }

        return count;
    }

    /// <inheritdoc />
    public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        if (Interlocked.Increment(ref _acquisitions) % SweepInterval == 0) SweepExpired();

        var key = _prefix + name;
        while (true)
        {
            var state = Locks.GetOrAdd(key, static _ => new LockState());
            lock (state.Gate)
            {
                if (state.Retired) continue;
                if (state.Token is not null && state.ExpiresAtUtc > DateTime.UtcNow) return Task.FromResult<string?>(null);
                var token = Guid.NewGuid().ToString("N");
                state.Token = token;
                state.ExpiresAtUtc = DateTime.UtcNow + ttl;
                return Task.FromResult<string?>(token);
            }
        }
    }

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var key = _prefix + name;
        if (!Locks.TryGetValue(key, out var state)) return Task.FromResult(false);
        lock (state.Gate)
        {
            if (state.Retired || !string.Equals(state.Token, token, StringComparison.Ordinal)) return Task.FromResult(false);
            state.Token = null;
            state.ExpiresAtUtc = default;
            Retire(key, state);
            return Task.FromResult(true);
        }
    }

    private static void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in Locks)
        {
            lock (entry.Value.Gate)
            {
                if (!entry.Value.Retired && (entry.Value.Token is null || entry.Value.ExpiresAtUtc <= now)) Retire(entry.Key, entry.Value);
            }
        }
    }

    // Call with state.Gate held.
    private static void Retire(string key, LockState state)
    {
        state.Retired = true;
        Locks.TryRemove(new KeyValuePair<string, LockState>(key, state));
    }
}
