# Naravel.Queue.RabbitMQ

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.RabbitMQ.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.RabbitMQ)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.RabbitMQ.svg)](https://www.nuget.org/packages/Naravel.Queue.RabbitMQ)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

RabbitMQ queue driver for Naravel.Queue - delayed jobs via per-message TTL + dead-lettering, no plugin required.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.RabbitMQ` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Queue`, which NuGet installs with it |
| **Driver** | `rabbitmq`, added with `AddRabbitMqDriver` |
| **Queue types** | `Classic` (the default, with priorities) or `Quorum` (replicated, no priorities) |

## What you get

- **Delayed jobs without a plugin.** A delay is a per-message TTL on a delay queue that dead-letters back to the ready queue.
- **Failed jobs stay on the broker.** Jobs that ran out of attempts go to `netqueue.failed.<queue>`.
- **Manual acknowledgement.** A message is acknowledged on the channel that delivered it, after the job ends.
- **A bounded redelivery.** On a quorum queue, a message that exceeds `DeliveryLimit` is dead-lettered to `netqueue.failed.<queue>` and kept for inspection.

## Quick start

```json
{
  "NaravelQueue": {
    "Default": "rabbit",
    "Stores": {
      "rabbit": { "Driver": "rabbitmq", "HostName": "rabbit.example.com", "UserName": "app" }
    }
  }
}
```

```csharp
using Naravel.Queue.Extensions;
using Naravel.Queue.RabbitMQ;

builder.Services.AddQueue(builder.Configuration).AddRabbitMqDriver(builder.Configuration);
builder.Services.AddQueueWorker();
```

The store reads `HostName` (default `localhost`), `Port` (default `5672`), `UserName`, `Password` and `VirtualHost`; keep the password out of the file, for example in user secrets or the variable `NaravelQueue__Stores__rabbit__Password`. `QueueType` is `Classic` (the default) or `Quorum`, and `DeliveryLimit` bounds how often the broker redelivers a message and needs `Quorum`. A quorum queue ignores message priorities, and RabbitMQ rejects a queue that is declared again with another type, so drain and delete an existing classic queue before you switch. A long-delay message can hold back shorter ones queued after it.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries with backoff, chaining, batching and the background worker |

## Learn more

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
