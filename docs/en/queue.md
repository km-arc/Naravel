# Naravel.Queue

A Laravel-inspired, driver-based background job queue for .NET, built on `Naravel.Foundation`
(`Manager<IQueueDriver, QueueOptions>`). Laravel equivalent: `illuminate/queue`
(`Queue::push()`, `Queue::connection()`, `php artisan queue:work`).

## Quickstart

The shortest working setup: one job, one in-process worker, no broker. The memory driver needs no package beyond `Naravel.Queue.Memory`, so you can try everything on this page before choosing a real driver.

```sh
dotnet add package Naravel.Queue.Memory
```

```json
{
  "NaravelQueue": {
    "Default": "local",
    "Stores": { "local": { "Driver": "memory" } }
  }
}
```

```csharp
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);
builder.Services.AddQueueWorker();                          // the worker: a hosted service that runs the jobs

var app = builder.Build();

app.MapPost("/welcome", async (string email, IJobDispatcher jobs, CancellationToken ct) =>
{
    var id = await jobs.DispatchAsync(new SendWelcomeEmailJob(email), cancellationToken: ct);
    return Results.Accepted(value: new { id });             // answers at once; the job runs on the worker
});

app.Run();

public sealed class SendWelcomeEmailJob : Job
{
    public string ToEmail { get; set; } = default!;

    public SendWelcomeEmailJob() { }                        // the worker rebuilds the job from JSON, so it needs this constructor
    public SendWelcomeEmailJob(string toEmail) => ToEmail = toEmail;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Sending to {ToEmail} (attempt {context.Attempt} of {context.MaxAttempts})");
        return Task.CompletedTask;
    }
}
```

`POST /welcome?email=ada@example.com` answers `202 Accepted` with the message id, and the console shows the line from the job a moment later. The worker takes jobs from the `default` queue, one at a time.

The second argument of `DispatchAsync` is the dispatch options (`o => o.DelayFor(...)`), so pass the token by name: `cancellationToken: ct`.

Memory jobs live in the process: they are gone when it stops, and a second process cannot see them. For jobs that must survive a restart or run on another machine, choose a driver from the table below; the application code stays the same.

## Packages

| Package | Driver | Extension method |
|---|---|---|
| `Naravel.Queue.Memory` | `memory` (in-process, zero deps) | `AddMemoryDriver(configuration)` |
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

## The same job on Redis, with worker settings

The quickstart job again, with the Redis driver and explicit worker settings.

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

