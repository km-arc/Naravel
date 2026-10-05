# PDR-007 — Cache port

Status: **Accepted 2026-10-04; implemented and verified 2026-10-05.** The owner approved the PDR and the concrete .NET design below. Implementation details and provider constraints are recorded here and in `docs/en/cache.md`.

## Context

The approved source is [km-arc/LaravelCacheNet](https://github.com/km-arc/LaravelCacheNet). Its public scope includes cache values, `Remember`, per-user/per-tenant key scoping, tag-based invalidation and locks. It also contains a Queue implementation, but Naravel already has `Naravel.Queue`; that code must not be copied.

Naravel.Foundation already provides the shared manager, named-store registry, runtime `Extend`, configuration reload invalidation and driver disposal needed by Cache. Config collections use the repo-wide `Stores` key (PDR-005).

Two source defects must not be carried into the port:
- `RedisCacheStore.FlushAsync` can default to `scanPattern = "*"`, potentially deleting keys outside the Cache namespace.
- The source manager builds a case-sensitive name map, unlike Foundation's case-insensitive configuration and registry behavior.

## Accepted decision

### Packages and ownership

- `Naravel.Cache` owns the public cache and lock contracts, options/managers, memory implementations, `TaggedCacheStore` and `ScopedCacheStore`.
- `Naravel.Cache.Redis` owns Redis cache and lock implementations and references the already centrally managed `StackExchange.Redis` dependency.
- `Naravel.Cache.Memcached` owns Memcached cache and lock implementations and references `EnyimMemcachedCore`; keep this dependency opt-in so users of Cache or Redis do not pull it in.
- Add all three runtime projects and the Cache test project to `Naravel.slnx`; add every new package version only to `Directory.Packages.props`.

The port keeps `ICacheStore`, `ICacheLock`, `MemoryCacheStore`, `MemoryLock`, `RedisCacheStore`, `RedisLock`, `MemcachedCacheStore`, `MemcachedLock`, tagging and scoping. Exclude `IQueueStore`, `QueueManager`, `QueueManager` tests and every other Queue behavior from the source.

### Managers and configuration

- `CacheOptions : ManagerOptions` and `CacheManager : Manager<ICacheStore, CacheOptions>` use `Cache:Default` and `Cache:Stores`.
- `LockManager : Manager<ICacheLock, CacheOptions>` is a separate manager using the same `CacheOptions`, `Default`, `Stores` and store names. This follows the source's `CacheManager`/`LockManager` public split without inventing another lock configuration section.
- Provider registration maps each configured store name into the cache registry and, where that provider supports locking, the lock registry. Cache-only custom drivers need not implement `ICacheLock`; lock access to a store without a lock implementation must fail clearly.
- Preserve the source's useful call shapes (`Store(name)`, `ForUser(scope)`, `Tags(...)`, `RememberAsync`, and named lock operations) where they fit the .NET async/cancellation conventions. Prefer direct driver injection for default use and managers for runtime store selection, matching Foundation/PDR-003.
- Store names are case-insensitive. New-store registration is at startup; changes to values in an existing section reload through Foundation. No reflection-based driver discovery or duplicate manager/cache implementation.

Example configuration:

```json
{
  "Cache": {
    "Default": "memory",
    "Stores": {
      "memory": { "Driver": "memory", "Prefix": "naravel" },
      "redis": { "Driver": "redis", "ConnectionString": "localhost:6379", "Prefix": "naravel" },
      "memcached": { "Driver": "memcached", "Servers": "localhost:11211", "Prefix": "naravel" }
    }
  }
}
```

### Safety and behavior

- Redis key scans and flush must always be restricted to the configured store prefix. Never use a default `"*"` scan pattern. Tests must prove that flushing one configured Cache store cannot delete unrelated Redis keys or another prefix.
- Keep tag versioning rather than key enumeration so tagging works on Memory, Redis and Memcached. Document that old tagged values become unreachable but may remain allocated until expiry/eviction.
- Scope cache keys by an explicit caller-provided user/tenant identifier; do not infer identity from ambient HTTP state.
- Memory cache and locks are single-process only. Memcached locks are not fully atomic on release; document Redis as the distributed-lock choice for critical sections, matching the source limitation.
- Redis values use `System.Text.Json`; memory stores typed objects in process; Memcached passes typed values through Enyim's configured transcoder. Document serialization compatibility and expiration behavior per provider.
- Memcached cannot flush a key prefix: `FlushAsync` clears the entire configured cluster. Document this destructive scope and direct callers to tag invalidation for scoped removal.
- Named Memory stores default to their store name as a prefix so the shared `IMemoryCache` does not make their keys overlap.
- Tag version keys are treated as version `0` until the first flush. Reads and writes do not initialize them, avoiding a race between concurrent first use; flush atomically increments the version.
- For simplicity and connection-pool efficiency, all configured Memcached stores share one Enyim client and must use the same normalized server list. A different list is rejected at registration instead of silently merging clusters. Server topology changes require a host restart; ordinary store configuration values still reload through Foundation.
- A null Memcached TTL is passed as zero expiration (the Memcached protocol's no-expiry value), not silently capped at 30 days.
- Enyim's current client exposes atomic increment/decrement only as synchronous methods. The provider may isolate these calls on the thread pool to preserve the async store contract; document that cancellation can prevent queued work but cannot interrupt a counter operation already in progress.
- Async I/O APIs accept `CancellationToken`; do not introduce sync-over-async APIs.

## Implementation and verification

The test suite covers `CacheManager`, `MemoryCacheStore`, `MemoryLock`, `ScopedCacheStore` and `TaggedCacheStore`, with no Queue tests. Foundation integration tests cover two names using the same driver type, runtime `Extend`, config reload of an existing store, unrelated reload stability, default selection and disposal. Provider tests cover configuration and prefix-bounded Redis flushing, plus Memcached configuration, key handling and TTL behavior. The Cache project has 27 test cases. Live Redis/Memcached integration was not run.

The bilingual Cache guides, parity tables, `AGENTS.md`, `PROGRESS.md`, README files and `CHANGELOG.md` document the implemented APIs and provider constraints. Full solution build and tests passed on 2026-10-05; live Redis/Memcached integration remains unverified.

## Alternatives considered

1. **Copy the source Queue implementation.** Rejected: duplicates `Naravel.Queue` and would create competing job/queue APIs.
2. **Use one Cache manager for both cache and locks.** Rejected: the approved source exposes distinct cache and lock contracts and managers; separate Foundation managers retain that separation while sharing options and store names.
3. **Put Redis/Memcached dependencies in the core package.** Rejected: users should opt into only the backend packages they use.
4. **Flush Redis with an unrestricted wildcard.** Rejected: it can delete unrelated application data; every scan must be prefix-bounded.
5. **Use case-sensitive store resolution.** Rejected: it conflicts with Foundation and .NET configuration conventions.

## Approval recorded

The owner approved the PDR on 2026-10-04, including separate `CacheManager` and `LockManager` sharing `Default`/`Stores`, the Memcached provider, serialization behavior and prefix-safe Redis flushing.
