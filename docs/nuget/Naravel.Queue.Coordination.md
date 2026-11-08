# Naravel.Queue.Coordination

Optional Naravel.Queue job middleware that coordinates workers through Naravel.Cache locks and rate limits: WithoutOverlapping, RateLimited and ThrottlesExceptions.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Coordination
```

Targets .NET 10. It depends on `Naravel.Queue` and `Naravel.Cache`, which NuGet installs with it.

## Use

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

`WithoutOverlapping(key)` runs one job per key at a time, `RateLimited(limiter, maxAttempts, window)` allows at most that many jobs per window, and `ThrottlesExceptions(maxExceptions, window, backoff)` stops running a kind of job for a while after repeated exceptions. A job that is held back is released to its queue without using up an attempt. The locks and counters live in Naravel.Cache, so workers in several processes need a shared cache store such as Redis. The rate limiter is a fixed window, so around a window boundary it can let up to twice the limit through.

## Documentation

- Queue coordination: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue-coordination.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
