# Naravel.Queue.RabbitMQ

RabbitMQ queue driver for Naravel.Queue - delayed jobs via per-message TTL + dead-lettering, no plugin required.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.RabbitMQ
```

Targets .NET 10. It depends on `Naravel.Queue`, which NuGet installs with it.

## Use

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

The store reads `HostName` (default `localhost`), `Port` (default `5672`), `UserName`, `Password` and `VirtualHost`; keep the password out of the file, for example in user secrets or the variable `NaravelQueue__Stores__rabbit__Password`. `QueueType` is `Classic` (the default) or `Quorum`, and `DeliveryLimit` bounds how often the broker redelivers a message and needs `Quorum`. A quorum queue ignores message priorities, and RabbitMQ rejects a queue that is declared again with another type, so drain and delete an existing classic queue before you switch.

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
