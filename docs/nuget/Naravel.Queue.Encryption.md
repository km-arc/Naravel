# Naravel.Queue.Encryption

Optional Naravel.Queue adapter that encrypts the payload of jobs marked IShouldBeEncrypted with ASP.NET Core Data Protection. Unmarked jobs keep the existing wire format.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Encryption
```

Targets .NET 10. It depends on `Naravel.Encryption` and `Naravel.Queue`, which NuGet installs with it.

## Documentation

- Encryption: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/encryption.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/encryption.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
