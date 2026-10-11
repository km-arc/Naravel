# Naravel.Queue

A Laravel-inspired, driver-based background job queue for .NET, built on `Naravel.Foundation`
(`Manager<IQueueDriver, QueueOptions>`). Laravel equivalent: `illuminate/queue`
(`Queue::push()`, `Queue::connection()`, `php artisan queue:work`).

## Quickstart

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);
builder.Services.AddQueueWorker();
await app.Services.GetRequiredService<IJobDispatcher>().DispatchAsync(new MyJob());
```

## Packages

| Package | Driver | Extension method |
|---|---|---|
| `Naravel.Queue` | `memory` (in-process, zero deps) | `AddMemoryDriver(configuration)` |
| `Naravel.Queue.File` | `file` (JSON files on disk) | `AddFileDriver(configuration)` |
| `Naravel.Queue.Redis` | `redis` | `AddRedisDriver(configuration)` |
| `Naravel.Queue.Database` | `database` (any EF Core provider) | `AddDatabaseDriver<TContext>(configuration)` |
| `Naravel.Queue.RabbitMQ` | `rabbitmq` | `AddRabbitMqDriver(configuration)` |
| `Naravel.Queue.Kafka` | `kafka` | `AddKafkaDriver(configuration)` |

## Configuration

Every Naravel module uses one shared keyword, `"Stores"`, for its named connections (PDR-005) - the
C# API still says "connection", matching Laravel:

```json
{
  "NaravelQueue": {
    "Default": "redis",
    "Stores": {
      "redis":   { "Driver": "redis",   "ConnectionString": "localhost:6379", "VisibilityTimeoutSeconds": "300" },
      "backup":  { "Driver": "redis",   "ConnectionString": "backup-redis:6379" },
      "file":    { "Driver": "file",    "Path": "storage/queue" },
      "sync":    { "Driver": "memory" }
    }
  }
}
```

Each store's own `"Driver"` key says which backend handles it - **two stores can use the same driver
type with different settings** (`redis` and `backup` above are both Redis, pointing at different
servers), which is exactly what the driver-registration helper (`AddQueueDriver`, internal to
`Naravel.Queue`) was built to support.

## Quick start

```csharp
using Naravel.Queue.Jobs;
using Naravel.Queue.Extensions;

public class SendWelcomeEmailJob : Job
{
    public string ToEmail { get; set; } = default!;
    public SendWelcomeEmailJob() { }                       // required for deserialization
    public SendWelcomeEmailJob(string toEmail) => ToEmail = toEmail;

    public override Task HandleAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"Sending to {ToEmail} (attempt {context.Attempt}/{context.MaxAttempts})");
        return Task.CompletedTask;
    }
}

// Program.cs
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);
builder.Services.AddQueueWorker(w => { w.Queues = new[] { "default" }; w.Concurrency = 4; });

var dispatcher = app.Services.GetRequiredService<IJobDispatcher>();
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"));
```

## Job registration

Dispatching a job registers its CLR type in the current process. A worker-only process must register every job it can
receive. By default, the registry uses `Type.FullName`; use an explicit alias to keep queued payloads valid when a CLR
type is renamed:

```csharp
builder.Services.AddJob<SendWelcomeEmailJob>("mail.welcome");
// Or: [Job("mail.welcome")] on the job type, then AddJobsFromAssembly(...).
```

Use `Naravel.Queue.Serialization` when applying the `[Job]` attribute.

Only registered aliases are resolved. Assembly-qualified names in existing payloads are not resolved automatically;
deployments with queued legacy messages must register an explicit compatibility alias before upgrading. JSON source
generation can be enabled per job with `AddJob<TJob>(jsonTypeInfo)`.

## Observability and testing

Queue emits `naravel.queue.processed`, `naravel.queue.failed`, and `naravel.queue.retried` counters,
plus the `naravel.queue.processing.duration` histogram through the `Naravel.Queue` meter. The
`Naravel.Queue` activity source creates a consumer span and restores W3C `traceparent`/`tracestate`
from the queued envelope. Metrics do not include job IDs or payload values.

Use `QueueFake` from `Naravel.Queue.Testing` in application tests to assert dispatch without starting
a worker or broker:

```csharp
var queue = new QueueFake();
await queue.DispatchAsync(new SendWelcomeEmailJob("a@x.com"));
queue.AssertDispatched<SendWelcomeEmailJob>();
```

`AssertChained<TJob>()` verifies jobs in a dispatched chain.

## Examples

### Fluent dispatch options

```csharp
await dispatcher.DispatchAsync(new SendWelcomeEmailJob("ali@example.com"), options => options
    .OnConnection("backup")        // pick a specific store by name
    .OnQueue("emails")             // pick a queue within that store
    .DelayFor(TimeSpan.FromMinutes(5))
    .WithPriority(9)               // 0-9, higher runs first on the same queue
    .WithMaxAttempts(5));
