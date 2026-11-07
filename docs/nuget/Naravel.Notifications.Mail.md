# Naravel.Notifications.Mail

Optional Naravel.Notifications adapter: a "mail" channel that delivers a notification through Naravel.Mail.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Notifications.Mail
```

Targets .NET 10. It depends on `Naravel.Notifications` and `Naravel.Mail`, which NuGet installs with it.

## Use

```csharp
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddMailNotificationChannel<InvoiceReady>(
    address: c => c.GetRecipient<User>().Email,
    mailable: n => new InvoiceMail(n));

await notifications.SendAsync(user, new InvoiceReady(7), ["mail", "log"], ct);
```

One delegate reads the address from the recipient, the other builds the `Mailable`; the message goes through the configured mailer. `User` and `InvoiceMail` are your own types.

## Documentation

- Notifications: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/notifications.md)
- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
