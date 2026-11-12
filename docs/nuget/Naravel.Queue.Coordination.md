# Naravel.Queue.Coordination

[![NuGet](https://img.shields.io/nuget/v/Naravel.Queue.Coordination.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Queue.Coordination)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Queue.Coordination.svg)](https://www.nuget.org/packages/Naravel.Queue.Coordination)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Optional Naravel.Queue job middleware that coordinates workers through Naravel.Cache locks and rate limits: WithoutOverlapping, RateLimited and ThrottlesExceptions.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Queue.Coordination` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Queue` and `Naravel.Cache`, which NuGet installs with it |
| **Works with** | a shared cache store such as [`Naravel.Cache.Redis`](https://www.nuget.org/packages/Naravel.Cache.Redis) when workers run in several processes |

## What you get

- **`WithoutOverlapping(key)`** runs one job per key at a time.
- **`RateLimited(limiter, maxAttempts, window)`** allows at most that many jobs per window.
- **`ThrottlesExceptions(maxExceptions, window, backoff)`** stops running a kind of job for a while after repeated exceptions.
- **Unique jobs.** An `IUniqueJob` rejects a duplicate at dispatch.

## Quick start

```csharp
using Naravel.Queue.Coordination;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;

builder.Services.AddNaravelCache(builder.Configuration);   // add AddNaravelRedisCache(...) from Naravel.Cache.Redis when several processes run workers
builder.Services.AddQueueCoordination();

public sealed class SyncInvoice : Job, IHasJobMiddleware
{
    public int InvoiceId { get; set; }

    public IEnumerable<IJobMiddleware> Middleware(JobContext context) => new IJobMiddleware[]
    {
        new RateLimited("billing-api", maxAttempts: 60, window: TimeSpan.FromMinutes(1)),   // before WithoutOverlapping: a job the limiter holds back never took the lock
        new WithoutOverlapping($"invoice:{InvoiceId}")
    };

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;                                                                 // your work goes here
}
```

A job that is held back is released to its queue without using up an attempt. The locks and counters live in Naravel.Cache, so workers in several processes need a shared cache store such as Redis. The rate limiter is a fixed window, so around a window boundary it can let up to twice the limit through.

## Learn more

- Queue coordination: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue-coordination.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
