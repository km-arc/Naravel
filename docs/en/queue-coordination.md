# Naravel.Queue.Coordination (job middleware that coordinates workers)

Status: `WithoutOverlapping`, `RateLimited`, `ThrottlesExceptions` and unique jobs (`IUniqueJob`, reject a duplicate at dispatch) are available.
Decision record: [PDR-012b](../pdr/en/PDR-012b-queue-coordination-middleware.md).

## Quickstart

The shortest working setup: one job with two middleware, the memory queue, the memory Cache and one in-process worker. Nothing outside the process is needed, so you can try everything on this page before choosing Redis.

```sh
dotnet add package Naravel.Queue.Coordination
dotnet add package Naravel.Queue.Memory
```

`Naravel.Queue.Coordination` brings `Naravel.Queue` and `Naravel.Cache` with it. `Naravel.Queue` itself never references Cache, so queue-only users do not pull it in.

```json
{
  "NaravelQueue": {
    "Default": "local",
    "Stores": { "local": { "Driver": "memory" } }
  }
}
```

```csharp
using Naravel.Queue.Coordination;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;
using Naravel.Queue.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelCache(builder.Configuration);   // no "Cache" section: a memory store and a memory lock
builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);
builder.Services.AddQueueCoordination();                   // the middleware read the lock, the store and the rate limiter from here
builder.Services.AddQueueWorker();

var app = builder.Build();

app.MapPost("/invoices/{id:int}/sync", async (int id, IJobDispatcher jobs, CancellationToken ct) =>
{
    var messageId = await jobs.DispatchAsync(new SyncInvoiceJob { InvoiceId = id }, cancellationToken: ct);
    return Results.Accepted(value: new { id = messageId });
});

app.Run();

public sealed class SyncInvoiceJob : Job, IHasJobMiddleware
{
    public int InvoiceId { get; set; }

    public IEnumerable<IJobMiddleware> Middleware(JobContext context) => new IJobMiddleware[]
    {
        new RateLimited("billing-api", maxAttempts: 60, window: TimeSpan.FromMinutes(1)),
        new WithoutOverlapping($"invoice:{InvoiceId}")
    };

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Syncing invoice {InvoiceId}");
        return Task.CompletedTask;
    }
}
```

`POST /invoices/7/sync` answers `202 Accepted` with the message id, and the console shows `Syncing invoice 7` a moment later. If a second job for the same invoice arrives while the first one runs, `WithoutOverlapping` does not run it beside the first: it hands the job back to the queue, and the worker tries it again 10 seconds later. More than 60 jobs in a minute are held back by `RateLimited` in the same way.

