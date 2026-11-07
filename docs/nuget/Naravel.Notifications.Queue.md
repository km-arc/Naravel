# Naravel.Notifications.Queue

Optional Naravel.Notifications adapter that delivers notifications through Naravel.Queue: only a registered alias and a plain payload are stored.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Notifications.Queue
```

Targets .NET 10. It depends on `Naravel.Notifications` and `Naravel.Queue`, which NuGet installs with it.

## Use

```csharp
builder.Services.AddNaravelNotificationQueue();   // plus AddQueue, a queue driver and, in worker processes, AddQueueWorker
builder.Services.AddQueuedNotification<InvoicePayload>("invoice.ready", async (payload, scope, ct) =>
{
    var user = await scope.GetRequiredService<IUserRepository>().FindAsync(payload.UserId, ct);   // fresh data
    await scope.GetRequiredService<INotificationSender>().SendAsync(user, new InvoiceReady(payload.InvoiceId), ["mail"], ct);
});

await notificationQueue.QueueAsync("invoice.ready", new InvoicePayload(user.Id, 7), ct);   // INotificationQueue
```

`InvoicePayload`, `InvoiceReady`, `IUserRepository` and `user` are your own types and objects, and `notificationQueue` is an injected `INotificationQueue`. The queue stores the alias and the payload as JSON, nothing else: no type name, no notification object, no recipient. Never rename an alias while jobs that use it are queued.

## Documentation

- Notifications: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/notifications.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
