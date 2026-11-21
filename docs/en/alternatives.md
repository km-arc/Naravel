# Naravel and the alternatives

Naravel is a small set of in-process packages that use Laravel's vocabulary: one way to name, dispatch, test and configure the common cases. It does not try to replace products that are built for harder cases. This page covers the three modules that overlap most with well-known .NET libraries (queue, scheduling and cache) and says, for each, what the other library gives that Naravel does not, so that you can pick the right one.

Two things apply to every section:

- Naravel is **0.x (pre-1.0)**: the public API can still change between minor versions (see [Status](../../README.md#status)). If you need a 1.0 API promise today, that alone can decide it.
- Statements about another project are what that project's own pages say, linked in [Sources](#sources) with the date they were read. Those projects change; check the page before you rely on a detail.

The other modules (events, mail, notifications, database, pagination, filesystem, routing, encryption, redis) are not compared here. For them, the [Laravel parity table](laravel-parity.md) and the [PDRs](../pdr/en) record what .NET already provides and why Naravel does or does not add a package.

## Background jobs

**Naravel.Queue** ([guide](queue.md)) dispatches job classes with a delay, a priority, retries with backoff, chains and batches, and runs them in hosted workers. The drivers are memory, file, Redis, database (any EF Core provider), RabbitMQ and Kafka. Delivery is at-least-once: a job can run more than once, so jobs must be idempotent. One exception is open on RabbitMQ: a retry or a failed job is acknowledged before its replacement message is published, so a stop between the two loses the job ([audit finding RQM-001](../audits/package-architecture/reports/11-Naravel.Queue.RabbitMQ.md)). Kafka cannot delay a job. Batch progress is kept in memory unless you add a database or Redis batch repository. Naravel ships no dashboard. Every module has a fake for tests (`QueueFake` here).

**Hangfire** describes itself as an "incredibly easy way to perform fire-and-forget, delayed and recurring jobs in .NET applications", backed by Redis, SQL Server, SQL Azure and MSMQ, and its README shows a dashboard (`app.UseHangfireDashboard()`). It is licensed under the LGPL.

**Coravel** offers a zero-configuration queue that runs in memory.

| Pick | When |
|---|---|
| Naravel.Queue | You want a broker or database behind the queue (RabbitMQ, Kafka, Redis, EF Core) with Laravel's dispatch options in one API, and fakes you can assert on in tests. You accept at-least-once delivery. |
| Hangfire | You want a dashboard, or one of the stores Hangfire supports, from a mature product. |
| Coravel | An in-memory queue is enough and you want the smallest setup. Queued work does not survive a restart. |

## Scheduling

**Naravel.Scheduling** ([guide](scheduling.md)) runs named tasks inside your host. A hosted service wakes once a minute and starts what is due. Frequencies are fluent (`EveryFiveMinutes`, `DailyAt`) or a five-field cron expression; seconds are not supported. `WithoutOverlapping` and `OnOneServer` work across instances through the locks of `Naravel.Cache`, when the cache store is shared (for example Redis). The locks are leases that expire (the overlap lock after one hour by default), so a task that runs longer than its lease can overlap with the next run. Minutes missed while the host was stopped are skipped, not replayed, and the last run of a task is not stored anywhere.

**Quartz.NET** keeps scheduling data either in memory, where it is lost when the application ends or crashes, or in a database through ADO.NET (SQL Server, PostgreSQL, MySQL, Oracle, Firebird and SQLite among others). Its clustering lets several scheduler instances share one database, and a node that dies can have its jobs recovered by the others when the job asks for it.

**Hangfire** schedules recurring jobs with CRON expressions and keeps them in its store.

**Coravel** declares schedules in code with a fluent syntax; its README quick start uses `EverySecond()`.

| Pick | When |
|---|---|
| Naravel.Scheduling | A handful of in-process tasks with simple guards, as Laravel's `Schedule` covers, and a missed minute may be skipped. |
| Quartz.NET | The schedule or its state must survive a restart, you need clustering with failover, or you need triggers that Naravel does not have. |
| Hangfire | You want recurring jobs, persistence and a dashboard in the same product as your background jobs. |
| Coravel | A task must run more often than once a minute. |
| The platform (cron, a Kubernetes `CronJob`, systemd timers) | Every run should be a separate process. |

## Cache

**Naravel.Cache** ([guide](cache.md)) gives named stores (memory by default, with Redis and Memcached packages), scopes (`ForUser` and `Scope`), tag invalidation, token-owned locks and a rate limiter. It is a single-level cache: a store is the memory, Redis or Memcached store you name, with no second tier in front of it. `RememberAsync` does not lock: two requests that miss the same key at the same moment both run the factory and the last write wins. For an expensive factory, wrap the call in a Naravel lock. A Naravel lock is a lease with a `ttl` and no renewal or fencing token, so a holder that outlives its `ttl` can overlap with the next holder ([audit finding AV-002](../audits/package-architecture/ARCHITECTURE-VETOES.md)); use it to save work, not to guard a write that must never overlap.

**HybridCache** (Microsoft) makes only one concurrent caller for a key run the factory and lets the others wait for its result. It uses `MemoryCache` as its primary storage and an `IDistributedCache` implementation, if you register one, as its secondary storage. It supports tags, and its invalidation by tag is a logical operation rather than a deletion.

**FusionCache** lists fail-safe (reusing an expired entry as a temporary fallback), cache stampede protection on one node and across nodes, a memory cache with an optional second level, an optional backplane that tells other nodes about changes, and tagging.

| Pick | When |
|---|---|
| Naravel.Cache | You want Laravel's cache vocabulary: named stores, scopes, tags, locks and a rate limiter in one package, with a fake for tests. |
| HybridCache | You want stampede protection and a two-tier cache from the platform, without another dependency. |
| FusionCache | You also want fail-safe and a backplane between nodes. |

They can live in one application: use HybridCache or FusionCache for hot read-through data, and Naravel locks and the rate limiter where you need them.

## Sources

Read on 2026-11-20. The Naravel statements come from this repository's guides, source and tests.

- Hangfire: [README](https://github.com/hangfireio/hangfire/blob/main/README.md)
- Quartz.NET: [Job stores](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/job-stores.html), [Advanced enterprise features](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/advanced-enterprise-features.html)
- Coravel: [README](https://github.com/jamesmh/coravel/blob/master/README.md)
- HybridCache: [HybridCache library in ASP.NET Core](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid)
- FusionCache: [package page on nuget.org](https://packages.nuget.org/packages/ZiggyCreatures.FusionCache)
