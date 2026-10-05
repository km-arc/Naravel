# PDR-008 — Filesystem on Foundation

Status: **Accepted and implemented 2026-10-04.** The owner approved this PDR, including the public API/config migration and S3 region/client-lifetime decisions. Stage 2 passed the full solution build and test suite (172 tests).

## Context

`Naravel.Filesystem` currently has a private `StorageManager`, `IFilesystemDriverFactory` and `FilesystemDriverFactory`. It caches disks itself, uses `IOptions<FilesystemOptions>`, and configures `DefaultDisk`/`Disks`. `Naravel.Foundation` already provides the common manager behavior required by the other driver-based modules: named-driver caching, runtime `Extend`, config reload invalidation and driver disposal.

The local driver also has a critical root-containment security fix and regression tests. The migration must preserve those guarantees. `DiskOptions.Region` currently has no effect, and `S3StorageDriver` owns an AWS client without disposing it.

## Decision

### Manager and registration

- Use `StorageManager : Manager<IStorageDriver, FilesystemOptions>` and retain `Disk(string? name = null)` as the filesystem-facing alias for `Driver(name)`.
- Derive `FilesystemOptions` from `ManagerOptions`; use `Default` and `Stores`, not `DefaultDisk` and `Disks`.
- Register built-in disk drivers through Foundation's `IDriverRegistry<IStorageDriver>` / `AddNaravelDriver` pattern. Preserve the `AddNaravelFilesystem` entry point and allow an optional configuration section name, defaulting to `Filesystem`.
- Remove the duplicate factory/manager resolution path (`IFilesystemDriverFactory`, `FilesystemDriverFactory`, and the old `IStorageManager` abstraction) rather than layering Foundation under it. This is a public API migration and must be called out in release notes.
- Runtime custom drivers use the manager's inherited `Extend`; do not add another registry or cache.

Example configuration:

```json
{
  "Filesystem": {
    "Default": "local",
    "Stores": {
      "local": { "Driver": "local", "Root": "storage/uploads", "BaseUrl": "/storage" },
      "archive": { "Driver": "s3", "Key": "...", "Secret": "...", "Bucket": "archive", "Endpoint": "https://s3.example.test", "Region": "us-east-1" }
    }
  }
}
```

### Driver lifetime and S3 settings

- Keep disposal out of the `IStorageDriver` contract. Foundation already disposes cached drivers that implement `IDisposable` or `IAsyncDisposable`; implement `IDisposable` on `S3StorageDriver` and dispose its owned `IAmazonS3` client.
- Make `DiskOptions.Region` effective: use `AmazonS3Config.RegionEndpoint` for standard AWS endpoints, and `AuthenticationRegion` for custom S3-compatible `ServiceURL` endpoints (the SDK clears `RegionEndpoint` when a custom service URL is set). Preserve the default endpoint behavior when `Region` is absent.
- Do not upgrade `AWSSDK.S3` as part of this stage; that upgrade remains subject to separate owner approval.

### API, safety and tests

- Preserve the existing async storage operations and add an optional `CancellationToken` to `GetUrlAsync` so all asynchronous I/O-facing APIs follow the repository's async/cancellation convention.
- Preserve the path-traversal containment check unchanged and retain its regression cases.
- Add local-driver contract tests for exists/get/stream/put/delete/copy/move/size/url; manager tests for multiple named stores, runtime `Extend`, config reload, default selection, and disposal; and an S3-focused test using an injectable client/factory seam or an already-approved test utility. Do not add a new package without a PDR decision.
- Add `docs/en/filesystem.md` and `docs/fa/filesystem.md` with configuration, disk selection, controller upload, temporary URL, custom driver, limitations and security behavior. Update parity tables, module status and changelog.

## Alternatives considered

1. **Keep the private manager/factory.** Rejected: it duplicates Foundation's driver resolution, cache, runtime extension, reload and disposal behavior.
2. **Wrap Foundation from the existing manager.** Rejected: it creates two manager APIs and keeps unnecessary forwarding surfaces.
3. **Make every `IStorageDriver` disposable.** Rejected: disposal is optional per driver, and Foundation already handles both disposal interfaces without imposing them on local/custom drivers.
4. **Rename `Disk()` to `Driver()`.** Rejected: `Disk()` is the user-facing filesystem concept and is already used by the module; it can delegate to Foundation's `Driver()`.

## Consequences

- No new runtime or test project is required; the existing `Naravel.Filesystem` and `Naravel.Filesystem.Tests` projects are migrated.
- The configuration shape and manager abstraction change. The old `DefaultDisk`/`Disks` shape and `IStorageManager`/factory APIs are removed as part of the migration.
- Foundation becomes responsible for driver caching, live config invalidation, runtime extension and disposal. Filesystem-specific storage behavior remains in its own driver contract.
- The Stage 0 path-containment security fix remains mandatory; no migration step may weaken it.

## Approval recorded

The owner approved the proposed decisions on 2026-10-04. Implementation preserves the path-containment fix, and the listed tests and EN/FA docs are complete. The solution build succeeded and all 172 tests passed on 2026-10-04; live AWS/S3 integration was not run.
