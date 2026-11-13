# Naravel.Events.Queue

[![NuGet](https://img.shields.io/nuget/v/Naravel.Events.Queue.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Events.Queue)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Events.Queue.svg)](https://www.nuget.org/packages/Naravel.Events.Queue)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Optional Naravel.Events adapter for running selected event listeners through Naravel.Queue.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Events.Queue` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Events` and `Naravel.Queue`, which NuGet installs with it |
| **Runs on** | a `Naravel.Queue` driver and a worker that you register |

## What you get

- **Opt in per listener.** `AddQueuedEventListener<OrderPaid, SendReceipt>(...)` moves just that listener to a queue worker.
- **A stable alias, not a type name.** The worker finds the listener by the alias you register, and no CLR type is read from queued data.
- **Placement per listener.** `o => o.OnConnection("redis").OnQueue("mail").WithMaxAttempts(5)` picks the connection, the queue and the attempts.
- **Trimming and Native AOT.** Pass source-generated JSON metadata, such as `MyJsonContext.Default.OrderPaid`, instead of reflection-based serialization.

## Quick start

```csharp
builder.Services.AddNaravelEventsQueue();
builder.Services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
```

The listener runs later on a queue worker, at least once, so make it idempotent. By default the job goes to the default queue connection and the `default` queue, so run a worker that consumes them.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Events`](https://www.nuget.org/packages/Naravel.Events) | The event dispatcher whose listeners this adapter can queue |
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries and the background worker that run the queued listener |

## Learn more

- Events: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/events.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
