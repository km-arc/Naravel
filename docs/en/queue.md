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

> **Kafka does not support delayed delivery** (`DelayFor`, `RunAt`, retry `Backoff`). See
> [Kafka: no delayed delivery](#kafka-no-delayed-delivery) before choosing it for jobs that wait.

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

> **Backoff on Kafka:** the Kafka driver cannot hold one job back without holding back the whole queue, so a retry
> there runs immediately and the `Backoff` is not applied (the worker logs this once per job type). Use Redis,
> Database or RabbitMQ for spaced retries. Details: [Kafka: no delayed delivery](#kafka-no-delayed-delivery).

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
Then create and apply an EF Core migration. **EF Core migrations are the production path** for every provider.

#### SQLite script and development/test helper

For development, tests and tooling on SQLite, `QueueSchema` ships the DDL for `QueueJobs`, `QueueFailedJobs` and
`QueueBatches` (also kept as `src/Naravel.Queue.Database/Schema/sqlite.sql`, so it can be run with the `sqlite3` CLI):

```csharp
// Opt-in: nothing creates tables on its own.
await using var db = await factory.CreateDbContextAsync();
await QueueSchema.EnsureSqliteSchemaAsync(db, app.Environment);   // CREATE TABLE IF NOT EXISTS ...
```

- The helper only runs when the environment is `Development`, `Test` or `Testing`; any other environment throws
  `InvalidOperationException` and creates nothing. It also requires the SQLite provider.
- It is idempotent, so calling it on every start of a development database is safe.
- The script assumes the date columns of `JobRecord` are stored as UTC ticks (`INTEGER`), because SQLite cannot compare
  `DateTimeOffset` values stored as text in the polling query. Add this to your context:

```csharp
var utcTicks = new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, t => new DateTimeOffset(t, TimeSpan.Zero));
modelBuilder.Entity<JobRecord>().Property(j => j.CreatedAt).HasConversion(utcTicks);
modelBuilder.Entity<JobRecord>().Property(j => j.AvailableAt).HasConversion(utcTicks);
modelBuilder.Entity<JobRecord>().Property(j => j.ReservedAt).HasConversion(utcTicks);
modelBuilder.Entity<JobRecord>().Property(j => j.FailedAt).HasConversion(utcTicks);
```

- The script uses the default table names. If you pass other names to `ConfigureQueueJobs(tableName)`, use migrations.
- A test keeps the script and the model in step: it compares the script's columns with the model's mapped columns.

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

### Reservation fencing (Redis, File, Database)

Every `PopAsync` stamps the message with a fresh `QueuedMessage.ReservationId`. `AckAsync`, `ReleaseAsync` and
`FailAsync` only act while that token is still the stored one, and reclaiming an expired reservation clears it. A
worker that outlives its `VisibilityTimeoutSeconds` therefore cannot delete, re-queue or fail the copy another worker
now owns: its late acknowledgement is a silent no-op and the job simply runs a second time (at-least-once, as before).
Messages without a token (envelopes written before this field existed) keep the previous unfenced behaviour, and
Memory, RabbitMQ and Kafka are unchanged because they have no expiry-based reclaim in the driver.

Upgrading the **Database** driver adds one nullable column, `ReservationId varchar(64)`, to the jobs table. Apply an EF
migration (for the default table: `ALTER TABLE QueueJobs ADD ReservationId varchar(64) NULL`) **before** deploying the
new package, otherwise polling fails with an unknown-column error. Fencing does not make a long job safe: it still
runs twice if it exceeds the visibility timeout, so keep `VisibilityTimeoutSeconds` above your longest job.

## Delivery guarantee: at-least-once

A job can run more than once (worker crash, visibility timeout, a failed acknowledgement), so jobs
must be idempotent. Chain follow-ups are published *before* the current job is acknowledged, so a
chain is never silently lost - but that also means a follow-up could be published twice if the
process crashes between publishing it and acknowledging the original job. Design accordingly.

Reclaiming an expired Database, Redis, or File reservation increments the stored attempt count.
When that count reaches `MaxAttempts`, the worker records a permanent failure without executing
the job again. Set `Job.Timeout` for a per-job limit; the worker uses the smaller of that value and
`QueueWorkerOptions.JobTimeout`. Cancellation is cooperative; handlers that ignore their token may
continue running.

#### Startup validation of the visibility timeout

For drivers that reclaim expired reservations (Database, Redis, File) the worker checks the driver's
visibility timeout when it starts:

- **Error:** the visibility timeout is shorter than `QueueWorkerOptions.JobTimeout`. The job would be
  reclaimed and executed a second time while it is still allowed to run, so `StartAsync` throws an
  `InvalidOperationException` (the host fails to start) and the error is logged.
- **Warning:** `QueueWorkerOptions.MaxJobTimeout` is set and the visibility timeout is shorter than
  `MaxJobTimeout + VisibilityMargin` (10 seconds by default). Set `MaxJobTimeout` to the longest
  `Job.Timeout` you use; when it is unset no such warning is produced.
- A job whose own `Job.Timeout` is longer than the visibility timeout minus the margin still logs a
  warning when it is first processed.
- Drivers without a visibility timeout (Memory, RabbitMQ, Kafka) are not validated.

`VisibilityMargin` replaces the earlier `VisibilityTimeoutMargin` name.

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
| Kafka | counted (the job is produced again to the tail of the topic for immediate delivery, with no backoff, and the original offset is committed) | **not counted** |

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

#### Kafka: no delayed delivery

*Decision: PDR-012a (AUDIT D-29), approved 2026-10-13.*

**In short:** the Kafka driver does not delay jobs. A delayed dispatch is rejected with an error, and a failed job is
retried immediately instead of after its backoff. For delayed jobs or spaced retries use Redis, Database or RabbitMQ.

**Why.** Kafka is a log, not a queue. Think of a tape: records are written one after another, and a consumer group
keeps a single bookmark per partition saying "everything before this is done". It cannot skip a record in the middle
and come back to it later. So a record that is not due yet cannot be set aside; everything behind it has to wait too.

Example: three jobs are on one partition, in this order.

1. job A, an email that must run in 60 seconds
2. job B, an SMS that is ready now
3. job C, an invoice that is ready now

Redis, Database and RabbitMQ run B and C immediately and A after 60 seconds. Kafka cannot: A sits at the head, is not
due, and B and C wait behind it for 60 seconds. Earlier versions of the driver did exactly that.

**Why a retry was worse.** When a job fails, the worker puts it back with its backoff (5 to 60 seconds by default).
On Kafka that is a delayed record like job A, so one failing job could silently stall every healthy job in the queue
for up to a minute, with no error anywhere. The old documentation only mentioned long delays, not this case.

**What happens now**

| Case | Behavior |
|---|---|
| `DispatchAsync(job, o => o.DelayFor(...))` or `RunAt(...)` on a Kafka store | rejected with a `NotSupportedException` that names the queue and points here; nothing is produced |
| A job fails and has a `Backoff` | produced again for immediate delivery; the backoff is **not** applied; a warning is logged once per job type; `OnRetryingAsync` reports a zero delay |
| Chains, failed-job retries and batches | unchanged: they are produced without a delay |
| A record already in a topic with a future `AvailableAt` (written by an older preview) | still honored for compatibility; it blocks its partition until due, as before |

Because the retry is immediate, a job that fails for a reason that needs time to clear (a downstream service that is
down) will burn through its `MaxAttempts` quickly on Kafka. Keep such jobs on a store that supports delay, or make
them tolerate the failure inside `HandleAsync`.

`IQueueDriver.SupportsDelayedDelivery` tells the worker (and your own code) whether a driver can delay. It defaults
to `true`; only the Kafka driver returns `false`.

**Alternatives that were considered and rejected**

- *Pause the partition and poll on a timer.* It removes the CPU cost of re-reading the same record, but Kafka still
  cannot skip a record, so the head-of-line blocking stays.
- *One shared delay topic.* The same blocking, one level down, because the head of that topic blocks the rest of it.
- *Delay topics per fixed step (for example 5 s, 30 s, 5 min).* This works, because all records in a topic then have the
  same delay, but it needs more topics, rounds delays up to a step, and a forwarding loop that must itself tolerate
  duplicates after a crash. It is a large piece of concurrent code that needs a live broker to verify, so it is
  deferred to the backlog and will be revisited only if users need delayed jobs on Kafka.
- *Sleeping inside the worker.* It would hold a worker slot and an uncommitted offset, and the delay would be lost on
  restart.

## Limitations

- **Kafka**: retries move to the tail of the topic (order is not preserved); `PopAsync` prioritizes
  up to 256 records already available in one poll batch, but later arrivals are not reordered; a
  delayed delivery is not supported: a delayed dispatch is rejected and a retry runs immediately without its backoff
  ([Kafka: no delayed delivery](#kafka-no-delayed-delivery), PDR-012a); `SizeAsync` returns -1. Each queue uses its own consumer group
  (`<groupId>-<queue>`), so an idle consumer of one queue cannot stall another queue. **Upgrade note (pre-1.0):**
  the new group ids start from the earliest retained offset, so messages that were already acknowledged under the old
  shared group id may be processed again; drain Kafka queues before upgrading.
  **Blocking poll (D-21):** `PopAsync` calls the synchronous `IConsumer.Consume` with a 200 ms timeout, so an idle poll
  occupies one thread-pool thread for up to 200 ms per queue (pops on the same queue are serialized, so concurrent workers
  wait on an async gate, not on extra threads). The timeout is not cancellable: stopping a worker is observed between
  polls, i.e. within about 200 ms. This is a known limitation of the current implementation; an asynchronous consume loop
  is a separate change that needs its own PDR and is not planned in this release.
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
