# Naravel.Encryption

[![NuGet](https://img.shields.io/nuget/v/Naravel.Encryption.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Encryption)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Encryption.svg)](https://www.nuget.org/packages/Naravel.Encryption)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

A thin Encrypt/Decrypt facade over ASP.NET Core Data Protection. No custom cryptography: keys, rotation and persistence stay in Data Protection.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Encryption` |
| **Targets** | .NET 10 |
| **Needs** | ASP.NET Core Data Protection (`Microsoft.AspNetCore.DataProtection`) and no other Naravel package |

## What you get

- **Strings and typed values.** `Encrypt` returns URL-safe text, and `Encrypt<T>` carries any JSON-serializable value.
- **No custom cryptography.** Every key and every byte of protection belongs to ASP.NET Core Data Protection.
- **Purposes.** `ForPurpose("name")` keeps the payloads of different uses apart.
- **Safe on bad input.** `TryDecrypt` returns `false` where `Decrypt` would throw `DecryptException`.
- **The key ring stays yours.** Persist it where every instance can read it; the notes under the example say why.

## Quick start

```csharp
using Naravel.Encryption;

builder.Services.AddNaravelEncryption();   // adds ASP.NET Core Data Protection only if the application has not registered it

app.MapGet("/demo", (IEncrypter encrypter) =>
{
    var payload = encrypter.Encrypt("reset:42");
    return encrypter.TryDecrypt(payload, out var value) ? value : "unreadable";   // "reset:42"
});
```

`Decrypt` throws `DecryptException` for a payload that was changed, was encrypted for another purpose or whose key is no longer in the key ring; `TryDecrypt` returns `false` in those cases instead. `Encrypt<T>` and `Decrypt<T>` use reflection, so a trimmed or Native AOT application passes a `JsonTypeInfo<T>`.

A payload can be read only by an application that uses the same key ring and the same Data Protection application name. With more than one instance, or when old payloads must survive a restart, persist the key ring in a place every instance can read. That is ASP.NET Core Data Protection configuration, not part of this package.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue.Encryption`](https://www.nuget.org/packages/Naravel.Queue.Encryption) | Optional: stores the payload of selected queue jobs encrypted |

## Learn more

- Encryption: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/encryption.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