```

### Retry with backoff

```csharp
public class ChargeCardJob : Job
{
    public override int MaxAttempts => 5;
    public override TimeSpan[] Backoff => new[]
    {
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)
    };

    public override Task HandleAsync(JobContext context, CancellationToken ct) { /* ... */ return Task.CompletedTask; }

    public override Task FailedAsync(JobContext context, Exception exception, CancellationToken ct)
    {
        // called once, after the last attempt is exhausted
        return Task.CompletedTask;
    }
}
```

### Chaining (static and dynamic)

```csharp
// Static: each step only runs if the previous one succeeded.
await dispatcher.Chain(
    new GenerateInvoiceJob(orderId),
    new SendInvoiceEmailJob(orderId)
).DispatchAsync();

// Dynamic: the job itself decides what runs next.
public override Task HandleAsync(JobContext context, CancellationToken ct)
{
    context.Then(new SendInvoiceEmailJob(orderId));
    return Task.CompletedTask;
}
```

### Batching

```csharp
var batch = await dispatcher.BatchAsync(
    jobs: new IJob[] { new SendWelcomeEmailJob("a@x.com"), new SendWelcomeEmailJob("b@x.com") },
    batchConfigure: b => b.OnCompleted = (batch, ct) =>
    {
        Console.WriteLine($"{batch.CompletedJobs} succeeded, {batch.FailedJobs} failed");
        return Task.CompletedTask;
    });
```

    The default batch repository is in-memory. For state shared across workers and restarts, register
    `AddDatabaseBatchRepository<AppDbContext>()` or `AddRedisBatchRepository("redis")`. The Database
    `ConfigureQueueJobs()` mapping also includes the `QueueBatches` table. Persistent repositories do
    not store delegates; register a typed handler under an explicit alias and persist only its JSON
    payload:

    ```csharp
    var callbacks = new BatchCallbackRegistry();
    callbacks.Register<string>("mail.batch-finished", (batch, note, ct) => SendSummaryAsync(batch, note, ct));
    builder.Services.AddSingleton(callbacks);

    await dispatcher.BatchAsync(jobs, batchConfigure: options =>
      options.Then("mail.batch-finished", "invoice batch complete").Finally("mail.batch-finished", "finished"));
    ```

    Use `Catch(alias, payload)` for failed batches and `Finally(alias, payload)` for either outcome.
    `AllowFailures = true` lets remaining jobs continue after a permanent failure; otherwise the batch
    is cancelled and not-yet-started jobs are acknowledged without execution. Explicit cancellation
    uses `IBatchRepository.CancelAsync(batchId, cancellationToken)`. Callbacks are at-least-once across
    process crashes and should be idempotent. A callback failure leaves its completion state pending so
    the next worker startup can retry it.

### Runtime `Extend` (a driver not known at startup)

```csharp
var manager = app.Services.GetRequiredService<QueueManager>();
manager.Extend("test-double", provider => new MyInMemoryTestDriver());
await dispatcher.DispatchAsync(new MyJob(), o => o.OnConnection("test-double"));
```

### Multiple workers, priority separation

```csharp
builder.Services.AddQueueWorker(w => { w.Connection = "redis"; w.Queues = new[] { "high" }; w.Concurrency = 8; });
builder.Services.AddQueueWorker(w => { w.Connection = "redis"; w.Queues = new[] { "default" }; w.Concurrency = 2; });
```

Worker lifecycle can be bounded with `StopWhenEmpty`, `MaxJobs`, and `MaxRuntime`; `Rest` controls
the delay after an empty poll. `MaxJobs` is shared across a worker's concurrent loops, and
`MaxRuntime` stops new reservations without cancelling an active job. The existing
`SleepWhenEmpty` property remains an alias for `Rest`.

### Database driver setup

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=queue.db"));
builder.Services.AddDatabaseDriver<AppDbContext>(builder.Configuration);
```

```csharp
public class AppDbContext : DbContext
{
    public DbSet<JobRecord> QueueJobs => Set<JobRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureQueueJobs();
}
```
Then create and apply an EF Core migration.

### Failed jobs

`AddQueue()` registers an in-memory failed-job store by default. For persistence, register either
`builder.Services.AddDatabaseFailedJobStore<AppDbContext>()` or
`builder.Services.AddRedisFailedJobStore("redis")`. `ConfigureQueueJobs()` also maps the
`QueueFailedJobs` table; create and apply a migration after adding it. The Redis store uses the
selected Redis queue connection and the `netqueue:failed-jobs` hash by default.

