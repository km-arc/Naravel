using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.RateLimiting;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;

namespace Naravel.Queue.Coordination;

/// <summary>
/// Job middleware that admits at most a number of jobs per time window, shared across workers through the Cache-backed
/// <see cref="IRateLimiter"/>. A job over the limit is released until the window ends; it does not consume an attempt.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>Illuminate\Queue\Middleware\RateLimited</c>.
/// <b>Why it exists:</b> to respect a downstream API quota from every worker at once.
/// <b>Not ported:</b> named limiters registered globally; the limit is stated where the job is defined.
/// The limiter is a fixed window and may admit up to twice the limit around a window boundary (PDR-007a).
/// </remarks>
public sealed class RateLimited : IJobMiddleware
{
    /// <summary>Creates the middleware for a named limit applied to one subject (for example one API account).</summary>
    public RateLimited(string limiter, long maxAttempts, TimeSpan window, string subject = "global")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(limiter);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window), "The window must be positive.");

        Limiter = limiter;
        MaxAttempts = maxAttempts;
        Window = window;
        Subject = subject;
    }

    /// <summary>The name of the limit.</summary>
    public string Limiter { get; }

    /// <summary>The number of jobs admitted per window.</summary>
    public long MaxAttempts { get; }

    /// <summary>The length of the fixed window.</summary>
    public TimeSpan Window { get; }

    /// <summary>The key the limit is counted against, such as an account id.</summary>
    public string Subject { get; }

    /// <inheritdoc />
    public async Task InvokeAsync(JobContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var rateLimiter = context.Services.GetRequiredService<IRateLimiter>();
        var decision = await rateLimiter
            .AttemptAsync(CoordinationKeys.Limiter(Limiter), Subject, MaxAttempts, Window)
            .ConfigureAwait(false);
        if (!decision.Allowed)
        {
            context.Release(decision.RetryAfter > TimeSpan.Zero ? decision.RetryAfter : TimeSpan.FromSeconds(1));
            return;
        }

        await next().ConfigureAwait(false);
    }
}
