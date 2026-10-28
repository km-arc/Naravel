# PDR-023 — Transactional enqueue for the database queue (outbox-by-construction)

**Native-first verdict:** EF Core's `SaveChanges` already commits every tracked change atomically. The Database queue stores jobs as rows of `QueueJobs`, so a job can be one more tracked row in the caller's own `DbContext`. No new infrastructure is needed.

Status: **Approved 2026-10-28** by the owner's instruction ("create the PDR, then implement option 2").

## Context

An analysis of transaction and dispatch (2026-10-28) found a dual-write gap. `JobDispatcher.DispatchAsync` pushes through `IQueueDriver`, and `DatabaseQueueDriver` opens its **own** `DbContext` from `IDbContextFactory` and saves immediately. A job is therefore never part of the caller's transaction, even when the queue table and the business tables are in one database:

| Scenario | Today |
|---|---|
| Commit succeeds, dispatch fails or the process crashes before it | the message is **lost**, silently |
| Dispatch succeeds, the transaction then rolls back | the job runs for data that does not exist |
| Worker crashes after the job ran, before the ack | the job runs again (at-least-once, already documented; needs idempotent jobs) |

Laravel answers the second case with `afterCommit` (not ported on purpose, PDR-022) and has no built-in answer to the first.

## Decision

Add an opt-in `DatabaseJobEnqueuer` to `Naravel.Queue.Database`:

```csharp
db.Orders.Add(order);
enqueuer.Enqueue(db, new SendReceiptJob(order.Id));   // tracked, not sent yet
await db.SaveChangesAsync();                          // order and job commit together, or neither
```

- `Enqueue(DbContext, IJob, Action<DispatchOptions>? = null)` builds the message exactly as `DispatchAsync` does (same serializer, alias registry, queue, attempts, delay, priority, trace context) and adds one `JobRecord` to the context. It returns the job id. It performs no I/O.
- The existing worker is the relay: it already polls `QueueJobs` and claims rows with a compare-and-swap. There is no second table, no relay process and no new package.
- To share the factory without duplicating `BuildMessage`, `Naravel.Queue` gains a small public `IJobMessageFactory` implemented by `JobDispatcher`.

### Contract

- The context must map `JobRecord` (`ConfigureQueueJobs`). The job is committed with whatever transaction `SaveChanges` uses, including an explicit `BeginTransactionAsync` and the EF adapter's shared connection.
- `IUniqueJob` is **rejected** (`ArgumentException`): its gate lives in the cache and cannot join a database transaction. Chains and batches are not supported by this method.
- `options.Connection` is ignored: the job is stored in this database; a worker must consume the `database` connection and the chosen queue.
- Delivery stays at-least-once. This closes the lost-message and phantom-message rows above, not redelivery; jobs stay idempotent (`queue.md`).
- Nothing changes for existing callers of `DispatchAsync`.

## Not decided here (explicitly out of scope)

An outbox for Redis, Kafka and RabbitMQ (a separate table and relay that publishes to a broker, with its own ordering, cleanup and duplicate-publish rules). That is a larger design; it needs its own PDR if a deployment needs broker delivery to be loss-free after commit. Until then those drivers keep dispatch-after-commit with the documented gap.

## Alternatives considered

1. **`afterCommit` callbacks.** Rejected: fixes the phantom message only; a crash after commit still loses it.
2. **Make `DatabaseQueueDriver.PushAsync` join the ambient transaction.** Rejected: implicit, surprising, and impossible when the driver does not know the caller's context.
3. **Full outbox package now.** Deferred (see above).

## Verification

Tests on SQLite: committed together (job visible to the driver only after `SaveChanges`), rolled back together (no job), explicit transaction rollback, delay and queue options honoured, unique job rejected, unmapped context fails clearly. EN/FA docs and changelog updated.
