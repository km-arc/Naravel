using System.Runtime.ExceptionServices;
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

    /// <summary>Tries to take a lock at once and returns a lease that renews itself, or <see langword="null"/> when the lock is held by someone else.</summary>
    /// <param name="cacheLock">The lock driver.</param>
    /// <param name="name">The lock name.</param>
    /// <param name="options">The lease length, the longest hold time and the renewal interval.</param>
    /// <param name="cancellationToken">Cancels the attempt. It does not cancel the lease that is returned.</param>
    /// <returns>The lease, which the caller must dispose to release the lock, or <see langword="null"/>.</returns>
    /// <remarks>Prefer <c>BlockAsync</c> or <c>TryRunAsync</c> with a <see cref="CacheLockLeaseOptions"/>: they release the lease for you and report a lost lease. Use this method when the work cannot be written as one callback; then watch <see cref="CacheLockLease.LostToken"/> and <see cref="CacheLockLease.IsLost"/> yourself.</remarks>
    public static Task<CacheLockLease?> TryAcquireLeaseAsync(
        this ICacheLock cacheLock,
        string name,
        CacheLockLeaseOptions options,
        CancellationToken cancellationToken = default)
        => CacheLockLease.TryAcquireAsync(cacheLock, name, options, cancellationToken);

    /// <summary>Waits for a lock, runs the callback while the lease renews itself, and releases the lock when the callback finishes.</summary>
    /// <param name="cacheLock">The lock driver.</param>
    /// <param name="name">The lock name.</param>
    /// <param name="options">The lease length, the longest hold time and the renewal interval.</param>
    /// <param name="wait">How long to wait for the lock. Zero makes one attempt.</param>
    /// <param name="callback">The work. Its token is cancelled when <paramref name="cancellationToken"/> is cancelled and when the lease is lost; the work has to look at it to stop.</param>
    /// <param name="cancellationToken">Cancels the wait and the work.</param>
    /// <returns><see langword="true"/> when the callback ran; <see langword="false"/> when the lock was not taken within <paramref name="wait"/>.</returns>
    /// <exception cref="CacheLockLostException">The callback returned normally although the lease was lost, or it threw an <see cref="OperationCanceledException"/> because the lease was lost while the caller's own token was not cancelled.</exception>
    /// <remarks>
    /// <para>An exception that the callback throws propagates unchanged, even when the lease was lost, except the one case above. If releasing the lock fails after the callback has already failed or the lease was lost, the failure to release is dropped and the lock ends at the end of its lease.</para>
    /// <para>The lock is taken with a lease of <see cref="CacheLockLeaseOptions.Ttl"/>, renewed at every <see cref="CacheLockLeaseOptions.RenewInterval"/> while the driver can refresh, and held for at most <see cref="CacheLockLeaseOptions.MaxHold"/>.
    /// A driver that cannot refresh gets one fixed lease of <see cref="CacheLockLeaseOptions.MaxHold"/>.</para>
    /// <para>Cancelling the callback's token asks the work to stop; it does not stop work that ignores it.</para>
    /// </remarks>
    public static Task<bool> BlockAsync(
        this ICacheLock cacheLock,
        string name,
        CacheLockLeaseOptions options,
        TimeSpan wait,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);
        return RunWithLeaseAsync(cacheLock, name, options, wait, callback, cancellationToken);
    }

    /// <summary>Runs the callback while the lease renews itself, only when the lock can be taken at once.</summary>
    /// <param name="cacheLock">The lock driver.</param>
    /// <param name="name">The lock name.</param>
    /// <param name="options">The lease length, the longest hold time and the renewal interval.</param>
    /// <param name="callback">The work. Its token is cancelled when <paramref name="cancellationToken"/> is cancelled and when the lease is lost.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true"/> when the callback ran; <see langword="false"/> when the lock was taken by someone else.</returns>
    /// <exception cref="CacheLockLostException">As for <c>BlockAsync</c>.</exception>
    public static Task<bool> TryRunAsync(
        this ICacheLock cacheLock,
        string name,
        CacheLockLeaseOptions options,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken = default)
        => RunWithLeaseAsync(cacheLock, name, options, TimeSpan.Zero, callback, cancellationToken);

    private static async Task<bool> RunWithLeaseAsync(
        ICacheLock cacheLock,
        string name,
        CacheLockLeaseOptions options,
        TimeSpan wait,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cacheLock);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);

        var time = options.TimeProvider ?? TimeProvider.System;
        var startedWaiting = time.GetTimestamp();
        CacheLockLease? lease = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lease = await CacheLockLease.TryAcquireAsync(cacheLock, name, options, cancellationToken).ConfigureAwait(false);
            if (lease is not null) break;

            var remaining = wait - time.GetElapsedTime(startedWaiting);
            if (remaining <= TimeSpan.Zero) return false;
            var pause = TimeSpan.FromMilliseconds(150);
            await Task.Delay(remaining < pause ? remaining : pause, time, cancellationToken).ConfigureAwait(false);
        }

        // Acquisition latency consumes MaxHold. Never invoke protected work with a lease that
        // was already expired by the time the backend returned its ownership token.
        if (lease.IsLost)
        {
            try
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Preserve the primary lease-loss outcome; the backend TTL is the final cleanup.
            }

            throw lease.CreateLostException(null);
        }

        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LostToken);
        ExceptionDispatchInfo? failure = null;
        try
        {
            await callback(work.Token).ConfigureAwait(false);
        }
        // Capture every callback exception so lease disposal cannot replace the primary work failure.
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
        }

        // Check before and after disposal. A token-checked release can report that ownership
        // was already gone in the narrow interval after the callback returned.
        var lost = lease.IsLost;
        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
        // Suppress cleanup failure only when a callback failure or lease loss is already the primary outcome.
        catch when (failure is not null || lost)
        {
            // The lock was not released, so it ends with its backend lease.
        }

        lost |= lease.IsLost;

        if (failure is not null)
        {
            // The cancellation that the lease itself caused is reported as the lost lease. Any other exception is the callback's own and stays what it was.
            if (lost && failure.SourceException is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw lease.CreateLostException(failure.SourceException);
            }

            failure.Throw();
        }

        if (lost) throw lease.CreateLostException(null);
        return true;
    }
}
