# Naravel.Mail.Smtp

Optional SMTP transport for Naravel.Mail, built on MailKit.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Mail.Smtp
```

Targets .NET 10. It depends on `Naravel.Mail`, which NuGet installs with it.

## Use

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

`Timeout` is in seconds. Keep the password out of the file: set it in user secrets or as `Mail__Mailers__relay__Password`. A missing `Host` fails the first send with a message that names the key.

## Documentation

- Mail: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/mail.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
