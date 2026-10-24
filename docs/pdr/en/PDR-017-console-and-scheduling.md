# PDR-017 — Console commands and scheduling (`Naravel.Scheduling`, Commands)

**Native-first verdict:** two halves, two answers.

- **Scheduling:** .NET gives the clock (`PeriodicTimer`, `TimeProvider`) and the host (`BackgroundService`), but **not** the part Laravel users actually rely on: a named schedule with frequencies, a cron expression, an overlap guard and a "run on one server only" guard that work across several instances. Writing that by hand is about 150 lines every time, and the two guards are easy to get subtly wrong. **Proposed outcome: accepted, as a thin module over what already exists (`ICacheLock` from R04, the Generic Host).**
- **Commands (Artisan):** `System.CommandLine` plus the Generic Host already give typed arguments, options, help, tab completion and DI. Laravel's value here is the PHP signature string (`mail:send {user} {--queue=}`) and the `make:command` generator, neither of which has a .NET equivalent worth building. **Proposed outcome: `DECLINED`, with a documented recipe.**

Each half is decided separately (R13.T01). Status: **Proposed, awaiting owner approval.**

## Context

Laravel's `Illuminate\Console\Scheduling\Schedule` (used from `routes/console.php` or `Kernel::schedule`) defines tasks in code and runs them from one cron line, `php artisan schedule:run`, called every minute:

- Task kinds: `call(callable)`, `command(...)`, `job(...)`, `exec(...)`.
- Frequencies: `everyMinute`, `everyFiveMinutes`, `hourly`, `dailyAt('13:00')`, `weekly`, `monthly`, `cron('* * * * *')`, with `timezone`, `between`, `weekdays`, `when`, `skip`.
- Guards: `withoutOverlapping($expiresAt)` (a cache lock, 24 hours by default) and `onOneServer()` (an atomic cache lock per task and minute, so several servers sharing a cache run a task once).
- Hooks: `before`, `after`, `onSuccess`, `onFailure`, and ping URLs.
- Tools: `schedule:run`, `schedule:work` (a foreground loop for development), `schedule:list`.

Laravel's `Artisan` offers command classes with a signature string, `argument()`/`option()`, `ask`/`confirm`/`table` helpers, `call`, `make:command`, and many built-in commands (`route:list`, `migrate`, `queue:work` and others).

What .NET already offers:

