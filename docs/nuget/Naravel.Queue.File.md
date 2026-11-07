# Naravel.Queue.File

File-based queue driver for Naravel.Queue - durable, zero external services required, JSON files on disk.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.File
```

Targets .NET 10. It depends on `Naravel.Queue`, which NuGet installs with it.

## Use

```json
{
  "NaravelQueue": {
    "Default": "disk",
    "Stores": {
      "disk": { "Driver": "file", "Path": "storage/queue", "VisibilityTimeoutSeconds": "300" }
    }
  }
}
```

```csharp
using Naravel.Queue.Extensions;
using Naravel.Queue.File;

builder.Services.AddQueue(builder.Configuration).AddFileDriver(builder.Configuration);
builder.Services.AddQueueWorker();
```

`Path` is the directory that holds the store's JSON files; it defaults to `storage/netqueue/<store name>`. `VisibilityTimeoutSeconds` (default 300) is how long a job may stay reserved by a worker that has disappeared before it goes back to the queue, so set it above your longest job. Failed jobs are kept in memory unless you register a persistent failed-job store.

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
