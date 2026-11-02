# Naravel.Queue.Coordination

Optional Naravel.Queue job middleware that coordinates workers through Naravel.Cache locks and rate limits: WithoutOverlapping, RateLimited and ThrottlesExceptions.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Queue.Coordination
```

Targets .NET 10. It depends on `Naravel.Queue` and `Naravel.Cache`, which NuGet installs with it.

## Documentation

- Queue coordination: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue-coordination.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/queue-coordination.md)
- Queue: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/queue.md) · [راهنمای فارسی](https://github.com/km-arc/Naravel/blob/main/docs/fa/queue.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
