# Naravel.Queue.File

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.File.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.File)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.File.svg)](https://www.nuget.org/packages/Naravel.Queue.File)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

File-based queue driver for Naravel.Queue - durable, zero external services required, JSON files on disk.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.File` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Queue`, which NuGet installs with it |
| **Driver** | `file`, added with `AddFileDriver` |

## What you get

- **Durable with no service to run.** Jobs are JSON files that survive a restart and can be inspected by opening the folder; a good fit for one machine or a small app.
- **One folder per state.** Each queue has `pending`, `reserved` and `failed` directories under the `Path`.
- **Two workers cannot take one job.** A reservation moves the file from `pending` to `reserved`, which is effectively atomic on a single volume.
- **Priorities and delays.** File names sort higher priority first, and a job that is not due yet is skipped.
- **Fenced reservations.** A worker that outlives its visibility timeout cannot acknowledge the copy that another worker now owns; the job simply runs a second time.

## Quick start

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

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries with backoff, chaining, batching and the background worker |

## Learn more

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