`DispatchAsync` returns the message id as `string?`. It is `null` only when the job is an `IUniqueJob` and an identical job is already pending or
running (the dispatch was rejected and nothing was queued). See [queue-coordination](queue-coordination.md#unique-jobs-iuniquejob).

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

### A reserved message is never left behind

Between taking a message from the queue and calling `HandleAsync`, the worker checks whether the job's batch was
cancelled, opens a DI scope, resolves the `IQueueEventListener` services and tells them the job is starting,
deserializes the payload and builds the middleware chain. Each of those can throw: the batch store is down, a listener
or middleware constructor fails, the payload no longer deserializes, the job alias is unknown, a decryption fails.
Each one now takes the same path as a job that threw: the attempt is counted, the message is released with a backoff
on drivers that can delay (the job's own `Backoff` once the job exists, otherwise 5 seconds per attempt up to 60), and
after `MaxAttempts` the failure is recorded in the failed-job store and the driver. Before this, such a message stayed
reserved: until the visibility timeout on Redis, File and Database, until the process ended on Memory, until the channel
closed on RabbitMQ, and until a restart or rebalance on Kafka.

Three rules close the remaining gaps:

- **Settling can fail too.** If the release, the failed-job store or the driver's fail call throws while the worker is
  settling a message, the worker hands the message back once more, without counting another attempt. On a shutdown it
  hands it back immediately; for any other error it waits one `Rest` on drivers that can delay, so a broken store is not
  hit in a loop. If that fails as well, the worker logs an error and the message stays reserved until the driver
  reclaims it (the visibility timeout) or the broker redelivers it (see
  [Attempt accounting by driver](#attempt-accounting-by-driver)).
- **A settled message is not touched again.** Once a message was acknowledged, released or failed, a later error (for
  example the batch store failing while a skipped or batch-cancelled job is being counted) is only logged. It is never
  routed back into the retry path, which would put a finished job on the queue again.
- **Stopping never cancels the hand-back.** The release that gives back a message the worker will not process (the
  worker reached `MaxJobs` or `MaxRuntime`, or the host is stopping) ignores the stopping token, because a driver that
  honours its token would otherwise throw and leave the message reserved.

A pre-execution failure counts as an attempt, like any other failure, so a permanent one (a payload that can never be
deserialized) ends in the failed-job store after `MaxAttempts`. An unknown job alias is retried too, because during a
rolling deployment another worker may know it. Redis, File and Database fence the hand-back to the reservation: if
another worker already owns the message, the call changes nothing.

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

### Dispatch and the database transaction (transactional enqueue)

`IJobDispatcher.DispatchAsync` pushes immediately, so a job is **not** part of your database transaction: if the process dies after the commit and before the dispatch, the job is lost; if you dispatch first and the transaction rolls back, the job runs for data that does not exist. With the Database queue you can close both gaps (PDR-023): add the job to your own `DbContext` and let one `SaveChanges` commit both.

```csharp
services.AddTransactionalEnqueue();                       // Naravel.Queue.Database

db.Orders.Add(order);
enqueuer.Enqueue(db, new SendReceiptJob(order.Id));       // tracked only; nothing is sent yet
await db.SaveChangesAsync();                              // the order and the job commit together, or neither
```

The context must map the jobs table (`ConfigureQueueJobs`). The same holds inside an explicit transaction or the EF adapter's shared connection. The existing database worker is the relay. Limits: `IUniqueJob` is rejected (its gate is in the cache), chains and batches are not supported, `Connection` is ignored (a worker must consume the `database` connection), and delivery is still at-least-once, so keep jobs idempotent as below. For Redis, Kafka and RabbitMQ there is no outbox yet: dispatch after the commit and accept the documented gap.

### Idempotent jobs

Delivery is at-least-once, so a job that changes money, stock or any external state must be safe to run twice. Naravel does **not** ship an idempotency-key store or a stored job result. Two features look similar and are not:

- `IUniqueJob` (see [queue-coordination](queue-coordination.md#unique-jobs-iuniquejob)) rejects a *second dispatch* while an identical job is pending or running. It does not stop a *redelivery* of the same message, and once the job finishes the key is free again.
- `WithoutOverlapping` and the other coordination middleware reduce concurrent execution and duplicates, but they do not make a job run exactly once.

The reliable pattern is to record a unique operation key **in the same database transaction as the business change**. A redelivered job finds the key and does nothing; two concurrent deliveries are separated by the unique key.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Jobs;

// 1. A table whose primary key is the operation key. The unique key is what makes a second delivery harmless.
public class ProcessedOperation
{
    public string Key { get; set; } = default!;
    public DateTimeOffset ProcessedAt { get; set; }
}
// AppDbContext: public DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
// OnModelCreating: modelBuilder.Entity<ProcessedOperation>().HasKey(x => x.Key);

// 2. The job carries the caller's key (for example a client-generated transaction id).
public class DepositJob : Job
{
    public string UserId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string TransactionId { get; set; } = default!;

    public DepositJob() { }                                    // required for deserialization
    public DepositJob(string userId, decimal amount, string transactionId)
        => (UserId, Amount, TransactionId) = (userId, amount, transactionId);

    public override async Task HandleAsync(JobContext context, CancellationToken ct)
    {
        var db = context.Services.GetRequiredService<AppDbContext>();   // one scope per job execution
        var key = $"deposit:{TransactionId}";

        if (await db.ProcessedOperations.AnyAsync(x => x.Key == key, ct))
            return;                                                      // a previous delivery already did the work

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ProcessedOperations.Add(new ProcessedOperation { Key = key, ProcessedAt = DateTimeOffset.UtcNow });
        await db.Users.Where(u => u.Id == UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Balance, u => u.Balance + Amount), ct);   // atomic in the database
        try
        {
            await db.SaveChangesAsync(ct);                               // inserts the key; the unique key rejects a duplicate
            await tx.CommitAsync(ct);                                    // the key and the balance change commit together
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another delivery of the same operation won the race; the transaction is rolled back, so nothing was applied twice.
        }
    }

    // Provider specific: SQL Server error 2601/2627, PostgreSQL SQLSTATE 23505, SQLite constraint error.
    private static bool IsUniqueViolation(DbUpdateException ex) => /* inspect ex.InnerException for your provider */ false;
}

// 3. Dispatch with a key the client generated once; retries of the same request reuse it.
await dispatcher.DispatchAsync(new DepositJob("user-123", 100m, transactionId));
```

Notes:

- The check before the transaction is only a fast path. The unique key is what guarantees correctness when two deliveries run at the same time.
- Use an atomic update (`ExecuteUpdateAsync`, or a row lock) instead of "read the balance, add, save": that read-modify-write loses updates under concurrency.
- Keep the key stable across retries of the same business operation (a client-supplied id), never a value generated inside the job.
- Delete old `ProcessedOperation` rows after the longest period in which a duplicate can still arrive.
- Calls to external systems (payment providers, mail) are outside your transaction: pass them the same operation key as their own idempotency key where they support one.

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

**Queue type.** By default the driver declares `netqueue.ready.<queue>` as a classic queue with `x-max-priority`, as
before. Set `QueueType` to `Quorum` on the store (and optionally `DeliveryLimit`) to declare it as a quorum queue:

```json
"rabbit": { "Driver": "rabbitmq", "HostName": "localhost", "QueueType": "Quorum", "DeliveryLimit": 5 }
```

A quorum ready queue drops message priorities (RabbitMQ ignores `x-max-priority` there) and is dead-lettered to
`netqueue.failed.<queue>` when a message exceeds the delivery limit, so crash redelivery becomes bounded and the
message is kept for inspection. RabbitMQ fixes the queue type at declaration and rejects a re-declaration with
different arguments, so an existing classic queue must be drained and deleted before you switch (the driver
fails with `PRECONDITION_FAILED` otherwise). `DeliveryLimit` needs `Quorum`. The delay and failed queues stay
classic. Kafka has no broker-side redelivery limit; the committed offset is the only state.

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
  **Polling (D-21, closed):** `PopAsync` no longer blocks a thread. It polls the consumer with a zero timeout and awaits
  a 20 ms delay between polls for up to 200 ms, so an idle poll holds no thread and a cancelled token (worker stop)
  takes effect within about 20 ms. Pops on the same queue are still serialized by an async gate.
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

## Errors

**Setup.** Raised when the application starts or when a store is first used.

| When | Exception and message |
|---|---|
| A store lacks a required setting (Redis: `ConnectionString` unless it uses a shared Redis connection; Kafka: `BootstrapServers`) | `InvalidOperationException`: `Naravel.Queue store 'NaravelQueue:Stores:redis' is missing required setting 'ConnectionString'.` |
| No default store is configured, or a store name is unknown | Foundation's errors, see the [Foundation guide](foundation.md#errors). |
| The driver's visibility timeout is shorter than `QueueWorkerOptions.JobTimeout` | `InvalidOperationException`, also logged at error level, so the worker does not start: `Naravel.Queue: visibility timeout 00:01:00 on connection 'redis' is shorter than the worker JobTimeout 00:05:00; a running job would be reclaimed and executed twice. Raise the driver's visibility timeout or lower JobTimeout.` See [startup validation](#startup-validation-of-the-visibility-timeout). |

**Job registration.** The registry is built the first time it is needed, so a duplicate alias surfaces then, not at `AddJob`.

| When | Exception and message |
|---|---|
| Two job types share one alias | `InvalidOperationException`: `Queue job alias 'mail.welcome' is already registered for 'MyApp.OldWelcomeJob'.` |
| The type is not a concrete `IJob` | `ArgumentException`: `'MyApp.IWelcome' must be a concrete IJob type.` |
| An empty alias | `ArgumentException`: `A job alias cannot be empty.` |
| The JSON type info is for another type | `ArgumentException`: `The JSON type info must describe the registered job type.` |
| A worker reads a message whose alias it does not know (a worker-only process that never registered the job, or a renamed class without an alias) | `UnknownJobTypeException`: `Queue job type alias 'mail.welcome' is not registered.` See [job registration](#job-registration). |
| The payload is not a job | `InvalidDataException`: `Queue payload for alias 'mail.welcome' did not deserialize to an IJob.` |

**Dispatching.**

| When | Exception and message |
|---|---|
| An `IUniqueJob` without the coordination package | `InvalidOperationException`: `SendWelcomeEmailJob implements IUniqueJob, but no IUniqueJobGate is registered, so uniqueness cannot be enforced. Call services.AddQueueCoordination() (package Naravel.Queue.Coordination, which needs AddNaravelCache()).` |
| An `IUniqueJob` inside a chain or a batch | `ArgumentException`: `ReportJob implements IUniqueJob and cannot be part of a chain: a rejected duplicate would stall the sequence. Dispatch it on its own.` (`a batch` for a batch) |
| `Chain()` with no job | `ArgumentException`: `A chain must contain at least one job.` |
| A batch callback registered twice | `InvalidOperationException`: `A batch callback is already registered as 'done'.` |
| A worker finishes a batch whose callback alias it never registered | `InvalidOperationException`: `No batch callback is registered as 'done'.` |

**Running.**

| When | What happens |
|---|---|
| A middleware returns without calling `next`, `JobContext.Release` or `JobContext.Skip` | The job is not run and fails with `InvalidOperationException`: `A job middleware returned without calling next and without calling JobContext.Release or JobContext.Skip, so the job was not run. Call one of them, or call next.` |
| `JobContext.Release` with a negative delay | `ArgumentOutOfRangeException`: `The delay cannot be negative.` |
| A job throws | It is retried by `MaxAttempts` and `Backoff`, then goes to the failed-job store; see [Failed jobs](#failed-jobs). |

`QueueFake` assertions throw `InvalidOperationException`:

| Assertion | Message |
|---|---|
| `AssertDispatched<SendWelcomeEmailJob>()` when none was dispatched | `No SendWelcomeEmailJob job was dispatched.` |
| `AssertNotDispatched<SendWelcomeEmailJob>()` when one was | `Expected no SendWelcomeEmailJob job to be dispatched, but 1 was.` (`but 2 were.` for more) |
| `AssertDispatchedCount<SendWelcomeEmailJob>(2)` when one was | `Expected 2 SendWelcomeEmailJob dispatch(es), but found 1.` |
| `AssertChained<SendWelcomeEmailJob>()` when no chain holds one | `No SendWelcomeEmailJob job was found in a dispatched chain.` |

`tests/Naravel.Queue.Tests/DocsExampleTests.cs` runs the quickstart and the registration and `QueueFake` messages above. The other messages come from the source and are not run there. Change one, change the other.

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
