# PDR-003: Hybrid consumption instead of `__call` forwarding

- **Status:** Accepted
- **Laravel equivalent:** `Manager::__call($method, $params)` forwards any method to the default driver
- **Native .NET equivalent:** DI registration of the default driver as a plain service

## Context
In Laravel you can write `Cache::get('k')` or `$manager->get('k')`; the manager forwards to the default driver with a magic method.
.NET has no magic methods. Options considered:
- **A.** The manager implements the driver interface and delegates every member (boilerplate per method, must track interface changes, or needs a source generator).
- **B.** Always call `manager.Driver().Method()`.
- **C.** Hybrid: register the default driver directly in DI as `TDriver`; inject the manager only when runtime selection is needed.

## Decision
**C.** `AddNaravelManager` registers the manager, the registry, the options and `TDriver` (resolved from `manager.Driver()`).

## Consequences
- Most code injects `ICacheDriver` and never sees the manager. No delegation boilerplate; full type-safety and IntelliSense.
- The plain `TDriver` is resolved once (default at first injection). Code that must follow a changing default or pick drivers dynamically injects the manager.
- The DI container will also dispose the default driver; drivers must have idempotent disposal (documented in the authoring guide).

## Rejected alternatives
- **A:** maintenance cost grows with every interface change; solves a problem DI already removes.
- **B alone:** verbose for the 99% case that only needs the default driver.
