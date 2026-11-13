# Naravel.Mail.Queue

[![NuGet](https://img.shields.io/nuget/v/Naravel.Mail.Queue.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Mail.Queue)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Mail.Queue.svg)](https://www.nuget.org/packages/Naravel.Mail.Queue)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Optional Naravel.Mail adapter that delivers mail through Naravel.Queue: QueueAsync stores only the built message data.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Mail.Queue` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Mail` and `Naravel.Queue`, which NuGet installs with it |
| **Runs on** | a `Naravel.Queue` driver and a worker that you register |

## What you get

- **Queue instead of send.** `mailer.To(...).QueueAsync(mailable)` builds the message now and leaves the sending to a worker.
- **Plain data in the queue.** Only the addresses, the subject, the text and HTML bodies and the transport name are stored, never the mailable or a type name.
- **The transport is fixed when you queue.** `manager.Mailer("smtp").To(a).QueueAsync(...)` stores `smtp`, and the worker uses it even if `Mail:Default` changes.
- **Placement in one place.** `options.Connection` and `options.Queue` choose where the job goes; the defaults are the queue module's default connection and `default`.
- **Retries from the queue.** A failed send is retried with the job's `MaxAttempts` and backoff.

## Quick start

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);   // AddRedisDriver comes from Naravel.Queue.Redis; any other queue driver package works the same
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelMailQueue();

await mailer.To(user.Email).QueueAsync(new WelcomeMail(user), ct);
```

`WelcomeMail` is your own `Mailable`, `user` is your own object and `mailer` is an injected `IMailer`. Delivery is at least once, so a worker that stops after the transport accepted the message can send it twice, and attachments are not part of the queued data yet. `AddNaravelMailQueue` registers neither a queue nor a worker: add `AddQueueWorker` in the process that should send. Without this package, `QueueAsync` throws an exception that says what to install.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Mail`](https://www.nuget.org/packages/Naravel.Mail) | Mailables, named transports and the fake that this adapter queues |
| [`Naravel.Mail.Smtp`](https://www.nuget.org/packages/Naravel.Mail.Smtp) | The SMTP transport that a worker can send through |
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries and the background worker that send the message |

## Learn more

- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
