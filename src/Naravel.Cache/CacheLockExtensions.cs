using Naravel.Cache.Abstractions;

namespace Naravel.Cache;

/// <summary>Convenience operations for token-owned cache locks.</summary>
public static class CacheLockExtensions
{
    /// <summary>Attempts a lock immediately and runs the callback only when it is acquired.</summary>
    public static Task<bool> TryRunAsync(
        this ICacheLock cacheLock,
        string name,
        TimeSpan ttl,
        Func<Task> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return cacheLock.TryRunAsync(name, ttl, _ => callback(), cancellationToken);
    }

    /// <summary>Waits for a lock, runs the callback, and releases it when the callback finishes.</summary>
    public static Task<bool> BlockAsync(
        this ICacheLock cacheLock,
        string name,
        TimeSpan ttl,
        TimeSpan wait,
        Func<Task> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return cacheLock.BlockAsync(name, ttl, wait, _ => callback(), cancellationToken);
    }

    /// <summary>Waits for a lock, runs the cancellable callback, and releases it when the callback finishes.</summary>
    public static async Task<bool> BlockAsync(
        this ICacheLock cacheLock,
        string name,
        TimeSpan ttl,
        TimeSpan wait,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cacheLock);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);

        var deadline = DateTime.UtcNow + wait;
        string? token;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            token = await cacheLock.AcquireAsync(name, ttl, cancellationToken).ConfigureAwait(false);
            if (token is not null) break;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        } while (true);

        try
        {
            await callback(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await cacheLock.ReleaseAsync(name, token, CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Runs the callback only when the lock can be acquired immediately.</summary>
    public static async Task<bool> TryRunAsync(
        this ICacheLock cacheLock,
        string name,
        TimeSpan ttl,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cacheLock);
        ArgumentNullException.ThrowIfNull(callback);
        var token = await cacheLock.AcquireAsync(name, ttl, cancellationToken).ConfigureAwait(false);
        if (token is null) return false;

        try
        {
            await callback(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await cacheLock.ReleaseAsync(name, token, CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }
}
