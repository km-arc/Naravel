# Naravel.Mail

[![NuGet](https://img.shields.io/nuget/v/Naravel.Mail.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Mail)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Mail.svg)](https://www.nuget.org/packages/Naravel.Mail)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Typed mail contract for Naravel: Mailable classes, named transports (log, array, SMTP via an optional package) and a test fake, built on Naravel.Foundation.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Mail` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Foundation`, which NuGet installs with it |
| **Transports** | `log` (the default) and `array` are built in; add [`Naravel.Mail.Smtp`](https://www.nuget.org/packages/Naravel.Mail.Smtp) to deliver for real and [`Naravel.Mail.Queue`](https://www.nuget.org/packages/Naravel.Mail.Queue) to send through a queue |

## What you get

- **A message is a class.** A `Mailable` builds its `MailMessage` with `MailBuilder`, and `mailer.To(email).SendAsync(new WelcomeMail(name))` sends it.
- **Named transports.** `Mail:Default` picks the one `IMailer` uses; the default, `log`, logs the message and delivers nothing.
- **No network dependency in the core.** SMTP is the optional `Naravel.Mail.Smtp` package and queued sending is `Naravel.Mail.Queue`.
- **Prepared messages too.** `IMailer.SendAsync(MailMessage)` sends an explicit message without a mailable.
- **A test fake.** `MailFake` records the sent and queued messages and offers `AssertSent`, `AssertSentCount` and `AssertQueued`.

## Quick start

```json
{ "Mail": { "From": "app@example.com" } }
```

```csharp
using Naravel.Mail;

builder.Services.AddNaravelMail(builder.Configuration);   // Mail:Default is "log": the message is logged, nothing is delivered

app.MapPost("/welcome", async (string email, string name, IMailer mailer, CancellationToken ct) =>
{
    await mailer.To(email).SendAsync(new WelcomeMail(name), ct);
    return Results.Accepted();
});

public sealed class WelcomeMail(string name) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();
}
```

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Mail.Smtp`](https://www.nuget.org/packages/Naravel.Mail.Smtp) | Optional SMTP transport for Naravel.Mail, built on MailKit. |
| [`Naravel.Mail.Queue`](https://www.nuget.org/packages/Naravel.Mail.Queue) | Optional Naravel.Mail adapter that delivers mail through Naravel.Queue: QueueAsync stores only the built message data. |

## Learn more

- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
