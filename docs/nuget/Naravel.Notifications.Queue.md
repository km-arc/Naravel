# Naravel.Notifications.Queue

[![NuGet](https://img.shields.io/nuget/v/Naravel.Notifications.Queue.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Notifications.Queue)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Notifications.Queue.svg)](https://www.nuget.org/packages/Naravel.Notifications.Queue)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Optional Naravel.Notifications adapter that delivers notifications through Naravel.Queue: only a registered alias and a plain payload are stored.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Notifications.Queue` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Notifications` and `Naravel.Queue`, which NuGet installs with it |
| **Runs on** | a `Naravel.Queue` driver and a worker that you register |

## What you get

- **An alias and a payload, nothing else.** The queue stores the alias and the payload as JSON: no type name, no notification object, no recipient.
- **Fresh data in the worker.** The handler gets the deserialized payload and the job's service scope, so it loads the recipient from your own services and builds the notification itself.
- **Mistakes fail early.** Queueing an unregistered alias, or a payload of another type than the registered one, throws at once, and registering one alias twice is refused.
- **Placement in one place.** `AddNaravelNotificationQueue(options => ...)` sets `Connection` and `Queue`; both default to the queue module's defaults.

## Quick start

```csharp
builder.Services.AddNaravelNotificationQueue();   // plus AddQueue, a queue driver and, in worker processes, AddQueueWorker
builder.Services.AddQueuedNotification<InvoicePayload>("invoice.ready", async (payload, scope, ct) =>
{
    var user = await scope.GetRequiredService<IUserRepository>().FindAsync(payload.UserId, ct);   // fresh data
    await scope.GetRequiredService<INotificationSender>().SendAsync(user, new InvoiceReady(payload.InvoiceId), ["mail"], ct);
});

await notificationQueue.QueueAsync("invoice.ready", new InvoicePayload(user.Id, 7), ct);   // INotificationQueue
```

`InvoicePayload`, `InvoiceReady`, `IUserRepository` and `user` are your own types and objects, and `notificationQueue` is an injected `INotificationQueue`. Never rename an alias while jobs that use it are queued.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Notifications`](https://www.nuget.org/packages/Naravel.Notifications) | One typed notification to a recipient through the channels you name |
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries and the background worker that run the handler |

## Learn more

- Notifications: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/notifications.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
