# PDR-004: Config shape, hot reload, invalidation and disposal policy

- **Status:** Accepted
- **Laravel equivalent:** `config/cache.php` (`default`, `stores`), `env()`, `config:cache`, `forgetDrivers()`
- **Native .NET equivalent:** `IConfiguration` + Options pattern (`IOptionsMonitor<T>`)

## Decisions
1. **Shape:** `Default` + `Stores` dictionary (Laravel-like), bound to `ManagerOptions`. Store values are raw `IConfigurationSection`; each driver binds its own typed options. Named Options were rejected because the set of store names is user-defined and not known at compile time.
2. **Live options:** managers read `IOptionsMonitor<TOptions>`; `Default` is read on every call.
3. **Invalidation uses a fingerprint, not "clear on any change".** *Verified experimentally:* changing an unrelated key (`Other:X`) and calling `Reload()` still fires `IOptionsMonitor.OnChange`, because a section's change token is the whole configuration root's token. Clearing on every callback would rebuild connections (Redis, HTTP clients ...) on unrelated edits. The manager stores a fingerprint of all `Stores` (name + every key/value) and retires cached drivers only when it changes. `Default`-only changes never rebuild anything.
4. **Retire, don't dispose immediately.** Evicted drivers move to a retired list and are disposed when the manager is disposed, because a request may still be using them. Trade-off: memory grows by one driver per real config change; acceptable because real changes are rare.
5. **Ownership:** the manager owns and disposes what factories return; disposal is de-duplicated by reference; errors are aggregated after all drivers were attempted.
6. **Escape hatch:** `Forget(name)` / `ForgetAll()` for drivers that depend on configuration outside their own store section (Laravel's `connection => 'cache'` pattern).
7. **`config:cache` is not ported:** .NET has no per-request boot cost to optimise.
8. **Names are case-insensitive** (like .NET configuration keys).

## Rejected alternatives
- *Fixed `IOptions<T>`:* no reload; acceptable for Laravel's request-per-process model, poor for long-running .NET hosts and Kubernetes ConfigMaps.
- *Per-store fingerprints:* would miss cross-section dependencies (see item 6).
- *Immediate disposal on reload:* risks `ObjectDisposedException` in in-flight requests.
- *Reference counting/leases:* over-engineered for the benefit.
