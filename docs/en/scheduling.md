# Scheduling

`Naravel.Scheduling` runs named tasks on a schedule inside your host. A task is a delegate; the schedule says when it is due; a hosted service wakes once a minute and starts what is due. Two guards work across several running instances: `WithoutOverlapping` (do not start a run while the previous one is still going) and `OnOneServer` (run a due minute on one instance only). Both use the cache locks of `Naravel.Cache`.

Decision and reasons: [PDR-017](../pdr/en/PDR-017-console-and-scheduling.md). Persian version: [docs/fa/scheduling.md](../fa/scheduling.md).

## Quickstart

```csharp
builder.Services.AddNaravelCache(builder.Configuration);   // the lock driver for the guards (Redis or Memcached when you run several instances)
builder.Services.AddNaravelScheduling(schedule =>
{
    schedule.Call("prune-sessions", (sp, ct) => sp.GetRequiredService<SessionPruner>().RunAsync(ct)).EveryFiveMinutes().WithoutOverlapping();
    schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct)).DailyAt(8, 0).OnOneServer();
});
```

That is all: there is no cron line to add and no command to run. The hosted service starts and stops with your host (`dotnet run`, a container, a Windows service). Each run gets a **new DI scope**: the handler receives a scoped `IServiceProvider` and the host's `CancellationToken`.

## Using it

### Names

Every task needs a unique name of ASCII letters, digits and `. _ : -` (at most 100 characters). The name is the lock key and the metric tag, so it must stay the same across deploys. Laravel derives its mutex name from the command text, which silently changes when the code changes; an explicit name does not.

### Frequencies

A task with no frequency runs every minute, as in Laravel. Calling another frequency method replaces the earlier one.

| Laravel | Naravel | Cron |
|---|---|---|
| `everyMinute` | `EveryMinute()` | `* * * * *` |
| `everyFiveMinutes` | `EveryFiveMinutes()` | every 5 minutes |
| `everyTenMinutes`, `everyFifteenMinutes`, `everyThirtyMinutes` | `EveryTenMinutes()`, `EveryFifteenMinutes()`, `EveryThirtyMinutes()` | every 10, 15, 30 minutes |
| (none) | `Every(TimeSpan)` | a whole number of minutes, at least one |
| `hourly`, `hourlyAt(15)` | `Hourly()`, `HourlyAt(15)` | `0 * * * *`, `15 * * * *` |
| `daily`, `dailyAt('08:00')` | `Daily()`, `DailyAt(8, 0)` | `0 0 * * *`, `0 8 * * *` |
| `weekly`, `weeklyOn(1, '09:30')` | `Weekly()`, `WeeklyOn(DayOfWeek.Monday, 9, 30)` | Sunday 00:00, `30 9 * * 1` |
| `monthly` | `Monthly()` | `0 0 1 * *` |
| `cron('*/20 9-17 * * 1-5')` | `Cron("*/20 9-17 * * 1-5")` | any five-field expression |

