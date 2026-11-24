using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Naravel.Cache;
using Naravel.Cache.Abstractions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;

namespace Naravel.Queue.Coordination;

/// <summary>
/// Job middleware that lets only one job with the same key run at a time, across every worker that shares the Cache lock store.
/// A job that cannot get the lock is released back to its queue (default) or skipped.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>Illuminate\Queue\Middleware\WithoutOverlapping</c>.
/// <b>Why it exists:</b> two workers must not process the same invoice or user at once, and .NET locks do not cross processes.
/// <b>Not ported:</b> keys derived from the job class or from reflection; the caller supplies the key.
/// With a Memory lock store the exclusion holds inside one process only. <see cref="LockTtl"/> is the longest the lock may be held. While the job runs the lock keeps
/// a short lease that is renewed, so a worker that dies frees the lock within about a minute (a lock that cannot refresh a lease, such as the Memcached lock, keeps one fixed
/// lease of <see cref="LockTtl"/>). A job that runs longer than <see cref="LockTtl"/> loses exclusion and a warning is logged; the job is not stopped, because the middleware has no way to cancel it.
/// The token-fenced release stops a late finisher from freeing someone else's lock.
/// </remarks>
public sealed class WithoutOverlapping : IJobMiddleware
{
    internal static readonly TimeSpan DefaultLockTtl = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan LeaseLength = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan DefaultReleaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>Creates the middleware for one caller-supplied key, for example <c>"invoice:" + invoiceId</c>.</summary>
    public WithoutOverlapping(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    /// <summary>The caller-supplied key that identifies the work that must not overlap.</summary>
    public string Key { get; }

    /// <summary>The longest time the lock may be held. Defaults to 15 minutes; set it above the job's longest run. The lock itself keeps a shorter lease of at most a minute that is renewed while the job runs.</summary>
    public TimeSpan LockTtl { get; private set; } = DefaultLockTtl;

    /// <summary>How long a blocked job waits before it is tried again.</summary>
    public TimeSpan ReleaseDelay { get; private set; } = DefaultReleaseDelay;

    /// <summary><c>true</c> (default) releases a blocked job back to the queue; <c>false</c> skips (drops) it.</summary>
    public bool ReleasesWhenLocked { get; private set; } = true;

    /// <summary>Sets how long a blocked job waits before it is retried. It does not consume an attempt.</summary>
    public WithoutOverlapping ReleaseAfter(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), "The delay cannot be negative.");
        ReleaseDelay = delay;
        return this;
    }

    /// <summary>Sets the longest time the lock may be held. Choose a value above the longest run of the job.</summary>
    public WithoutOverlapping ExpireAfter(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl), "The lock lifetime must be positive.");

        // The lease options need a lease of at least two ticks to choose a renewal interval; fail here and not when a job runs.
        if (ttl.Ticks < 2) throw new ArgumentOutOfRangeException(nameof(ttl), "The lock lifetime must be at least two ticks.");
        LockTtl = ttl;
        return this;
    }

    /// <summary>Drops a blocked job instead of releasing it, for work where only the first run matters.</summary>
    public WithoutOverlapping DontRelease()
    {
        ReleasesWhenLocked = false;
        return this;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(JobContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var cacheLock = context.Services.GetRequiredService<ICacheLock>();
        var name = CoordinationKeys.Overlap(Key);

        // LockTtl is the longest the lock may be held. The lease on the lock is shorter and is renewed while the job runs, so a worker that dies frees the lock
        // within LeaseLength instead of after LockTtl. A lock that cannot refresh keeps one fixed lease of LockTtl.
        var options = new CacheLockLeaseOptions(LockTtl < LeaseLength ? LockTtl : LeaseLength, LockTtl)
        {
            TimeProvider = context.Services.GetService<TimeProvider>(),
        };
        var lease = await cacheLock.TryAcquireLeaseAsync(name, options).ConfigureAwait(false);
        if (lease is null)
        {
            if (ReleasesWhenLocked) context.Release(ReleaseDelay);
            else context.Skip();
            return;
        }

        try
        {
            using var lostWarning = lease.LostToken.Register(() => WarnLost(context));
            await next().ConfigureAwait(false);
        }
        finally
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void WarnLost(JobContext context) =>
        context.Services.GetService<ILoggerFactory>()?.CreateLogger("Naravel.Queue.Coordination").LogWarning(
            "The WithoutOverlapping lock of job {JobId} on queue {Queue} was lost while the job was running; another worker may have started the same work.",
            context.JobId,
            context.QueueName);
}
