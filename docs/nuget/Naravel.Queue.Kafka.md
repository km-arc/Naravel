# Naravel.Queue.Kafka

Kafka queue driver for Naravel.Queue, built on Confluent.Kafka - semantics adapted for a log-based broker (see class docs for details).

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Kafka
```

Targets .NET 10. It depends on `Naravel.Queue`, which NuGet installs with it.

## Use

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

`BootstrapServers` is required; `GroupId` defaults to `netqueue-workers`, and each queue uses its own consumer group named `<GroupId>-<queue>`. **Kafka does not delay jobs**: a dispatch with `DelayFor` or `RunAt` is rejected with an error, and a failed job is retried at once instead of after its backoff. Use the Redis, Database or RabbitMQ driver for jobs that wait.

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