Use `FailedJobManager` to inspect and manage terminal failures:

```csharp
var failed = app.Services.GetRequiredService<FailedJobManager>();
var records = await failed.ListAsync(cancellationToken);
await failed.RetryAsync(records[0].Id, cancellationToken);
```

`RetryAllAsync` reports successful retries and IDs that could not be published. `ForgetAsync` removes
one stored failure; `FlushAsync` removes all stored failures without dispatching them. A retry
publishes the original envelope to its original connection and queue with a new message ID and a
reset attempt count, then removes the failed record after publish succeeds. This is at-least-once:
a process crash between publishing and removing the record can cause a duplicate retry. Job handlers
should remain idempotent.

## Crash recovery

Redis, File and Database drivers return a job to the queue when its worker disappeared for longer
than `VisibilityTimeoutSeconds` (default 300 - set it above your longest job). The Memory driver is
in-process only, so there is nothing to recover from. Kafka and RabbitMQ rely on the broker's own
redelivery, which Naravel does not count; see [Attempt accounting by driver](#attempt-accounting-by-driver).

## Delivery guarantee: at-least-once

A job can run more than once (worker crash, visibility timeout, a failed acknowledgement), so jobs
must be idempotent. Chain follow-ups are published *before* the current job is acknowledged, so a
chain is never silently lost - but that also means a follow-up could be published twice if the
process crashes between publishing it and acknowledging the original job. Design accordingly.

Reclaiming an expired Database, Redis, or File reservation increments the stored attempt count.
When that count reaches `MaxAttempts`, the worker records a permanent failure without executing
the job again. Set `Job.Timeout` for a per-job limit; the worker uses the smaller of that value and
`QueueWorkerOptions.JobTimeout`. Drivers with a visibility timeout emit a warning when it is shorter
than the effective job timeout plus `VisibilityTimeoutMargin` (10 seconds by default). Cancellation
is cooperative; handlers that ignore their token may continue running.

### Attempt accounting by driver

`QueuedMessage.Attempts` is stored on the message envelope. There are two ways it can change:

- **Worker-initiated retry** (the job threw): the worker adds one and releases the message. Every driver
  must hand the next delivery exactly that value, and `Release` itself never adds another. The shared driver
  contract tests assert this across consecutive retries, and a worker test asserts one increment per failure.
  When the count reaches `MaxAttempts` the worker records the failure in the failed-job store and calls the
  driver's `FailAsync`.
- **Crash or expired-reservation redelivery** (the worker disappeared): counted only by drivers that track
  reservations themselves.

| Driver | Worker-initiated retry | Crash / expired-reservation redelivery |
|---|---|---|
| Memory | counted | not applicable (in-process) |
| File, Database, Redis | counted | **counted** when the visibility timeout expires |
| RabbitMQ | counted (the delivery is acknowledged and a new message carrying the updated count is published) | **not counted** |
| Kafka | counted (the job is produced again to the tail of the topic and the original offset is committed) | **not counted** |

On RabbitMQ and Kafka, a message whose worker died before acknowledging it is redelivered by the broker
(unacknowledged deliveries are requeued when the channel or connection closes; an uncommitted Kafka offset is
read again after a restart or rebalance). That is at-least-once delivery, and Naravel's `Attempts` does not
change. A job that kills the whole process every time it runs (for example an out-of-memory crash) can therefore
be redelivered without limit on these two drivers. Mitigate it with idempotent jobs, `Job.Timeout`, process
memory limits, and alerts on repeated worker restarts or growing consumer lag.

Jobs that exhaust `MaxAttempts` are also published to `netqueue.failed.<queue>` (RabbitMQ) or the
`netqueue-<queue>-failed` topic (Kafka). Naravel never consumes these; inspect them with broker tooling and use
`FailedJobManager` to retry.

#### RabbitMQ: broker-side delivery limit and dead-lettering

Checked against the RabbitMQ [quorum queue](https://www.rabbitmq.com/docs/quorum-queues) and
[dead letter exchange](https://www.rabbitmq.com/docs/dlx) documentation on 2026-10-11:

- Quorum queues support a delivery limit: the policy key `delivery-limit` or the queue argument `x-delivery-limit`.
  The default is 20 since RabbitMQ 4.0. A message returned more times than the limit is dropped, or dead-lettered
  when a dead letter exchange is configured. From RabbitMQ 4.3 the limit counts failed deliveries (for example a
  client crash or lost connection, and `basic.reject`) and not a plain requeue with `basic.nack`; earlier versions
  counted every acquisition, so check your broker version.
- A dead letter exchange is set with the policy key `dead-letter-exchange` or the queue argument
  `x-dead-letter-exchange`; the argument overrides the policy. Messages are dead-lettered on rejection without
  requeue, TTL expiry, a queue length limit, and the quorum delivery limit.
- The queue type is chosen when the queue is declared (`x-queue-type`) and cannot be set or changed by a policy.
  `x-max-priority` applies to classic queues only and is ignored by quorum queues; strict quorum priorities exist
  from RabbitMQ 4.3.

**Current limitation:** the driver declares `netqueue.ready.<queue>` as a classic queue with `x-max-priority`, and
declares it again on every operation. Because the type cannot be changed by policy, and RabbitMQ rejects
re-declaring an existing queue with different arguments, an operator cannot turn this queue into a quorum queue,
and so cannot use `delivery-limit`, without driver support. A configurable queue type is not implemented and
needs its own decision (AUDIT D-27). Until then, crash redelivery on RabbitMQ is unbounded and uncounted, as described
above. Kafka has no broker-side redelivery limit; the committed offset is the only state.

## Limitations

- **Kafka**: retries move to the tail of the topic (order is not preserved); `PopAsync` prioritizes
  up to 256 records already available in one poll batch, but later arrivals are not reordered; a
  delayed job blocks its partition until due; `SizeAsync` returns -1. Each queue uses its own consumer group
  (`<groupId>-<queue>`), so an idle consumer of one queue cannot stall another queue. **Upgrade note (pre-1.0):**
  the new group ids start from the earliest retained offset, so messages that were already acknowledged under the old
  shared group id may be processed again; drain Kafka queues before upgrading. Polling still blocks a worker thread (D-21).
- **RabbitMQ**: uses RabbitMQ.Client 7 async operations and a separate channel for each in-flight
  delivery so acknowledgements stay on their originating channel. A long-delay message can hold
  back shorter ones queued after it. Existing ready queues created without `x-max-priority` must be
  deleted and recreated before upgrading the provider.
- **Database**: every store using the `database` driver against the same `DbContext` type shares one
  table with no store/connection column - use different `DbContext` types if you need to tell them
  apart.
- **New stores added after startup are not picked up automatically.** Each `AddXxxDriver()` scans
  configured stores once, at startup, to decide which store names it should register a factory for.
  Changing an *existing* store's settings and reloading configuration works (Naravel.Foundation
  rebuilds the driver); adding a brand-new store name requires a restart. This matches Laravel, which
  also does not let you add a new named connection without a code/config file change and a restart.
- Batch tracking is in-memory by default; use `AddDatabaseBatchRepository<TContext>()` or
  `AddRedisBatchRepository()` for durable multi-process state.

## Testing

`tests/Naravel.Queue.Tests` covers:
- `QueueDriverContractTests` - reusable checks for pop-once, priority, delay, release, failure,
  size, queue isolation and concurrent consumers; run against Memory and File.
- `FileDriverRecoveryTests` - reservation reclaim after a worker crash.
- `WorkerTests` - exactly-once execution, retry until success, permanent failure calling
  `FailedAsync` once, delayed dispatch, static and dynamic chaining (including a chain stopping on
  permanent failure), failed-job retry, timeouts, worker controls, batch cancellation/failure policy,
  and a throwing callback that neither reruns the job nor kills the worker.
- `QueueFakeTests` and `QueueTelemetryTests` - dispatch/chain assertions, Meter instruments, duration
  histogram, and W3C trace context restoration.
- `ManagerIntegrationTests` - the PDR-006 migration: two stores sharing one driver type stay
  isolated, runtime `Extend`, a config reload that changes a store rebuilds its driver, and an
  unrelated reload does not.

`tests/Naravel.Queue.Providers.Tests` reuses the contract for Database (SQLite, always runs), Redis,
RabbitMQ and Kafka. Set `NARAVEL_TEST_REDIS=host:port`,
`NARAVEL_TEST_RABBITMQ=amqp://user:password@host:5672/%2f`, or `NARAVEL_TEST_KAFKA=host:port` to
enable each live test; otherwise xUnit reports it as skipped. Redis tests exercise Lua-backed atomic
state transitions plus persistent failed-job and batch stores. SQLite tests cover retry, batch recovery/
cancellation, reclaim attempts, and trace-envelope persistence. Kafka's offset tracker has unit coverage for out-of-order acknowledgements. Cache
provider tests use `NARAVEL_TEST_REDIS` and `NARAVEL_TEST_MEMCACHED=host:port` in the same way.

The `services` CI job runs live tests with Redis, RabbitMQ, Kafka and Memcached containers. The
`fast` job builds and tests the full solution without external services on Ubuntu, Windows and macOS.
