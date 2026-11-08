# Naravel.Encryption

A thin Encrypt/Decrypt facade over ASP.NET Core Data Protection. No custom cryptography: keys, rotation and persistence stay in Data Protection.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Encryption
```

Targets .NET 10.

## Use

```csharp
using Naravel.Encryption;

builder.Services.AddNaravelEncryption();   // adds ASP.NET Core Data Protection only if the application has not registered it

app.MapGet("/demo", (IEncrypter encrypter) =>
{
    var payload = encrypter.Encrypt("reset:42");
    return encrypter.TryDecrypt(payload, out var value) ? value : "unreadable";   // "reset:42"
});
```

`Decrypt` throws `DecryptException` for a payload that was changed, was encrypted for another purpose or whose key is no longer in the key ring; `TryDecrypt` returns `false` in those cases instead. `ForPurpose("name")` returns an encrypter whose payloads an encrypter of another purpose cannot read. `Encrypt<T>` and `Decrypt<T>` carry any JSON-serializable value; they use reflection, so a trimmed or Native AOT application passes a `JsonTypeInfo<T>`.

A payload can be read only by an application that uses the same key ring and the same Data Protection application name. With more than one instance, or when old payloads must survive a restart, persist the key ring in a place every instance can read. That is ASP.NET Core Data Protection configuration, not part of this package.

## Documentation

- Encryption: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/encryption.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
