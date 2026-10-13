# Naravel.Queue.Coordination (job middleware that coordinates workers)

Status: `WithoutOverlapping`, `RateLimited` and `ThrottlesExceptions` are available. `UniqueJob` (reject a duplicate at dispatch) is the
remaining part of R08 and is not available yet. Decision record: [PDR-012b](../pdr/en/PDR-012b-queue-coordination-middleware.md).

## Quickstart

```csharp
builder.Services.AddNaravelCache(builder.Configuration);   // add AddNaravelRedisCache(...) when several processes run workers
builder.Services.AddQueueCoordination();

public sealed class SyncInvoiceJob : Job, IHasJobMiddleware
{
    public int InvoiceId { get; set; }

    public IEnumerable<IJobMiddleware> Middleware(JobContext context) => new IJobMiddleware[]
    {
        new RateLimited("billing-api", maxAttempts: 60, window: TimeSpan.FromMinutes(1)),
        new WithoutOverlapping($"invoice:{InvoiceId}")
    };
}
```

Install `Naravel.Queue.Coordination`. It depends on `Naravel.Queue` and `Naravel.Cache`; `Naravel.Queue` itself never references Cache, so
queue-only users do not pull it in.

## Why this exists

.NET locks and `System.Threading.RateLimiting` only work inside one process. A queue usually runs on several workers, so "do not process the
same invoice twice at once" or "stay under the API quota" needs a store that all workers share. Naravel already has one: the Cache locks and
the Cache rate limiter. These middleware are the thin job-level rules on top of them. The key is always text you choose; nothing is derived
from a class name.

## The three middleware

| Middleware | What it does | When it cannot run the job |
|---|---|---|
| `WithoutOverlapping(key)` | one job per key at a time | releases the job and tries again after `ReleaseAfter(...)` (default 10 s), or drops it with `DontRelease()` |
| `RateLimited(limiter, max, window, subject)` | at most `max` jobs per `window` for the subject | releases the job until the window ends |
| `ThrottlesExceptions(max, window, backoff)` | after `max` exceptions inside `window`, stops running that kind of job for `backoff` | releases the job without running it |

**Release and skip.** A middleware that does not call `next` must say what happens to the job. *Release* hands the job back to its queue after
a delay. It does **not** use up an attempt, runs no chain and counts no success, so a job that was only waiting its turn never fails because it
waited. *Skip* acknowledges and drops the job. A middleware that does neither makes the job fail with a clear error; before PDR-012b it was
silently counted as a success.

**Order.** Per-job middleware run inside the globally registered `IJobMiddleware` and in the order you return them. Put `RateLimited` before
`WithoutOverlapping`: a job the limiter holds back then never took the lock.

### WithoutOverlapping

```csharp
new WithoutOverlapping($"invoice:{InvoiceId}")
    .ReleaseAfter(TimeSpan.FromSeconds(5))   // how long a blocked job waits
    .ExpireAfter(TimeSpan.FromMinutes(30));  // lock lifetime if the worker dies; set it above the job's longest run
```

The lock is always released when the job ends, also when it throws. Its lifetime (`ExpireAfter`, default 15 minutes) only protects against a worker
that crashed while holding it. If a job runs longer than that lifetime it loses exclusion; the release is token-fenced, so a late finisher cannot free
a lock that someone else now owns.

### RateLimited

```csharp
new RateLimited("billing-api", maxAttempts: 60, window: TimeSpan.FromMinutes(1), subject: accountId)
```

`subject` is what the limit is counted against, for example one customer account (default `"global"`). The limiter is a fixed window, so around a
window boundary it can let up to twice the limit through (PDR-007a). Do not use it where that matters.

### ThrottlesExceptions

```csharp
new ThrottlesExceptions(maxExceptions: 5, window: TimeSpan.FromMinutes(2), backoff: TimeSpan.FromMinutes(1))
```

Think of a circuit breaker. While the downstream service works nothing changes. When 5 jobs throw within 2 minutes, the circuit opens for 1 minute:
jobs of that type are released without running, instead of burning their attempts against a service that is down. Every exception is still rethrown
to the normal retry policy, a success resets the counter, and worker shutdown (`OperationCanceledException`) is not counted. By default all jobs of one
type share a circuit; pass `key:` to choose another.

## What is and is not guaranteed

| Store | Coordination holds |
|---|---|
| Memory (`AddNaravelCache` default) | **inside one process only**. Two worker processes do not see each other's locks. |
| Redis (`AddNaravelRedisCache`) or another shared store | across every worker that uses it |

- The lock is not part of the job's transaction. If a worker crashes after taking the lock, it stays until its lifetime ends.
- Everything is **at-least-once**. The middleware reduce duplicates and overload; they do not make a job run exactly once, so jobs must stay idempotent.
- `AddQueueCoordination()` checks at startup that a Cache lock and store are registered and fails with a clear message if not.
- On a driver that cannot delay (Kafka, see [PDR-012a](../pdr/en/PDR-012a-kafka-delay-and-retry.md)) a released job comes back immediately, so
  `WithoutOverlapping` and `RateLimited` would spin. Use a driver with delayed delivery for these middleware.

## Testing

The middleware read the lock and limiter from the job's service provider, so tests can register a Memory Cache or a fake. The repository's tests run them
through a real worker (`tests/Naravel.Queue.Coordination.Tests`), and a Redis test with two workers runs when `NARAVEL_TEST_REDIS` is set.
