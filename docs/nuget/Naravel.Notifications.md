# Naravel.Notifications

Typed notification fan-out over explicitly named channels, with a fake for tests. Channels (Mail, Queue) are separate opt-in packages.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Notifications
```

Targets .NET 10.

## Use

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

- [`Naravel.Notifications.Mail`](https://www.nuget.org/packages/Naravel.Notifications.Mail): Optional Naravel.Notifications adapter: a "mail" channel that delivers a notification through Naravel.Mail.
- [`Naravel.Notifications.Queue`](https://www.nuget.org/packages/Naravel.Notifications.Queue): Optional Naravel.Notifications adapter that delivers notifications through Naravel.Queue: only a registered alias and a plain payload are stored.

## Documentation

- Notifications: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/notifications.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
