# Naravel.Queue.Encryption

Optional Naravel.Queue adapter that encrypts the payload of jobs marked IShouldBeEncrypted with ASP.NET Core Data Protection. Unmarked jobs keep the existing wire format.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Encryption
```

Targets .NET 10. It depends on `Naravel.Encryption` and `Naravel.Queue`, which NuGet installs with it.

## Use

```csharp
using Naravel.Queue.Encryption;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;

builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);   // first: the encryption wraps the job serializer that AddQueue registers
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

Only classes that implement the empty marker `IShouldBeEncrypted` are encrypted; other jobs keep the existing wire format. A marked job is never sent or run as plaintext. If a producer and its worker do not share the key ring and the application name, the worker cannot decrypt and every marked job fails with `DecryptException`. A job that was queued as plaintext before its class was marked is refused unless you set `AddNaravelQueueEncryption(o => o.AcceptPlaintextForMarkedJobs = true)` for a deploy window. `AddNaravelQueueEncryption` throws if `AddQueue` has not been called before it.

## Documentation

- Encryption: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/encryption.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
