using Naravel.Cache.Abstractions;

namespace Naravel.Cache;

/// <summary>A held lock whose lease is renewed in the background, until it is disposed, lost, or has been held for the longest allowed time.</summary>
/// <remarks>
/// <para><b>Why it exists:</b> the lifetime given to <see cref="ICacheLock.AcquireAsync"/> is a lease, so work that runs longer than the lease loses the lock while it is still running. A lease renews itself every
/// <see cref="CacheLockLeaseOptions.RenewInterval"/> while the lock is refreshable (<see cref="IRefreshableCacheLock"/>), and tells the work when it can no longer be sure that it holds the lock.</para>
/// <para><b>When the lease is lost:</b> <see cref="IsLost"/> becomes <see langword="true"/> and <see cref="LostToken"/> is cancelled, once, for good, in any of these cases: a renewal answers that the token does not own the lock any more; the lease
/// ended without a successful renewal (a renewal that failed, for example because the backend was unreachable, is tried again at the next interval, and counts as lost only when the previous lease has ended); or
/// <see cref="CacheLockLeaseOptions.MaxHold"/> was reached. A lease is judged lost as soon as it can no longer be proven held, even if the lock itself still exists on the server.
/// Every time is measured with the monotonic timestamp of the clock in <see cref="CacheLockLeaseOptions.TimeProvider"/>, from immediately before the acquisition request or refresh request, so the estimate never runs later than the real expiry.</para>
/// <para><b>What it does not do:</b> cancelling <see cref="LostToken"/> only asks the work to stop. Work that never looks at the token keeps running, and so does work that is stuck in a call that cannot be cancelled. A lease therefore makes a second holder rare and
/// visible; it does not make it impossible. For a resource that must never see two writers, the resource itself has to check a fencing token, which this type does not provide.</para>
/// <para>Dispose the lease to stop the renewal and release the lock. Releasing compares the token, so it never releases a lock that another process took after the lease was lost.</para>
/// </remarks>
public sealed class CacheLockLease : IAsyncDisposable
{
    private const string NotOwnerReason = "the lock no longer belonged to this holder when the lease was renewed (the lease had ended and another process took the lock, or the lock was force released)";
    private const string RenewalFailedReason = "the lease could not be renewed before it ended";
    private const string MaxHoldReason = "the longest allowed hold time was reached";
    private const string EndedReason = "the lease ended before it could be renewed";

    private static readonly TimeSpan LongestDelay = TimeSpan.FromHours(24);

    private readonly ICacheLock _cacheLock;
    private readonly IRefreshableCacheLock? _refreshable;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly string _token;
    private readonly TimeSpan _renewInterval;
    private readonly long _origin;
    private readonly long _maxHoldEnd;
    // Not disposed: it has no timer and no linked source, and LostToken must stay usable by work that outlives the lease.
    private readonly CancellationTokenSource _lostSource = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private long _leaseEnd;
    private int _lost;
    private int _disposed;
    private string? _lossReason;
    private Exception? _renewalError;
    private Task _renewal = Task.CompletedTask;

    private CacheLockLease(ICacheLock cacheLock, string name, string token, CacheLockLeaseOptions options, TimeProvider time, long origin, TimeSpan firstLease)
    {
        _cacheLock = cacheLock;
        _refreshable = cacheLock as IRefreshableCacheLock;
        Name = name;
        _token = token;
        _time = time;
        _ttl = options.Ttl;
        _renewInterval = options.ResolvedRenewInterval;
        _origin = origin;
        _maxHoldEnd = options.MaxHold.Ticks;
        _leaseEnd = firstLease.Ticks;
    }

    /// <summary>The name of the lock.</summary>
    public string Name { get; }

    /// <summary>Whether the selected driver supports ownership-checked lease refresh.</summary>
    /// <remarks>When this is <see langword="false"/>, including for Memcached, the lease is fixed for <see cref="CacheLockLeaseOptions.MaxHold"/> and is not renewed.</remarks>
    public bool CanRenew => _refreshable is not null;

