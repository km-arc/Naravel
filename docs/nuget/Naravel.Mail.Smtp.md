# Naravel.Mail.Smtp

[![NuGet](https://img.shields.io/nuget/v/Naravel.Mail.Smtp.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Mail.Smtp)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Mail.Smtp.svg)](https://www.nuget.org/packages/Naravel.Mail.Smtp)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Optional SMTP transport for Naravel.Mail, built on MailKit.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Mail.Smtp` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Mail`, which NuGet installs with it |
| **Driver** | `smtp`, added with `AddNaravelMailSmtp` |
| **Security** | `StartTls` (the default, port 587), `SslOnConnect` (port 465) or `None` (clear text, for local test servers only) |

## What you get

- **TLS unless you say otherwise.** `Security` defaults to `StartTls`, and clear text needs an explicit `"Security": "None"`.
- **Authentication only when asked.** Without a `Username` no authentication is attempted.
- **A connection per send.** Each send connects, authenticates, sends and disconnects, so concurrent sends are safe.
- **Safe headers.** `Bcc` recipients are used for delivery only and never appear in headers, and a subject with line breaks cannot inject a header.
- **A bad setting fails with its name.** The first send reports which key is wrong, and the application still starts.

## Quick start

```csharp
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelMailSmtp(builder.Configuration);   // after AddNaravelMail
```

```json
{
  "Mail": {
    "Default": "relay",
    "From": "app@example.com",
    "Mailers": {
      "relay": { "Driver": "smtp", "Host": "smtp.example.com", "Port": 587, "Security": "StartTls", "Username": "app", "Timeout": 30 }
    }
  }
}
```

`Timeout` is in seconds. Keep the password out of the file: set it in user secrets or as `Mail__Mailers__relay__Password`. A server rejection or a network failure reaches the caller as the MailKit exception, and nothing is retried inside the transport.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Mail`](https://www.nuget.org/packages/Naravel.Mail) | Mailables, named transports and the fake that this transport delivers for |
| [`Naravel.Mail.Queue`](https://www.nuget.org/packages/Naravel.Mail.Queue) | Queues the message, and a worker sends it through the transport you named |

## Learn more

- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
