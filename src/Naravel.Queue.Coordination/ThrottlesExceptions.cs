using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;

namespace Naravel.Queue.Coordination;

/// <summary>
/// Job middleware that stops running jobs for a while after they have thrown too many exceptions (a circuit breaker shared
/// across workers). While the circuit is open, jobs are released without running and without using an attempt.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>Illuminate\Queue\Middleware\ThrottlesExceptions</c>.
/// <b>Why it exists:</b> when a downstream service is down, retrying every job immediately wastes attempts and hammers it.
/// <b>Not ported:</b> per-exception-type filters. An exception that is thrown is always rethrown to the normal retry policy,
/// except <see cref="OperationCanceledException"/> (worker shutdown), which is not counted. A success resets the counter.
/// The counter uses the Cache store's fixed-lifetime counter, so the window starts at the first exception.
/// </remarks>
public sealed class ThrottlesExceptions : IJobMiddleware
{
    /// <summary>Creates the middleware. The key defaults to the job's registered alias, so every job of one type shares a circuit.</summary>
    public ThrottlesExceptions(int maxExceptions, TimeSpan window, TimeSpan backoff, string? key = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExceptions, 1);
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window), "The window must be positive.");
        if (backoff <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(backoff), "The backoff must be positive.");
        if (key is not null) ArgumentException.ThrowIfNullOrWhiteSpace(key);

        MaxExceptions = maxExceptions;
        Window = window;
        Backoff = backoff;
        Key = key;
    }

    /// <summary>The number of exceptions inside <see cref="Window"/> that opens the circuit.</summary>
    public int MaxExceptions { get; }

    /// <summary>How long exceptions are counted before the counter expires.</summary>
    public TimeSpan Window { get; }

    /// <summary>How long the circuit stays open, and how long a blocked job waits before it is tried again.</summary>
    public TimeSpan Backoff { get; }

    /// <summary>The caller-supplied circuit key, or <c>null</c> to use the job's registered alias.</summary>
    public string? Key { get; }

    /// <inheritdoc />
    public async Task InvokeAsync(JobContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var store = context.Services.GetRequiredService<ICacheStore>();
        var key = Key ?? context.Message.JobType;
        var openKey = CoordinationKeys.ThrottleOpen(key);
        var countKey = CoordinationKeys.ThrottleCount(key);

        if (await store.ExistsAsync(openKey).ConfigureAwait(false))
        {
            context.Release(Backoff);
            return;
        }

        try
        {
            await next().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var count = await store.IncrementAsync(countKey, 1, Window).ConfigureAwait(false);
            if (count >= MaxExceptions)
            {
                await store.SetAsync(openKey, true, Backoff).ConfigureAwait(false);
                await store.RemoveAsync(countKey).ConfigureAwait(false);
            }

            throw;
        }

        await store.RemoveAsync(countKey).ConfigureAwait(false);
    }
}