    /// <summary>Whether the lease can no longer be proven held. Once <see langword="true"/> it stays so. After <see cref="DisposeAsync"/> it tells whether the lease was lost before the disposal.</summary>
    public bool IsLost
    {
        get
        {
            if (Volatile.Read(ref _lost) != 0) return true;
            if (Volatile.Read(ref _disposed) != 0) return false;
            var leaseEnd = Volatile.Read(ref _leaseEnd);
            if (Elapsed() < leaseEnd) return false;
            MarkLost(leaseEnd >= _maxHoldEnd ? MaxHoldReason : _renewalError is not null ? RenewalFailedReason : EndedReason);
            return true;
        }
    }

    /// <summary>A token that is cancelled when the lease is lost. Pass it to the work that the lock protects, so that the work can stop; it is not cancelled when the lease is disposed normally.</summary>
    public CancellationToken LostToken => _lostSource.Token;

    internal static async Task<CacheLockLease?> TryAcquireAsync(ICacheLock cacheLock, string name, CacheLockLeaseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cacheLock);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);

        var time = options.TimeProvider ?? TimeProvider.System;

        // A lock that cannot refresh gets one fixed lease for the whole hold time, so that its end is the end of MaxHold.
        var firstLease = cacheLock is IRefreshableCacheLock ? options.Ttl : options.MaxHold;
        var origin = time.GetTimestamp();
        var token = await cacheLock.AcquireAsync(name, firstLease, cancellationToken).ConfigureAwait(false);
        if (token is null) return null;

        var lease = new CacheLockLease(cacheLock, name, token, options, time, origin, firstLease);
        lease._renewal = lease.RenewAsync();
        return lease;
    }

    /// <summary>Stops the renewal and releases the lock when the token still owns it. It does nothing when called again.</summary>
    /// <returns>A task that ends when the renewal has stopped and the lock has been released.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);

            // Settle the verdict while the lease can still be judged, so that IsLost keeps telling the truth afterwards.
            _ = IsLost;
            Volatile.Write(ref _disposed, 1);
            _disposeTask = DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            _stop.Cancel();
            await _renewal.ConfigureAwait(false);
            var released = await _cacheLock.ReleaseAsync(Name, _token, CancellationToken.None).ConfigureAwait(false);
            if (!released) MarkLost(NotOwnerReason);
        }
        finally
        {
            // This source is field-owned: its lifetime ends only after DisposeAsync has cancelled and awaited the renewal task.
            _stop.Dispose();
        }
    }

    internal CacheLockLostException CreateLostException(Exception? cause)
    {
        var reason = Volatile.Read(ref _lossReason) ?? EndedReason;
        return new CacheLockLostException(
            Name,
            $"The lease of lock '{Name}' was lost while the work was running: {reason}. Another process may have entered the same critical section, so the work may not have been exclusive.",
            cause ?? Volatile.Read(ref _renewalError));
    }

    private long Elapsed() => _time.GetElapsedTime(_origin).Ticks;

    private void MarkLost(string reason)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _lost) != 0) return;
            Volatile.Write(ref _lossReason, reason);
            Volatile.Write(ref _lost, 1);
        }

        try
        {
            _lostSource.Cancel();
        }
        catch (AggregateException)
        {
            // A callback that the work registered on LostToken threw. That is the work's own fault and must not stop the lease.
        }
    }

    private async Task RenewAsync()
    {
        var stop = _stop.Token;
        var lastAttempt = 0L;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (Volatile.Read(ref _lost) != 0) return;

                var now = Elapsed();
                var leaseEnd = Volatile.Read(ref _leaseEnd);
                if (now >= leaseEnd)
                {
                    // The previous lease has ended and no renewal made it longer, so the lock cannot be proven held any more.
                    MarkLost(leaseEnd >= _maxHoldEnd ? MaxHoldReason : _renewalError is not null ? RenewalFailedReason : EndedReason);
                    return;
                }

                // Nothing to renew when the lease already reaches the longest hold time, which is always so for a lock that cannot refresh: only wait for its end.
                if (_refreshable is null || leaseEnd >= _maxHoldEnd)
                {
                    await DelayUntilAsync(leaseEnd, stop).ConfigureAwait(false);
                    continue;
                }

                var nextRenewal = lastAttempt + _renewInterval.Ticks;
                if (now < nextRenewal)
                {
                    await DelayUntilAsync(Math.Min(nextRenewal, leaseEnd), stop).ConfigureAwait(false);
                    continue;
                }

                lastAttempt = now;
                await RefreshOnceAsync(_refreshable, now, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The lease was disposed.
        }
        // Any unexpected monitor failure must fail closed; otherwise the caller could keep working without a trustworthy lease.
        catch (Exception exception)
        {
            // Not expected: a lease that cannot be tracked is a lease that cannot be trusted.
            Volatile.Write(ref _renewalError, exception);
            MarkLost(RenewalFailedReason);
        }
    }

    private async Task RefreshOnceAsync(IRefreshableCacheLock refreshable, long startedAt, CancellationToken stop)
    {
        // The new lease never reaches beyond the longest hold time, so the lock also ends by itself if this process dies.
        var length = TimeSpan.FromTicks(Math.Min(_ttl.Ticks, _maxHoldEnd - startedAt));
        var confirmedLeaseEnd = Volatile.Read(ref _leaseEnd);
        var refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(stop);

        Task<bool> refreshTask;
        try
        {
            refreshTask = refreshable.RefreshAsync(Name, _token, length, refreshCancellation.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            refreshCancellation.Dispose();
            throw;
        }
        // Backend faults are retried while the last confirmed lease is still valid.
        catch (Exception exception)
        {
            refreshCancellation.Dispose();
            Volatile.Write(ref _renewalError, exception);
            return;
        }

        // The refresh call must never be allowed to suspend the only task that watches the
        // currently confirmed lease. Race the provider operation against that lease's deadline.
        using var deadlineStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var deadlineTask = DelayUntilAsync(confirmedLeaseEnd, deadlineStop.Token);
        var completed = await Task.WhenAny(refreshTask, deadlineTask).ConfigureAwait(false);

        if (completed != refreshTask)
        {
            // Disposal wins over a simultaneous deadline. Retain the per-attempt token source
            // until the provider task finishes, even when that provider ignores cancellation.
            if (stop.IsCancellationRequested)
            {
                ObserveLateRefresh(refreshTask, refreshCancellation);
                await deadlineTask.ConfigureAwait(false);
                return;
            }

            CancelSafely(refreshCancellation);
            MarkLost(confirmedLeaseEnd >= _maxHoldEnd
                ? MaxHoldReason
                : _renewalError is not null ? RenewalFailedReason : EndedReason);
            ObserveLateRefresh(refreshTask, refreshCancellation);
            return;
        }

        deadlineStop.Cancel();
        try
        {
            await deadlineTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadlineStop.IsCancellationRequested)
        {
            // The refresh completed first; stop the unused deadline timer.
        }

        if (stop.IsCancellationRequested)
        {
            ObserveLateRefresh(refreshTask, refreshCancellation);
            return;
        }

        try
        {
            var refreshed = await refreshTask.ConfigureAwait(false);

            // A response observed at or after the old deadline is too late, even if it says that
            // the backend accepted the refresh. The local owner must fail closed.
            if (Elapsed() >= confirmedLeaseEnd)
            {
                MarkLost(confirmedLeaseEnd >= _maxHoldEnd
                    ? MaxHoldReason
                    : _renewalError is not null ? RenewalFailedReason : EndedReason);
                return;
            }

            if (!refreshed)
            {
                MarkLost(NotOwnerReason);
                return;
            }

            // Counted from before the call, so the estimate is never later than the expiry that the server set.
            // The backend starts its TTL when it processes the refresh, but MaxHold is a local hard stop.
            Volatile.Write(ref _leaseEnd, Math.Min(startedAt + length.Ticks, _maxHoldEnd));
            Volatile.Write(ref _renewalError, null);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Retry on the next interval, but only while the previous confirmed lease remains valid.
            Volatile.Write(ref _renewalError, exception);
        }
        finally
        {
            refreshCancellation.Dispose();
        }
    }

    private static void CancelSafely(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A provider cancellation callback must not prevent the lease from being marked lost.
        }
    }

    private static void ObserveLateRefresh(Task refreshTask, CancellationTokenSource refreshCancellation)
    {
        _ = refreshTask.ContinueWith(
            static (completed, state) =>
            {
                if (completed.IsFaulted) _ = completed.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            refreshCancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DelayUntilAsync(long offsetTicks, CancellationToken stop)
    {
        var wait = TimeSpan.FromTicks(offsetTicks - Elapsed());
        if (wait <= TimeSpan.Zero) return;
        await Task.Delay(wait < LongestDelay ? wait : LongestDelay, _time, stop).ConfigureAwait(false);
    }
}
