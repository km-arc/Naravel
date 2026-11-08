# Naravel.Queue

A Laravel-inspired, driver-based background job queue for .NET. Dispatch, retry with backoff, chaining, batching and a background worker - core package (add Naravel.Queue.Memory for the in-process driver, or Naravel.Queue.Redis/.File/.Database/.RabbitMQ/.Kafka for other backends).

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue
```

Targets .NET 10. It depends on `Naravel.Foundation`, which NuGet installs with it.

## Use

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

builder.Services.AddQueue(builder.Configuration);   // reads the "NaravelQueue" section; the driver package adds its own AddXxxDriver call after it
builder.Services.AddQueueWorker();                  // the hosted service that runs the jobs; a process that only dispatches leaves it out

public sealed class SendWelcomeEmail : Job
{
    public string ToEmail { get; set; } = "";

    public SendWelcomeEmail() { }                   // the worker rebuilds the job from JSON, so it needs this constructor
    public SendWelcomeEmail(string toEmail) => ToEmail = toEmail;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;                         // your work goes here
}

// jobs is an injected IJobDispatcher
var id = await jobs.DispatchAsync(new SendWelcomeEmail("ada@example.com"), cancellationToken: ct);
```

The store above uses the `memory` driver, so it needs `Naravel.Queue.Memory` and its `AddMemoryDriver(builder.Configuration)` call; every other driver is chosen the same way, by the `Driver` of the store. `DispatchAsync` returns the message id, or `null` when the job is an `IUniqueJob` (it needs `AddQueueCoordination()` from `Naravel.Queue.Coordination`) and an identical job is already waiting or running; with `UniqueUntilProcessing` a running one no longer counts. The lock is bounded by the job's `UniqueFor` (default one hour), so a job that stays waiting or running longer than that no longer blocks a new dispatch, and on a Memory cache store uniqueness holds inside one process only. The worker takes jobs from the `default` queue, one at a time, unless `AddQueueWorker` is given other `Queues` or a `Concurrency`.

## Related packages

- [`Naravel.Queue.Memory`](https://www.nuget.org/packages/Naravel.Queue.Memory): In-process "memory" queue driver for Naravel.Queue - zero dependencies, ideal for tests and local development.
- [`Naravel.Queue.File`](https://www.nuget.org/packages/Naravel.Queue.File): File-based queue driver for Naravel.Queue - durable, zero external services required, JSON files on disk.
- [`Naravel.Queue.Redis`](https://www.nuget.org/packages/Naravel.Queue.Redis): Redis queue driver for Naravel.Queue, built on StackExchange.Redis - priority buckets, delayed jobs and automatic reclaim of jobs left behind by a crashed worker.
- [`Naravel.Queue.Database`](https://www.nuget.org/packages/Naravel.Queue.Database): EF Core-backed queue driver for Naravel.Queue - works with any EF Core provider (SQL Server, PostgreSQL, SQLite, MySQL).
- [`Naravel.Queue.RabbitMQ`](https://www.nuget.org/packages/Naravel.Queue.RabbitMQ): RabbitMQ queue driver for Naravel.Queue - delayed jobs via per-message TTL + dead-lettering, no plugin required.
- [`Naravel.Queue.Kafka`](https://www.nuget.org/packages/Naravel.Queue.Kafka): Kafka queue driver for Naravel.Queue, built on Confluent.Kafka - semantics adapted for a log-based broker (see class docs for details).
- [`Naravel.Queue.Coordination`](https://www.nuget.org/packages/Naravel.Queue.Coordination): Optional Naravel.Queue job middleware that coordinates workers through Naravel.Cache locks and rate limits: WithoutOverlapping, RateLimited and ThrottlesExceptions.
- [`Naravel.Queue.Encryption`](https://www.nuget.org/packages/Naravel.Queue.Encryption): Optional Naravel.Queue adapter that encrypts the payload of jobs marked IShouldBeEncrypted with ASP.NET Core Data Protection. Unmarked jobs keep the existing wire format.

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
