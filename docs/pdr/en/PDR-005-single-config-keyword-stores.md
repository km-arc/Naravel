# PDR-005 — One config keyword: `Stores` (not `Connections`/`Disks`/etc.)

> **Implementation status (2026-10-04):** `Naravel.Queue` was renamed `Connections` → `Stores` in PDR-006. Filesystem's approved and verified PDR-008 migration changed `DefaultDisk`/`Disks` to `Default`/`Stores`. Statements below that describe the earlier configuration shape are historical.

Status: **Accepted**. Owner approved 2026 (see chat log referenced in PROGRESS.md).

## Context

`Naravel.Foundation`'s `ManagerOptions` binds configuration through a concrete property:

```csharp
public class ManagerOptions : IManagerOptions
{
    public string Default { get; set; } = string.Empty;
    public Dictionary<string, IConfigurationSection> Stores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

The standard .NET configuration binder matches JSON keys to property names. Because `Stores` is a concrete,
non-virtual property (not read through an attribute or a per-module override), **every module's configuration
section is technically required to use the literal key `"Stores"`** to bind correctly - there is no way for a
module to rename it to `"Connections"` or `"Disks"` without changing Foundation itself.

Laravel, by contrast, uses a different word per module (`config/queue.php` → `connections`, `config/cache.php` →
`stores`, `config/filesystems.php` → `disks`). Two modules already need a decision here:

- `Naravel.Queue` currently uses `"Connections"` (written before Foundation existed).
- The incoming `Naravel.Filesystem` port uses `"Disks"`.
- The incoming `Naravel.Cache` port (from `LaravelCacheNet`) already used `"Stores"`.

## Options considered

**A. One literal keyword (`Stores`) everywhere.** Every module's JSON section uses `"Stores"`, regardless of
what Laravel calls it. Requires no Foundation change. `Naravel.Queue` must rename `Connections` → `Stores` (a
breaking change, acceptable pre-1.0). Slightly less Laravel-authentic reading (`Filesystem:Stores:s3` instead of
`Filesystem:Disks:s3`), gained back partly by using `Disk()` as the *method* name on `StorageManager`.

**B. Per-module keyword via a Foundation extension point** (e.g. `[ConfigurationKeyName]` or a virtual property).
More faithful to Laravel's own vocabulary per module. Requires changing `IManagerOptions`/`ManagerOptions` and
adding tests for the new extension point before any module can use it - delays Queue, Cache and Filesystem work
that is otherwise ready to proceed.

## Decision

**Option A.** Every module's configuration section uses `"Stores"` as the collection key, bound through the
existing, already-tested `ManagerOptions.Stores`. This includes:

- `Naravel.Queue`: `"Connections"` → `"Stores"` (tracked as part of the Queue-on-Foundation migration, PDR-006).
- `Naravel.Filesystem`: uses `"Stores"` in configuration; the public API keeps Laravel's vocabulary at the
  *method* level (`StorageManager.Disk(name)`), not the config key.
- `Naravel.Cache`: already uses `"Stores"` - no change needed.

## Rejected alternative

Option B is not rejected outright - it is **deferred**. If, after Cache/Filesystem/Queue all ship, per-module
config vocabulary turns out to matter for adoption or documentation clarity, revisit with a new PDR. Until then,
consistency and shipping speed win over per-module authenticity in the config file specifically (the C# API
surface still uses Laravel's per-module words: `Disk()`, `Store()`, `Connection()`).

## Consequences

- `Naravel.Queue`'s `appsettings.json` shape changes (`Connections` → `Stores`) as part of PDR-006. Any code or
  docs written against the old shape must be updated in the same change.
- Every future module (Mail, Session, Events, ...) uses `"Stores"` too - documented here so nobody re-litigates
  it per module.
