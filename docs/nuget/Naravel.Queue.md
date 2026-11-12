# Naravel.Queue

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.svg)](https://www.nuget.org/packages/Naravel.Queue)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

A Laravel-inspired, driver-based background job queue for .NET: dispatch, retry with backoff, chaining, batching and a background worker.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Foundation`, which NuGet installs with it |
| **Drivers** | install one next to this package: `Memory` (in-process), `File`, `Redis`, `Database`, `RabbitMQ` or `Kafka` (listed below) |

## What you get

- **Dispatch with options.** Per job you choose the connection, the queue, a delay, a priority from 0 to 9 and the maximum number of attempts.
- **Retry with backoff, chaining and batching.** A job lists its own backoff delays; `Chain` runs jobs one after another, and `BatchAsync` groups jobs and counts how many succeeded and failed.
- **A background worker.** `AddQueueWorker` runs the jobs; failed jobs land in a failed-job store that `FailedJobManager` lets you inspect.
- **The driver is a configuration value.** Memory, file, Redis, database (any EF Core provider), RabbitMQ or Kafka: the store's `Driver` picks the backend. Kafka is the one driver without delayed delivery.
- **At-least-once delivery, stated plainly.** A job can run more than once after a crash, so write jobs that can safely run twice.

## Quick start

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

The store above uses the `memory` driver, so it needs `Naravel.Queue.Memory` and its `AddMemoryDriver(builder.Configuration)` call; every other driver is chosen the same way, by the `Driver` of the store. `DispatchAsync` returns the message id, or `null` when the job is an `IUniqueJob` and an identical job is already waiting or running; unique jobs need `AddQueueCoordination()` from `Naravel.Queue.Coordination` and are covered in the [coordination guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue-coordination.md#unique-jobs-iuniquejob). The worker takes jobs from the `default` queue, one at a time, unless `AddQueueWorker` is given other `Queues` or a `Concurrency`.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue.Memory`](https://www.nuget.org/packages/Naravel.Queue.Memory) | In-process driver with no dependencies, for tests and local development |
| [`Naravel.Queue.File`](https://www.nuget.org/packages/Naravel.Queue.File) | JSON files on disk: durable, no external service |
| [`Naravel.Queue.Redis`](https://www.nuget.org/packages/Naravel.Queue.Redis) | Redis driver on StackExchange.Redis: priority buckets, delayed jobs, recovery of jobs a crashed worker left behind |
| [`Naravel.Queue.Database`](https://www.nuget.org/packages/Naravel.Queue.Database) | Any EF Core provider: SQL Server, PostgreSQL, SQLite, MySQL |
| [`Naravel.Queue.RabbitMQ`](https://www.nuget.org/packages/Naravel.Queue.RabbitMQ) | RabbitMQ driver; delayed jobs by per-message TTL and dead-lettering, no plugin required |
| [`Naravel.Queue.Kafka`](https://www.nuget.org/packages/Naravel.Queue.Kafka) | Kafka driver on Confluent.Kafka; it does not support delayed delivery |
| [`Naravel.Queue.Coordination`](https://www.nuget.org/packages/Naravel.Queue.Coordination) | Optional job middleware over Naravel.Cache locks: WithoutOverlapping, RateLimited, ThrottlesExceptions |
| [`Naravel.Queue.Encryption`](https://www.nuget.org/packages/Naravel.Queue.Encryption) | Optional: encrypts the payload of jobs marked IShouldBeEncrypted with ASP.NET Core Data Protection |

## Learn more

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
