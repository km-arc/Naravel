# Naravel.Queue.Redis

Redis queue driver for Naravel.Queue, built on StackExchange.Redis - priority buckets, delayed jobs and automatic reclaim of jobs left behind by a crashed worker.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Redis
```

Targets .NET 10. It depends on `Naravel.Queue` and `Naravel.Redis`, which NuGet installs with it.

## Documentation

- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md)
- Redis: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/redis.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
