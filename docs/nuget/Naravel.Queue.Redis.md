# Naravel.Queue.Redis

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.Redis.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.Redis)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.Redis.svg)](https://www.nuget.org/packages/Naravel.Queue.Redis)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Redis queue driver for Naravel.Queue, built on StackExchange.Redis - priority buckets, delayed jobs and automatic reclaim of jobs left behind by a crashed worker.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.Redis` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Queue` and `Naravel.Redis`, which NuGet installs with it |
| **Driver** | `redis`, added with `AddRedisDriver` |

## What you get

- **Ten priority buckets.** Each queue keeps one ready list per priority, 0 to 9, and 9 comes out first.
- **Delays and recovery in one pop.** Each pop promotes the delayed jobs that are due and reclaims reserved jobs whose visibility timeout expired.
- **Fenced reservations.** A worker that outlives its visibility timeout cannot acknowledge the copy that another worker now owns; the job simply runs a second time.
- **One connection with the rest of Naravel.** `Connection` names a `Naravel.Redis` store, so the queue and the cache can share one connection.
- **Batches that outlive the process.** `AddRedisBatchRepository()` keeps batch state in Redis; the default tracking is in memory.

## Quick start

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

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries with backoff, chaining, batching and the background worker |
| [`Naravel.Redis`](https://www.nuget.org/packages/Naravel.Redis) | Named, shared Redis connections that the driver can borrow |

## Learn more

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- Redis: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/redis.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
