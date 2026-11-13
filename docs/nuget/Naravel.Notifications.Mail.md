# Naravel.Notifications.Mail

[![NuGet](https://img.shields.io/nuget/v/Naravel.Notifications.Mail.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Notifications.Mail)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Notifications.Mail.svg)](https://www.nuget.org/packages/Naravel.Notifications.Mail)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Optional Naravel.Notifications adapter: a "mail" channel that delivers a notification through Naravel.Mail.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Notifications.Mail` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Notifications` and `Naravel.Mail`, which NuGet installs with it |
| **Channel** | `mail`, or the name you pass as `name:` |

## What you get

- **One of several channels.** `SendAsync(user, notification, ["mail", "log"], ct)` runs the channels in the order you name them.
- **Two plain delegates.** One reads the address from the recipient and the other builds the `Mailable`; they replace Laravel's `routeNotificationForMail()` and `toMail()`.
- **Naravel.Mail's rules apply.** The message goes out with `PendingMail`, so the mailer's default `From` and the address validation apply, and an invalid address fails the channel.
- **Fails loudly.** A `mailable` delegate that returns `null` throws an `InvalidOperationException`, and a transport failure surfaces as the transport's own exception.

## Quick start

```csharp
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddMailNotificationChannel<InvoiceReady>(
    address: c => c.GetRecipient<User>().Email,
    mailable: n => new InvoiceMail(n));

await notifications.SendAsync(user, new InvoiceReady(7), ["mail", "log"], ct);
```

The message goes through the default mailer, or the one you name with `mailer:`. `User` and `InvoiceMail` are your own types.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Notifications`](https://www.nuget.org/packages/Naravel.Notifications) | One typed notification to a recipient through the channels you name |
| [`Naravel.Mail`](https://www.nuget.org/packages/Naravel.Mail) | Mailables, named transports and the fake that this channel sends through |

## Learn more

- Notifications: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/notifications.md)
- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
