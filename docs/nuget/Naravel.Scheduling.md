# Naravel.Scheduling

Naravel task scheduling: a fluent, named schedule run by a hosted service, with overlap and one-server guards over Naravel.Cache locks.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Scheduling
```

Targets .NET 10. It depends on `Naravel.Cache`, which NuGet installs with it.

## Use

```csharp
builder.Services.AddNaravelCache(builder.Configuration);   // from Naravel.Cache: the lock driver for WithoutOverlapping and OnOneServer

builder.Services.AddNaravelScheduling(schedule =>
{
    // Task names are yours: ASCII letters, digits and . _ : - , at most 100 characters.
    schedule.Call("prune-sessions", (sp, ct) => sp.GetRequiredService<SessionPruner>().RunAsync(ct))   // SessionPruner is your own service
        .EveryFiveMinutes()
        .WithoutOverlapping();

    schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct))   // so is Digest
        .DailyAt(8, 0)
        .OnOneServer();
});
```

A hosted service wakes once a minute and starts what is due; each run gets its own DI scope. Times are UTC unless a task calls `InTimeZone(zone)`. If the host was stopped, the missed minutes are skipped, not replayed; there is no catch-up in this version.

`WithoutOverlapping()` skips a run while the previous one still holds its lock; the lock expires after one hour (`SchedulingOptions.OverlapTtl`), so a crash cannot block the task for good. `OnOneServer()` needs a lock driver that all instances share (Redis or Memcached): with the Memory cache driver every instance would run the task, so the host refuses to start.

## Documentation

- Scheduling: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/scheduling.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