- **Host and timers:** `BackgroundService` (Generic Host) and `PeriodicTimer` run a loop with graceful shutdown; `TimeProvider` makes the clock injectable and testable (`FakeTimeProvider` in `Microsoft.Extensions.TimeProvider.Testing`). The repository already uses `TimeProvider` (`RateLimiterFake`) and hosts a long-running worker the same way (`QueueWorkerService`).
- **Commands:** `System.CommandLine` (arguments, options, subcommands, help, completion, an invocation pipeline that can use the host's DI) and top-level `dotnet run -- <args>` programs. A command is a normal method; there is nothing to port.
- **Cron:** nothing in the BCL parses cron expressions. The small, widely used library is Cronos (MIT, no dependencies, the parser behind Hangfire's recurring jobs). Adding it is a dependency decision (OD-01/OD-02), so it is an explicit question below.
- **Whole schedulers:** Quartz.NET (persistent job store, clustering, misfire policies) and Hangfire (dashboard, storage, recurring jobs) are large products with their own storage and model. They answer a bigger question than Laravel's `Schedule`, and platform schedulers (cron, Kubernetes `CronJob`, systemd timers, Azure/AWS schedulers) run one-off processes better than an in-process loop for some deployments.
- **Locks:** `ICacheLock` (R04) already gives token-owned `AcquireAsync` and `ReleaseAsync` with a TTL, with Redis and Memcached drivers that are shared across processes and a Memory driver that is not.

## Proposed quickstart (S1)

```csharp
// registration, Program.cs
builder.Services.AddNaravelScheduling(schedule =>
{
    schedule.Call("prune-sessions", async (sp, ct) => await sp.GetRequiredService<SessionPruner>().RunAsync(ct))
            .EveryFiveMinutes().WithoutOverlapping();
    schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct))
            .DailyAt(8, 0).OnOneServer();
});
```

Two lines of user code per task after the single registration call. Nothing else to run: the module is a hosted service, so `dotnet run` or the deployed host runs the schedule. A task is a delegate that receives a scoped `IServiceProvider` and a `CancellationToken`.

## Proposed decision

### Scheduling (accept, thin)

1. New package `Naravel.Scheduling` (hosted service, fluent builder, no web dependency), referencing the Cache abstractions for `ICacheLock`. Added to `Naravel.slnx`.
2. **Fluent surface:** `Call(name, handler)` and, once the Queue API is confirmed at T02, `Job<T>(name)` that dispatches through `IJobDispatcher`. Frequencies: `EveryMinute`, `EveryFiveMinutes`, `EveryTenMinutes`, `EveryFifteenMinutes`, `EveryThirtyMinutes`, `Hourly`, `HourlyAt(minute)`, `Daily`, `DailyAt(hour, minute)`, `Weekly`, `WeeklyOn(day, hour, minute)`, `Monthly`, `Every(TimeSpan)`, and `Cron(expression)` (see question D3). Filters: `Weekdays`, `Between(start, end)`, `When(predicate)`, `Skip(predicate)`, `InTimeZone(TimeZoneInfo)` (default UTC).
3. **Names are required and explicit.** The name is the lock key and the metric tag. Laravel derives the mutex from the command text, which silently changes when the code changes; an explicit name is stable across deploys.
4. **Ticks and slots.** The host loop wakes on each minute boundary using `TimeProvider`, computes the due tasks for that minute and starts each on its own task. Missed minutes (host stopped or paused) are **skipped, not replayed**, as with cron. No catch-up is offered in the first version.
5. **`WithoutOverlapping(expiresAfter)`:** acquires `schedule:overlap:<name>` through `ICacheLock` before running, releases it in `finally`, and skips the run when the lock is held. The TTL (default one hour, configurable) is the safety net after a crash; Laravel's 24 hours is too long for a task that runs every five minutes.
6. **`OnOneServer()`:** acquires `schedule:slot:<name>:<minute>` and never releases it (the TTL expires it), so exactly one instance runs the task for that minute, the same method Laravel uses. It needs a lock backend shared by all instances (Redis or Memcached). With the Memory driver it would silently run on every instance, so **startup fails with a clear message** when `OnOneServer` is used with a driver that is not shared. `ICacheLock` has no way to say whether it is shared today, so T02 adds a small additive marker for it (for example an `IsShared` property with a default of `true`, set to `false` by the Memory driver); this is the only change to existing code.
7. **Failure isolation:** an exception in one task is logged, counted and reported through `OnFailure`, and never stops the loop or other tasks. Cancellation on shutdown is passed to running tasks and awaited with the host's shutdown timeout.
8. **S3:** a `Meter` with bounded tags only (`task` name, `outcome` = `succeeded`, `failed`, `skipped_overlap`, `skipped_other_server`, `skipped_filter`) and an `ActivitySource`; a `SchedulerFake` / manual-tick test helper built on `FakeTimeProvider` so a user can assert "this task is due at 08:00" without waiting.
9. **S2:** the loop wakes once a minute, so this is not a hot path and no benchmark is added. The per-minute work is a linear scan over the registered tasks with no reflection; task delegates are compiled by the caller, not by the module.
10. **Out of the first version:** `exec`/`command` process tasks, ping URLs, `runInBackground`, maintenance-mode awareness, persistence of last-run times and a dashboard. They are listed in the backlog. `schedule:list` is a method (`IScheduler.Tasks` with name, expression and next run), so any command or endpoint can print it.

### Commands (`DECLINED`, recipe)

1. No `Naravel.Console` package and no signature-string parser. Record `DECLINED` with this PDR as the reason.
2. Document the recipe in `docs/en|fa/scheduling.md`: a command is a `System.CommandLine` `Command` or a plain method, built with the host's `IServiceProvider`; a table maps Artisan calls (`argument`, `option`, `call`, `ask`, `confirm`) to their .NET equivalents.
3. `route:list` and similar built-in commands are a separate question: they depend on endpoint metadata (R05) and are not part of this PDR.

## Alternatives considered

1. **Hand-written `BackgroundService` plus `PeriodicTimer` per project (decline Scheduling too).** Rejected as the default: it works for one task on one server, but the overlap and one-server guards are the part that goes wrong, and every project would rewrite them. It remains a documented fallback.
2. **Quartz.NET or Hangfire as the engine, with a Laravel-style fluent wrapper.** Rejected for the first version: both bring their own job store and model, which is a larger commitment than Laravel's `Schedule` and a heavy dependency for a port whose rule is "thin". A wrapper over either can be a later adapter if an owner needs persistence or clustering.
3. **Platform schedulers only (cron, Kubernetes `CronJob`).** Not rejected as a deployment choice: it is the right answer when each run should be a separate process. It cannot give an in-process fluent definition, and the docs will name it as the alternative.
4. **A `Naravel.Console` with signature strings and `make:command`.** Rejected: it adds a second command model next to `System.CommandLine`, which already gives typed binding, help and completion. It fails rule zero.

## Decisions for the owner

- **D1 Scheduling:** accept as proposed (recommended), decline with the recipe in alternative 1, or accept with a Quartz/Hangfire engine (alternative 2).
- **D2 Commands:** `DECLINED` with the recipe (recommended), or a thin `Naravel.Console` (alternative 4).
- **D3 Cron expressions:** include `Cron(expression)` using the Cronos package (recommended: a hand-written cron parser is correctness-sensitive code, and Laravel code uses `cron()` often), or ship only the fluent frequencies and `Every(TimeSpan)` with no new dependency. The exact version is pinned at T02 from the NuGet audit.
- **D4 Overlap TTL default:** one hour (recommended) or Laravel's 24 hours.

## Contracts and quality (if accepted)

No task payloads, exception messages with user data or lock tokens in logs or metric tags; metric tags are bounded (task names come from code, not from input); all I/O async with `CancellationToken`; the clock only through `TimeProvider`; the host's shutdown token is honoured. Tests (T02): a fake-clock test per frequency and a daylight-saving and time-zone case; a due-in-this-minute test; an overlap test with **two scheduler instances over one shared lock** where exactly one runs; a one-server test with two instances; a failure-isolation test; the startup failure for `OnOneServer` on a non-shared driver.

## Approval

Not yet approved. After approval the stage runs T02 (Scheduling), T03 (Commands only if kept; otherwise ticked as `DECLINED` with the recipe docs) and T04 (Validate).
