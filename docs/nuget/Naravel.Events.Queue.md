# Naravel.Events.Queue

Optional Naravel.Events adapter for running selected event listeners through Naravel.Queue.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Events.Queue
```

Targets .NET 10. It depends on `Naravel.Events` and `Naravel.Queue`, which NuGet installs with it.

## Use

```csharp
builder.Services.AddNaravelEventsQueue();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

The listener runs later on a queue worker. The queue stores the explicit alias, never a type name. Register `Naravel.Queue` with a driver and run a worker: by default the job goes to the default queue connection and the `default` queue.

## Documentation

- Events: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/events.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