The "every N minutes" forms count from the Unix epoch, so every instance agrees on the same minutes without talking to each other. `Cron` takes five fields (minute, hour, day of month, month, day of week); seconds are not supported, and a bad expression throws `ArgumentException` when the task is added, not at 3 a.m. Parsing is done by the [Cronos](https://github.com/HangfireIO/Cronos) library.

### Filters

```csharp
schedule.Call("report", Report).DailyAt(9, 0).Weekdays().InTimeZone(tehran);
schedule.Call("poll", Poll).EveryFiveMinutes().Between(new TimeOnly(8, 0), new TimeOnly(18, 0)).When(() => featureOn);
```

- `InTimeZone(TimeZoneInfo)` reads the frequency, `Weekdays` and `Between` in that zone (default UTC, **not** the machine's local zone, so every instance behaves the same). Daylight-saving changes follow the zone.
- `Weekdays()` runs Monday to Friday. `Between(start, end)` includes both ends; a start later than the end crosses midnight.
- `When(predicate)` must be true and `Skip(predicate)` must be false. Several calls all apply. The predicates are asked at the start of the minute, so keep them cheap; a predicate that throws skips that task for that minute and is logged.

### Overlap

`WithoutOverlapping()` takes the lock `schedule:overlap:<name>` before running and releases it in a `finally`, also after a failure or cancellation. If the lock is held, the run is skipped (not queued). The lock expires after one hour (`SchedulingOptions.OverlapTtl`) so a crash cannot block the task for ever; give a long job its own value with `WithoutOverlapping(TimeSpan.FromHours(6))`. Laravel's default is 24 hours, which is too long for a task that runs every five minutes.

### One server

`OnOneServer()` takes the lock `schedule:slot:<name>:<minute>` and never releases it, so exactly one instance runs the task for each due minute (Laravel does the same). The lock lasts `SchedulingOptions.SlotLockTtl` (five minutes) so it outlives the minute and small clock differences.

It needs a lock driver that all instances share (Redis or Memcached). The memory driver is process-local, so every instance would run the task; the host therefore **refuses to start** with a message that names the task and the driver. `ICacheLock.IsShared` is `true` for every driver except `MemoryLock`.

### Failures and hooks

An exception in one task is logged, counted and passed to `OnFailure(exception => ...)`; it never stops the loop or other tasks. `OnSuccess(() => ...)` runs after a run that did not throw. An exception inside a hook is logged and ignored. Cancellation when the host stops is not a failure: the run ends with the outcome `cancelled`.

When the host stops, it cancels the token given to running tasks and waits for them until the host's shutdown timeout. A task that ignores the token delays shutdown up to that timeout.

### Missed minutes

If the host was stopped or paused, the missed minutes are **skipped, not replayed**, as with cron. The loop uses the minute the clock shows when it wakes, so a late wake-up never runs a minute twice. There is no catch-up in this version. A task that must not miss a run should be made idempotent and use a short frequency, or be queued through `Naravel.Queue`.

### The task list

`Scheduler.List()` returns the name, the frequency text and the next due minute of every task (Laravel: `schedule:list`). Filters are not evaluated. It is a method, so any command, endpoint or health page can print it.

## Settings

```csharp
builder.Services.AddNaravelScheduling(configure, options =>
{
    options.OverlapTtl = TimeSpan.FromHours(2);
    options.SlotLockTtl = TimeSpan.FromMinutes(10);
});
```

## Observability

- Meter `Naravel.Scheduling`: counter `naravel.scheduling.task.runs` with tags `task` and `outcome`, and histogram `naravel.scheduling.task.duration` (ms).
- `outcome` is one of `succeeded`, `failed`, `cancelled`, `skipped_overlap`, `skipped_other_server`. A task that is not due, or is filtered out, is not counted: that would add one measurement per task per minute for nothing.
- `ActivitySource` `Naravel.Scheduling`: one activity per run with the same tags.
- Tags never carry exception messages, task data or lock tokens. Task names come from your code, so they are bounded.
- Logs name the task and carry the exception; they never contain lock tokens.

## Testing

`SchedulerFake` runs a schedule at times you choose: no host, no timer, no waiting.

```csharp
var fake = new SchedulerFake(schedule => schedule.Call("daily-digest", (sp, ct) => Task.CompletedTask).DailyAt(8, 0).Weekdays());

fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero)).Should().BeTrue();    // Monday
fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero)).Should().BeFalse();   // Saturday

await fake.RunAtAsync(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
fake.AssertRan("daily-digest");
```

`Outcomes` lists every run with its outcome. To test the guards, build two fakes over one lock (`new SchedulerFake(configure, sharedLock)`); with no lock a fake uses a private in-memory lock that reports itself as shared. Task handlers under the fake get an empty service provider, so a task that needs services is better tested through a real container: resolve `Scheduler` and call `RunDueAsync(time)`.

## Not ported, and why

- **`command`, `exec`, `job`:** process and Artisan-command tasks are not part of this version; Commands are decided as native `System.CommandLine` (see PDR-017), and queueing a job is one line inside a task through the queue's dispatcher.
- **Ping URLs, `runInBackground`, maintenance-mode awareness, last-run persistence, a dashboard:** listed in the backlog. Tasks already run concurrently in the host process.
- **`before` and `after` hooks:** use the body of the task; `OnSuccess` and `OnFailure` cover the two outcomes.
- **Catch-up of missed minutes:** see above.

## Alternatives

Hangfire and Quartz.NET add a job store, retries and a dashboard or clustering; use them when you need persistence of schedules or per-run history. Platform schedulers (cron, a Kubernetes `CronJob`, systemd timers) are the right choice when every run should be a separate process. This module is for the common case Laravel's `Schedule` covers: a handful of in-process tasks with simple guards.
