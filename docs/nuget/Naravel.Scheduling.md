# Naravel.Scheduling

[![NuGet](https://img.shields.io/nuget/v/Naravel.Scheduling.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Scheduling)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Scheduling.svg)](https://www.nuget.org/packages/Naravel.Scheduling)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Naravel task scheduling: a fluent, named schedule run by a hosted service, with overlap and one-server guards over Naravel.Cache locks.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Scheduling` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Cache`, which NuGet installs with it |

## What you get

- **A fluent, named schedule.** `schedule.Call("name", handler)` followed by `EveryFiveMinutes()`, `DailyAt(8, 0)`, `WeeklyOn(...)` or `Cron("*/20 9-17 * * 1-5")`.
- **A hosted service runs it.** It wakes once a minute and starts what is due, and each run gets its own DI scope.
- **Guards over cache locks.** `WithoutOverlapping()` and `OnOneServer()` run on Naravel.Cache locks.
- **Time zones.** Times are UTC unless a task calls `InTimeZone(zone)`.

## Quick start

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

If the host was stopped, the missed minutes are skipped, not replayed; there is no catch-up in this version.

`WithoutOverlapping()` skips a run while the previous one still holds its lock; the lock expires after one hour (`SchedulingOptions.OverlapTtl`), so a crash cannot block the task for good. `OnOneServer()` needs a lock driver that all instances share (Redis or Memcached): with the Memory cache driver every instance would run the task, so the host refuses to start.

## Learn more

- Scheduling: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/scheduling.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
