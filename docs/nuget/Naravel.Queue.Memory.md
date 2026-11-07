# Naravel.Queue.Memory

In-process "memory" queue driver for Naravel.Queue - zero dependencies, ideal for tests and local development.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Memory
```

Targets .NET 10. It depends on `Naravel.Queue`, which NuGet installs with it.

## Use

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

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
