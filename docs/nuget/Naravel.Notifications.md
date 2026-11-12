# Naravel.Notifications

[![NuGet](https://img.shields.io/nuget/v/Naravel.Notifications.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Notifications)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Notifications.svg)](https://www.nuget.org/packages/Naravel.Notifications)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Typed notification fan-out over explicitly named channels, with a fake for tests. Channels (Mail, Queue) are separate opt-in packages.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Notifications` |
| **Targets** | .NET 10 |
| **Needs** | No other Naravel package |
| **Channels** | mail and queued delivery are separate opt-in packages: [`Naravel.Notifications.Mail`](https://www.nuget.org/packages/Naravel.Notifications.Mail) and [`Naravel.Notifications.Queue`](https://www.nuget.org/packages/Naravel.Notifications.Queue) |

## What you get

- **One typed notification, the channels you name.** `SendAsync(recipient, notification, ["log"], ct)` delivers through the named channels, in the order you name them.
- **A channel is a class or a delegate.** A class implements `INotificationChannel<T>` and is resolved from DI per scope, so it can take dependencies.
- **A built-in log channel.** `AddLogNotificationChannel<T>()` logs the type names only.
- **Plain recipients.** The recipient is any object; it is passed to the channels as is.
- **A test fake.** `NotificationFake` is an `INotificationSender` that delivers nothing, for assertions in tests.

## Quick start

```csharp
using Naravel.Notifications;

builder.Services.AddNaravelNotifications();
builder.Services.AddLogNotificationChannel<InvoiceReady>();   // a channel named "log"

app.MapPost("/invoices/{id:int}/notify", async (int id, INotificationSender notifications, CancellationToken ct) =>
{
    var recipient = new User("Ada");   // any object: it is passed to the channels as is
    await notifications.SendAsync(recipient, new InvoiceReady(id), ["log"], ct);
    return Results.Accepted();
});

public sealed record User(string Name);
public sealed record InvoiceReady(int InvoiceId);
```

A channel is registered per notification type and chosen by name on every call; nothing is inferred from the recipient or from registration order.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Notifications.Mail`](https://www.nuget.org/packages/Naravel.Notifications.Mail) | Optional Naravel.Notifications adapter: a "mail" channel that delivers a notification through Naravel.Mail. |
| [`Naravel.Notifications.Queue`](https://www.nuget.org/packages/Naravel.Notifications.Queue) | Optional Naravel.Notifications adapter that delivers notifications through Naravel.Queue: only a registered alias and a plain payload are stored. |

## Learn more

- Notifications: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/notifications.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
