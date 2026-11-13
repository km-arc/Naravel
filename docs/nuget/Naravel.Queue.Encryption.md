# Naravel.Queue.Encryption

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.Encryption.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.Encryption)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.Encryption.svg)](https://www.nuget.org/packages/Naravel.Queue.Encryption)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Optional Naravel.Queue adapter that encrypts the payload of jobs marked IShouldBeEncrypted with ASP.NET Core Data Protection. Unmarked jobs keep the existing wire format.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.Encryption` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Encryption` and `Naravel.Queue`, which NuGet installs with it, and a queue driver package of your own |
| **Marker** | `IShouldBeEncrypted`, an empty interface on the job class |

## What you get

- **Opt in per job.** Only classes that implement `IShouldBeEncrypted` are stored encrypted; every other job keeps the existing wire format.
- **ASP.NET Core Data Protection does the work.** Naravel adds no cryptography of its own.
- **Never plaintext where it is registered.** A marked job is neither sent nor run as plaintext by an application that registered the package.
- **Undecryptable jobs fail normally.** They take the usual retry and failed-job path, and the error says to check the shared key ring and application name first.

## Quick start

```csharp
using Naravel.Queue.Encryption;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;

// AddMemoryDriver comes from Naravel.Queue.Memory, which this package does not install: use the driver package you already have
// AddQueue goes first: the encryption wraps the job serializer that it registers
builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);
builder.Services.AddDataProtection()                                                      // the same key ring and application name in every producer and worker
    .SetApplicationName("my-shop")
    .PersistKeysToFileSystem(new DirectoryInfo("/shared/keys"));
builder.Services.AddNaravelQueueEncryption();

public sealed class ChargeCard : Job, IShouldBeEncrypted
{
    public string CardNumber { get; set; } = "";

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;                                                               // your work goes here
}
```

Call `AddNaravelQueueEncryption` in every application that dispatches marked jobs and in every worker: a dispatching application without it sends a marked job as plaintext, and a worker without it cannot decrypt one. If a producer and its worker do not share the key ring and the application name, the worker cannot decrypt and every marked job fails with `DecryptException`. A job that was queued as plaintext before its class was marked is refused unless you set `AddNaravelQueueEncryption(o => o.AcceptPlaintextForMarkedJobs = true)` for a deploy window. `AddNaravelQueueEncryption` throws if `AddQueue` has not been called before it.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Encryption`](https://www.nuget.org/packages/Naravel.Encryption) | The `Encrypt` and `Decrypt` facade over Data Protection |
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries and the background worker whose payloads this package encrypts |

## Learn more

- Encryption: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/encryption.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
