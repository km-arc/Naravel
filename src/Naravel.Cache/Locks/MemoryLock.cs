using System.Collections.Concurrent;
using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Locks;

/// <summary>Process-local token-owned lock implementation.</summary>
/// <remarks><b>Laravel equivalent:</b> the array cache lock; locks are not shared between processes.</remarks>
public sealed class MemoryLock : ICacheLock
{
    private sealed class LockState
    {
        public object Gate { get; } = new();
        public string? Token { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    private static readonly ConcurrentDictionary<string, LockState> Locks = new(StringComparer.Ordinal);
    private readonly string _prefix;

    /// <summary>Creates a process-local lock provider with an optional key prefix.</summary>
    public MemoryLock(string? prefix = null) => _prefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.TrimEnd(':') + ":";

    /// <inheritdoc />
    public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        var state = Locks.GetOrAdd(_prefix + name, static _ => new LockState());
        lock (state.Gate)
        {
            if (state.Token is not null && state.ExpiresAtUtc > DateTime.UtcNow) return Task.FromResult<string?>(null);
            var token = Guid.NewGuid().ToString("N");
            state.Token = token;
            state.ExpiresAtUtc = DateTime.UtcNow + ttl;
            return Task.FromResult<string?>(token);
        }
    }

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (!Locks.TryGetValue(_prefix + name, out var state)) return Task.FromResult(false);
        lock (state.Gate)
        {
            if (!string.Equals(state.Token, token, StringComparison.Ordinal)) return Task.FromResult(false);
            state.Token = null;
            state.ExpiresAtUtc = default;
            return Task.FromResult(true);
        }
    }
}
