# Naravel.Queue

A Laravel-inspired, driver-based background job queue for .NET. Dispatch, retry with backoff, chaining, batching and a background worker - core package (add Naravel.Queue.Memory for the in-process driver, or Naravel.Queue.Redis/.File/.Database/.RabbitMQ/.Kafka for other backends).

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue
```

Targets .NET 10. It depends on `Naravel.Foundation`, which NuGet installs with it.

## Related packages

- [`Naravel.Queue.Memory`](https://www.nuget.org/packages/Naravel.Queue.Memory): In-process "memory" queue driver for Naravel.Queue - zero dependencies, ideal for tests and local development.
- [`Naravel.Queue.File`](https://www.nuget.org/packages/Naravel.Queue.File): File-based queue driver for Naravel.Queue - durable, zero external services required, JSON files on disk.
- [`Naravel.Queue.Redis`](https://www.nuget.org/packages/Naravel.Queue.Redis): Redis queue driver for Naravel.Queue, built on StackExchange.Redis - priority buckets, delayed jobs and automatic reclaim of jobs left behind by a crashed worker.
- [`Naravel.Queue.Database`](https://www.nuget.org/packages/Naravel.Queue.Database): EF Core-backed queue driver for Naravel.Queue - works with any EF Core provider (SQL Server, PostgreSQL, SQLite, MySQL).
- [`Naravel.Queue.RabbitMQ`](https://www.nuget.org/packages/Naravel.Queue.RabbitMQ): RabbitMQ queue driver for Naravel.Queue - delayed jobs via per-message TTL + dead-lettering, no plugin required.
- [`Naravel.Queue.Kafka`](https://www.nuget.org/packages/Naravel.Queue.Kafka): Kafka queue driver for Naravel.Queue, built on Confluent.Kafka - semantics adapted for a log-based broker (see class docs for details).
- [`Naravel.Queue.Coordination`](https://www.nuget.org/packages/Naravel.Queue.Coordination): Optional Naravel.Queue job middleware that coordinates workers through Naravel.Cache locks and rate limits: WithoutOverlapping, RateLimited and ThrottlesExceptions.
- [`Naravel.Queue.Encryption`](https://www.nuget.org/packages/Naravel.Queue.Encryption): Optional Naravel.Queue adapter that encrypts the payload of jobs marked IShouldBeEncrypted with ASP.NET Core Data Protection. Unmarked jobs keep the existing wire format.

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
