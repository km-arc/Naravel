# Naravel.Mail

Typed mail contract for Naravel: Mailable classes, named transports (log, array, SMTP via an optional package) and a test fake, built on Naravel.Foundation.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Mail
```

Targets .NET 10. It depends on `Naravel.Foundation`, which NuGet installs with it.

## Related packages

- [`Naravel.Mail.Smtp`](https://www.nuget.org/packages/Naravel.Mail.Smtp): Optional SMTP transport for Naravel.Mail, built on MailKit.
- [`Naravel.Mail.Queue`](https://www.nuget.org/packages/Naravel.Mail.Queue): Optional Naravel.Mail adapter that delivers mail through Naravel.Queue: QueueAsync stores only the built message data.

## Documentation

- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
