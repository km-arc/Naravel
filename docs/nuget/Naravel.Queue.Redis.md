# Naravel.Queue.Redis

Redis queue driver for Naravel.Queue, built on StackExchange.Redis - priority buckets, delayed jobs and automatic reclaim of jobs left behind by a crashed worker.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Redis
```

Targets .NET 10. It depends on `Naravel.Queue` and `Naravel.Redis`, which NuGet installs with it.

## Use

```json
{
  "NaravelQueue": {
    "Default": "redis",
    "Stores": {
      "redis": { "Driver": "redis", "ConnectionString": "localhost:6379", "VisibilityTimeoutSeconds": "300" }
    }
  }
}
```

```csharp
using Naravel.Queue.Extensions;
using Naravel.Queue.Redis;

builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration);   // also registers Naravel.Redis
builder.Services.AddQueueWorker();
```

A store needs a `ConnectionString`, or a `Connection` that names a Naravel.Redis store whose connection the driver then reuses. `VisibilityTimeoutSeconds` (default 300) is how long a job may stay reserved by a worker that has disappeared before it goes back to the queue, so set it above your longest job. Failed jobs are kept in memory unless you add `builder.Services.AddRedisFailedJobStore()`, which keeps them in a Redis hash (`netqueue:failed-jobs` unless you pass another key).

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- Redis: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/redis.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
