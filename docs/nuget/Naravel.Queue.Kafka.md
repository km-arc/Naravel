# Naravel.Queue.Kafka

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.Kafka.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.Kafka)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.Kafka.svg)](https://www.nuget.org/packages/Naravel.Queue.Kafka)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Kafka queue driver for Naravel.Queue, built on Confluent.Kafka. Kafka is a log rather than a queue, so its semantics are adapted: it does not delay jobs.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.Kafka` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Queue`, which NuGet installs with it |
| **Driver** | `kafka`, added with `AddKafkaDriver` |
| **Delayed jobs** | Not supported: read the note under the example before you choose Kafka for jobs that wait |

## What you get

- **A topic per queue.** Queue `q` is the topic `netqueue-q`, and jobs that ran out of attempts go to `netqueue-q-failed`.
- **A consumer group per queue.** An idle consumer of one queue cannot stall another queue.
- **Offsets commit after the outcome.** Nothing is committed until the job is acknowledged, released or failed.
- **An idle worker holds no thread.** Polling awaits a short delay, so a stopped worker reacts to cancellation within about 20 ms.

## Quick start

```json
{
  "NaravelQueue": {
    "Default": "kafka",
    "Stores": {
      "kafka": { "Driver": "kafka", "BootstrapServers": "localhost:9092", "GroupId": "netqueue-workers" }
    }
  }
}
```

```csharp
using Naravel.Queue.Extensions;
using Naravel.Queue.Kafka;

builder.Services.AddQueue(builder.Configuration).AddKafkaDriver(builder.Configuration);
builder.Services.AddQueueWorker();
```

`BootstrapServers` is required; `GroupId` defaults to `netqueue-workers`, and each queue uses its own consumer group named `<GroupId>-<queue>`. **Kafka does not delay jobs**: a dispatch with `DelayFor` or `RunAt` is rejected with an error, and a failed job is retried at once instead of after its backoff. Use the Redis, Database or RabbitMQ driver for jobs that wait. A retried job is produced again at the tail of the topic, so order is not preserved.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries with backoff, chaining, batching and the background worker |
| [`Naravel.Queue.Redis`](https://www.nuget.org/packages/Naravel.Queue.Redis) | Redis driver: priority buckets and delayed jobs |
| [`Naravel.Queue.Database`](https://www.nuget.org/packages/Naravel.Queue.Database) | Any EF Core provider: delayed jobs and priorities |
| [`Naravel.Queue.RabbitMQ`](https://www.nuget.org/packages/Naravel.Queue.RabbitMQ) | RabbitMQ driver: delayed jobs by per-message TTL and dead-lettering |

## Learn more

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
