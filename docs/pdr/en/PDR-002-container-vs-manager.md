# PDR-002: Container vs Manager — what Foundation replaces (and what it does not)

- **Status:** Accepted
- **Laravel equivalent:** `Illuminate\Container\Container` (IoC) and `Illuminate\Support\Manager` (driver factory)
- **Native .NET equivalent:** `Microsoft.Extensions.DependencyInjection` (+ Keyed Services, .NET 8+)

## Context
The Foundation proposal was framed as extracting logic from `illuminate/container` + `illuminate/support`. These are two different layers:
- The **Container** binds abstractions to implementations. It has *no* notion of "N named implementations of one contract chosen by string at runtime".
- The **Manager** is the layer Laravel added on top of the container to solve exactly that.

.NET's container already covers the first layer. Keyed Services cover part of the second, but:
1. Keyed registrations can only be made **before** the provider is built; Naravel requires runtime `extend()`.
2. DI gives no guarantee of once-only creation for **async** driver construction.
3. There is no configuration-driven default plus invalidation logic.

## Decision
- Keep the .NET DI container unchanged. Do **not** build a Naravel container.
- Build `Manager<TDriver,TOptions>` + `IDriverRegistry<TDriver>` in `Naravel.Foundation` to fill only the gaps above.
- Keyed DI is not used as the source of truth; the registry is.

## Consequences
- Value of Foundation is concentrated in: runtime `Extend`, async once-only creation, config-driven default and invalidation, and one shared contract for all modules.
- Without the runtime-extend requirement, plain Keyed DI + `IOptions` would have been enough for much of this.

## Rejected alternatives
- *Custom IoC container:* duplicates a mature, well-integrated framework component.
- *Keyed DI only:* fails the runtime-extend requirement.
- *Per-module ad-hoc managers:* duplicates and diverges the same logic N times.