The memory queue and the memory Cache live inside the process, so this setup coordinates nothing between processes. To coordinate several workers, add `AddNaravelRedisCache(...)` after `AddNaravelCache(...)` and make `redis` the default Cache store, as the [Cache guide](cache.md) shows; the queue can stay on any driver that can delay a job. `AddQueueCoordination(rateLimiterStore)` takes the name of the Cache store that counts `RateLimited` attempts (the default store when omitted), and it registers the rate limiter only if none is registered yet, so an earlier `AddNaravelRateLimiter(...)` wins. See [What is and is not guaranteed](#what-is-and-is-not-guaranteed).

## Why this exists

.NET locks and `System.Threading.RateLimiting` only work inside one process. A queue usually runs on several workers, so "do not process the
same invoice twice at once" or "stay under the API quota" needs a store that all workers share. Naravel already has one: the Cache locks and
the Cache rate limiter. These middleware are the thin job-level rules on top of them. The key is always text you choose; nothing is derived
from a class name.

## The three middleware (and unique jobs)

| Middleware | What it does | When it cannot run the job |
|---|---|---|
| `WithoutOverlapping(key)` | one job per key at a time | releases the job and tries again after `ReleaseAfter(...)` (default 10 s), or drops it with `DontRelease()` |
| `RateLimited(limiter, max, window, subject)` | at most `max` jobs per `window` for the subject | releases the job for the time the limiter reports as left (one second when it reports none) |
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
public sealed class BuildReportJob : Job, IUniqueJob
{
    public int ReportId { get; set; }

    public string UniqueKey => $"report:{ReportId}";          // what "the same job" means
    public TimeSpan UniqueFor => TimeSpan.FromMinutes(10);     // safety net only (default 1 hour)
    // public bool UniqueUntilProcessing => true;              // optional, see below

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

// jobs is an IJobDispatcher, injected as in the quickstart
var id = await jobs.DispatchAsync(new BuildReportJob { ReportId = 7 });
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
| The lock owner is the **message id** | The worker that releases the key knows the message id but not the lock token. Storing the token in the queued envelope would need a new column in every Database queue table. The adapter keeps `messageId\|token` next to the lock instead, so no driver or schema changes. |
| Throw when no gate is registered | A unique job that silently allows duplicates is worse than one that fails loudly on the first dispatch. |
| Default `UniqueFor` of one hour | Laravel's default is "until the job finishes" with no bound, which turns one crashed worker into a permanently blocked key. A TTL bound is the safe default; raise it for long jobs. |

## What goes into the Cache

The middleware only give names to the Cache. These are the names, so you can find them in Redis or clear one by hand.

| Used by | Name given to the Cache | Kept in |
|---|---|---|
| `WithoutOverlapping(key)` | `naravel:queue:overlap:{key}` | the lock |
| `RateLimited(limiter, ...)` | `naravel:queue:limiter:{limiter}`; the limiter adds the subject and the window itself | the rate limiter |
| `ThrottlesExceptions` | `naravel:queue:throttle:{key}:count` and `naravel:queue:throttle:{key}:open`, where `{key}` is the `key:` you pass or the job type | the store |
| `IUniqueJob` | `naravel:queue:unique:{key}` and `naravel:queue:unique:{key}:owner` (the message id and the lock token, separated by a vertical bar) | the lock and the store |

## What is and is not guaranteed

| Store | Coordination holds |
|---|---|
| Memory (`AddNaravelCache` default) | **inside one process only**. Two worker processes do not see each other's locks. |
| Redis (`AddNaravelRedisCache`) or another shared store | across every worker that uses it |

- At startup `AddQueueCoordination()` logs a warning when the Cache lock/store is Memory but the queue driver is not the Memory one, because that combination looks active while coordinating nothing across processes.
- The lock is not part of the job's transaction. If a worker crashes after taking the lock, it stays until its lifetime ends.
- Everything is **at-least-once**. The middleware reduce duplicates and overload; they do not make a job run exactly once, so jobs must stay idempotent (see [Idempotent jobs](queue.md#idempotent-jobs) for the pattern; `IUniqueJob` is not an idempotency store).
- `AddQueueCoordination()` checks at startup that a Cache lock and store are registered and fails with a clear message if not.
- On a driver that cannot delay (Kafka, see [PDR-012a](../pdr/en/PDR-012a-kafka-delay-and-retry.md)) a released job comes back immediately, so
  `WithoutOverlapping` and `RateLimited` would spin. Use a driver with delayed delivery for these middleware.

## Errors

**Setup.**

| When | Exception or log and message |
|---|---|
| `AddQueueCoordination()` without a Cache lock and store, that is, without `AddNaravelCache(...)` | `InvalidOperationException`: `Naravel.Queue.Coordination needs a Cache lock and store. Call services.AddNaravelCache(configuration) (and a provider such as AddNaravelRedisCache for coordination across processes) before starting the host.` The container's own error is its `InnerException`. The worker takes the unique job gate as a constructor dependency, so a host with a worker fails while it builds its hosted services; the startup check fails the same way at the latest when the host starts. |
| A Memory Cache lock or store together with a queue driver other than the memory one | A warning, not an exception, logged once at startup under the category `Naravel.Queue.Coordination`: `Naravel.Queue.Coordination is using a Memory cache lock/store with the RedisQueueDriver queue driver. Locks, rate limits and unique keys are then visible inside this process only, so several worker or web processes do not coordinate with each other.` and a hint to register a shared Cache store. The driver named is the queue driver's class. |

**Middleware arguments.** Checked when the middleware object is built. .NET appends ` (Parameter 'name')` to the messages of the argument exceptions; the table leaves it out.

| When | Exception and message |
|---|---|
| `new WithoutOverlapping(key)` with a null, empty or blank key | `ArgumentNullException` for null, `ArgumentException` otherwise; parameter `key` |
| `ReleaseAfter` with a negative delay | `ArgumentOutOfRangeException`: `The delay cannot be negative.` |
| `ExpireAfter` with zero or a negative lifetime | `ArgumentOutOfRangeException`: `The lock lifetime must be positive.` |
| `RateLimited` with a blank `limiter` or `subject` | `ArgumentException`; parameter `limiter` or `subject` |
| `RateLimited` with `maxAttempts` below 1 | `ArgumentOutOfRangeException`; parameter `maxAttempts` |
| `RateLimited` or `ThrottlesExceptions` with a `window` of zero or less | `ArgumentOutOfRangeException`: `The window must be positive.` |
| `ThrottlesExceptions` with `maxExceptions` below 1 | `ArgumentOutOfRangeException`; parameter `maxExceptions` |
| `ThrottlesExceptions` with a `backoff` of zero or less | `ArgumentOutOfRangeException`: `The backoff must be positive.` |
| `ThrottlesExceptions` with a `key` that is not null but blank | `ArgumentException`; parameter `key` |

**Dispatching.**

| When | Exception and message |
|---|---|
| An `IUniqueJob` without `AddQueueCoordination()` | `InvalidOperationException`: `BuildReportJob implements IUniqueJob, but no IUniqueJobGate is registered, so uniqueness cannot be enforced. Call services.AddQueueCoordination() (package Naravel.Queue.Coordination, which needs AddNaravelCache()).` |
| An `IUniqueJob` in `Chain(...)` or `BatchAsync(...)` | `ArgumentException`, parameter `jobs`: `BuildReportJob implements IUniqueJob and cannot be part of a chain: a rejected duplicate would stall the sequence. Dispatch it on its own.` (`a batch` for a batch) |
| An `IUniqueJob` whose `UniqueFor` is zero or negative | `ArgumentOutOfRangeException`: `The unique lock lifetime must be positive.` |
| An `IUniqueJob` whose `UniqueKey` is empty | `ArgumentException`; parameter `key` |
| A second dispatch of a unique job that is waiting or running | Not an error: `DispatchAsync` returns `null` and nothing is queued. |

**Running.**

| When | What happens |
|---|---|
| `WithoutOverlapping` finds the lock taken, `RateLimited` is over its limit, or the `ThrottlesExceptions` circuit is open | Not an error. The job is released and runs again later, uses up no attempt, and is dropped instead only for `WithoutOverlapping` after `DontRelease()`. |
| A middleware needs a service that is not registered | The container's own `InvalidOperationException` fails the job. `WithoutOverlapping` needs `ICacheLock` and `ThrottlesExceptions` needs `ICacheStore`, both from `AddNaravelCache(...)`; `RateLimited` needs `IRateLimiter`, which `AddQueueCoordination()` (or `AddNaravelRateLimiter()`) registers. |

`QueueFake` throws its own `ArgumentException` for a unique job in a chain, shorter than the real dispatcher's: `BuildReportJob implements IUniqueJob and cannot be part of a chain.` (parameter `jobs`).

`tests/Naravel.Queue.Coordination.Tests/DocsExampleTests.cs` runs the quickstart, the unique job example and the setup, argument, dispatching and `QueueFake` messages above. The startup warning, the batch variant and the running errors come from the source and are not run there. Change one, change the other.

## Testing

**Run a job through a worker.** The middleware read the lock, the store and the rate limiter from the job's service provider, so a test needs the registration of the quickstart (memory Cache, memory queue, `AddQueueCoordination()`, a worker). It does not need ASP.NET Core: the test starts the hosted services itself, dispatches, and waits for the job. The job is the quickstart job with a static `TaskCompletionSource<int> Handled` that `HandleAsync` completes with `InvoiceId` instead of writing to the console.

```csharp
[Fact]
public async Task The_job_runs_through_its_middleware()
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["NaravelQueue:Default"] = "local",
        ["NaravelQueue:Stores:local:Driver"] = "memory",
    }).Build();

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddNaravelCache(configuration);
    services.AddQueue(configuration).AddMemoryDriver(configuration);
    services.AddQueueCoordination();
    services.AddQueueWorker();

    await using var provider = services.BuildServiceProvider();
    var hosted = provider.GetServices<IHostedService>().ToList();
    foreach (var service in hosted) await service.StartAsync(CancellationToken.None);
    try
    {
        SyncInvoiceJob.Handled = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await provider.GetRequiredService<IJobDispatcher>().DispatchAsync(new SyncInvoiceJob { InvoiceId = 7 });

        Assert.Equal(7, await SyncInvoiceJob.Handled.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
    finally
    {
        foreach (var service in hosted) await service.StopAsync(CancellationToken.None);
    }
}
```

**Test unique jobs without a worker.** Build the same provider but do not start the hosted services: nothing takes the first job off the queue, so the second dispatch of the same key is rejected deterministically.

```csharp
var jobs = provider.GetRequiredService<IJobDispatcher>();

var first = await jobs.DispatchAsync(new BuildReportJob { ReportId = 7 });       // a message id
var duplicate = await jobs.DispatchAsync(new BuildReportJob { ReportId = 7 });   // null
var other = await jobs.DispatchAsync(new BuildReportJob { ReportId = 8 });       // another key: a message id
```

**Without Cache or a queue.** `Naravel.Queue.Testing.QueueFake` replaces the dispatcher. For a unique job it mimics the gate: a second dispatch of the same `UniqueKey` returns `null` and is not recorded, and keys stay locked until `Reset()` because the fake never runs jobs. Use `AssertNotDispatched<TJob>()` and `AssertDispatchedCount<TJob>(n)` to check the outcome. The fake does not run middleware, so it cannot tell you whether a job would be held back.

**What the repository tests.** `tests/Naravel.Queue.Coordination.Tests` runs the middleware and unique jobs through a real worker on the memory Cache (`CoordinationMiddlewareTests`, `UniqueJobTests`), and runs the same rules with two workers on one Redis when `NARAVEL_TEST_REDIS` is set. `DocsExampleTests` runs the examples and messages of this guide.
