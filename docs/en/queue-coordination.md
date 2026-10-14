# Naravel.Queue.Coordination (job middleware that coordinates workers)

Status: `WithoutOverlapping`, `RateLimited`, `ThrottlesExceptions` and unique jobs (`IUniqueJob`, reject a duplicate at dispatch) are available.
Decision record: [PDR-012b](../pdr/en/PDR-012b-queue-coordination-middleware.md).

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

## The three middleware (and unique jobs)

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

## Unique jobs (`IUniqueJob`)

`WithoutOverlapping` lets a duplicate into the queue and holds it back at run time. A **unique job** is stricter: the duplicate is never queued.
This is Laravel's `ShouldBeUnique`.

```csharp
public class BuildReportJob : Job, IUniqueJob
{
    public int ReportId { get; set; }

    public string UniqueKey => $"report:{ReportId}";          // what "the same job" means
    public TimeSpan UniqueFor => TimeSpan.FromMinutes(10);     // safety net only (default 1 hour)
    // public bool UniqueUntilProcessing => true;              // optional, see below

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) { /* ... */ }
}

var id = await dispatcher.DispatchAsync(new BuildReportJob { ReportId = 7 });
if (id is null)
{
    // an identical job is already waiting or running: nothing was queued
}
```

**`DispatchAsync` now returns `Task<string?>`.** `null` means "rejected as a duplicate". For any job that is not unique the result is
never `null`, so existing code keeps working apart from the nullable annotation.

| Situation | What happens to the key |
|---|---|
| Job finishes successfully | released by the worker |
| Job fails and will be retried | **stays locked**, the retry is the same job |
| Job fails permanently (attempts exhausted) | released, so a fresh dispatch can try again |
| Middleware skips the job (`Skip()`) | released |
| Middleware releases the job back (`Release(delay)`) | stays locked, the job is still pending |
| `UniqueUntilProcessing => true` | released as soon as the job **starts**, so one more can queue while it runs |
| Worker dies | released when `UniqueFor` ends |
| The push to the queue itself fails | released immediately, so a broker outage does not block the key |

Rules and limits:

- **Needs `AddQueueCoordination()`.** `Naravel.Queue` itself has no Cache dependency, so the lock lives in this package. Dispatching an `IUniqueJob`
  without it **throws** with a message that names `AddQueueCoordination()`; uniqueness is never silently ignored.
- **Not in chains or batches.** A rejected link would stall a chain and a rejected job would leave a batch that never completes, so `Chain(...)` and
  `BatchAsync(...)` throw `ArgumentException` for an `IUniqueJob`. Dispatch it on its own.
- **Memory store = one process.** With the default Memory cache two worker processes do not see each other's keys. For uniqueness across processes use
  Redis (or another shared store).
- The key is text you choose, never a class name. A different job type with the same key is "the same job".
- Release is owner-checked: a worker that finished late, after the key expired and a newer dispatch took it, cannot free the newer dispatch's key.
- Acquiring the key and recording its owner are two writes. A crash between them leaves the key locked until `UniqueFor` ends (never forever).

### Why it is built this way

| Choice | Reason |
|---|---|
| A rejected dispatch returns `null` (owner decision 2026-10-13) | Laravel returns nothing for a rejected unique dispatch too; an exception would make a normal, expected event look like an error, and a made-up id would lie about a job that does not exist. |
| The gate is an interface in `Naravel.Queue` (`IUniqueJobGate`), implemented in `Naravel.Queue.Coordination` | Queue-only users must not pull Cache (AGENTS.md rule 3). The core only knows "ask the gate"; the Cache lock stays in the adapter. |
| The lock owner is the **message id** | The worker that releases the key knows the message id but not the lock token. Storing the token in the queued envelope would need a new column in every Database queue table. The adapter keeps `messageId|token` next to the lock instead, so no driver or schema changes. |
| Throw when no gate is registered | A unique job that silently allows duplicates is worse than one that fails loudly on the first dispatch. |
| Default `UniqueFor` of one hour | Laravel's default is "until the job finishes" with no bound, which turns one crashed worker into a permanently blocked key. A TTL bound is the safe default; raise it for long jobs. |

## What is and is not guaranteed

| Store | Coordination holds |
|---|---|
| Memory (`AddNaravelCache` default) | **inside one process only**. Two worker processes do not see each other's locks. |
| Redis (`AddNaravelRedisCache`) or another shared store | across every worker that uses it |

- At startup `AddQueueCoordination()` logs a warning when the Cache lock/store is Memory but the queue driver is not the Memory one, because that combination looks active while coordinating nothing across processes.
- The lock is not part of the job's transaction. If a worker crashes after taking the lock, it stays until its lifetime ends.
- Everything is **at-least-once**. The middleware reduce duplicates and overload; they do not make a job run exactly once, so jobs must stay idempotent.
- `AddQueueCoordination()` checks at startup that a Cache lock and store are registered and fails with a clear message if not.
- On a driver that cannot delay (Kafka, see [PDR-012a](../pdr/en/PDR-012a-kafka-delay-and-retry.md)) a released job comes back immediately, so
  `WithoutOverlapping` and `RateLimited` would spin. Use a driver with delayed delivery for these middleware.

## Testing

The middleware read the lock and limiter from the job's service provider, so tests can register a Memory Cache or a fake. The repository's tests run them
through a real worker (`tests/Naravel.Queue.Coordination.Tests`), and a Redis test with two workers runs when `NARAVEL_TEST_REDIS` is set.

For unique jobs, `Naravel.Queue.Testing.QueueFake` mimics the gate: a second dispatch of the same `UniqueKey` returns `null` and is not recorded, and keys stay
locked until `Reset()` because the fake never runs jobs. Use `AssertNotDispatched<TJob>()` and `AssertDispatchedCount<TJob>(n)` to check the outcome.
