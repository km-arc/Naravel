# Naravel.Mail.Queue

Optional Naravel.Mail adapter that delivers mail through Naravel.Queue: QueueAsync stores only the built message data.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Mail.Queue
```

Targets .NET 10. It depends on `Naravel.Mail` and `Naravel.Queue`, which NuGet installs with it.

## Use

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);   // any queue driver
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelMailQueue();

await mailer.To(user.Email).QueueAsync(new WelcomeMail(user), ct);
```

`QueueAsync` builds the mailable and queues the message data; the mailable object itself is never stored. `AddNaravelMailQueue` registers neither a queue nor a worker: add `AddQueueWorker` in the process that should send. Without this package, `QueueAsync` throws an exception that says what to install.

## Documentation

- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
