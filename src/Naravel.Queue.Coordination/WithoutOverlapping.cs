using Microsoft.Extensions.DependencyInjection;
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
/// With a Memory lock store the exclusion holds inside one process only. The lock expires after <see cref="LockTtl"/>, so a job that
/// runs longer loses exclusion; the token-fenced release stops a late finisher from freeing someone else's lock.
/// </remarks>
public sealed class WithoutOverlapping : IJobMiddleware
{
    internal static readonly TimeSpan DefaultLockTtl = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan DefaultReleaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>Creates the middleware for one caller-supplied key, for example <c>"invoice:" + invoiceId</c>.</summary>
    public WithoutOverlapping(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    /// <summary>The caller-supplied key that identifies the work that must not overlap.</summary>
    public string Key { get; }

    /// <summary>How long the lock lives if the job never finishes (crashed worker). Defaults to 15 minutes; set it above the job's longest run.</summary>
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

    /// <summary>Sets the lock lifetime. Choose a value above the longest run of the job.</summary>
    public WithoutOverlapping ExpireAfter(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl), "The lock lifetime must be positive.");
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
        var token = await cacheLock.AcquireAsync(name, LockTtl).ConfigureAwait(false);
        if (token is null)
        {
            if (ReleasesWhenLocked) context.Release(ReleaseDelay);
            else context.Skip();
            return;
        }

        try
        {
            await next().ConfigureAwait(false);
        }
        finally
        {
            await cacheLock.ReleaseAsync(name, token).ConfigureAwait(false);
        }
    }
}
