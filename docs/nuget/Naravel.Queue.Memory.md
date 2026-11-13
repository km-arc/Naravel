# Naravel.Queue.Memory

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.Memory.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.Memory)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.Memory.svg)](https://www.nuget.org/packages/Naravel.Queue.Memory)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

In-process "memory" queue driver for Naravel.Queue - zero dependencies, ideal for tests and local development.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.Memory` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Queue`, which NuGet installs with it |
| **Driver** | `memory`, added with `AddMemoryDriver` |

## What you get

- **Asynchronous, not inline.** Dispatch queues the job and the worker runs it, unlike Laravel's `sync` driver.
- **Priorities and delays.** Jobs come out by priority, and a job that is not due yet waits.
- **Zero dependencies.** It adds nothing to `Naravel.Queue`.
- **State per store name.** Each store name keeps its own jobs, so two stores in one process never mix them.

## Quick start

```json
{
  "NaravelQueue": {
    "Default": "local",
    "Stores": { "local": { "Driver": "memory" } }
  }
}
```

```csharp
using Naravel.Queue.Extensions;
using Naravel.Queue.Memory;

builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);
builder.Services.AddQueueWorker();
```

The `memory` store has no settings. Jobs live in the process: they are gone when it stops, and a second process cannot see them. The jobs are kept per store name for the whole process, so two hosts in one process that use the same store name share them, which matters in tests. For jobs that must survive a restart or run on another machine, use another driver package; the job classes stay the same.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries with backoff, chaining, batching and the background worker |
| [`Naravel.Queue.File`](https://www.nuget.org/packages/Naravel.Queue.File) | JSON files on disk: durable, no external service |
| [`Naravel.Queue.Redis`](https://www.nuget.org/packages/Naravel.Queue.Redis) | Redis driver for jobs that must run on another machine |

## Learn more

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
