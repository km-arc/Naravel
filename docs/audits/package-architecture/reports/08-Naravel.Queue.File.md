# Review Report — Naravel.Queue.File

## Review metadata

- Package ID: `Naravel.Queue.File` (SDK default from project/assembly name; no explicit `PackageId`).
- Source project: `src/Naravel.Queue.File/Naravel.Queue.File.csproj`.
- Source-review baseline: `main` at `c0aa29b4743760d879c0dc1041f5669e92f5b592`.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: package project metadata and references; shipped/unshipped public API; `FileQueueDriver`; registration extension and configuration defaults; shared `QueueDriverContractTests`; File recovery/fencing/stress tests; `IQueueDriver`; `DispatchOptions`; Queue and NuGet guide sections.
- Dependency map: one project reference to `Naravel.Queue`; no direct broker or third-party storage dependencies. The driver is optional.
- Verification performed: queue-path and directory resolution trace; queue state layout; temporary write and atomic replace control flow; reservation/reclaim/fencing sequence; static gate lifetime; timeout configuration; corrupt-envelope handling; cancellation propagation and test coverage.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, Windows/Linux path traversal reproduction, cross-process stress, disk-full/cancellation failure injection, corrupt-file recovery tests, mutation testing and independent review.

## Executive assessment

- The driver provides durable JSON-file-backed queues and does not require an external service. Its basic shared contract and the additional recovery/fencing tests cover priority, delay, retry attempt propagation, crash reclaim, stale-worker protection and in-process concurrent consumers.
- A critical path-boundary flaw exists: queue names are transformed by deleting invalid filename characters, so the result is not a safe or unique directory component. A queue named `..` escapes the configured root, and different names can collapse onto the same directory.
- Several lifecycle/recovery details need hardening: static gate objects are never reclaimed; failed temporary writes can leave orphan files; non-positive visibility timeout is accepted; malformed queue files are silently skipped or can block stale-reservation reclaim; and `SizeAsync` ignores cancellation.
- No runtime code, tests, dependencies or public APIs were changed by this review.

## Findings

### [P1] FILE-001 — Queue-name sanitization allows path traversal and namespace collisions

- Remediation state: OPEN.
- Architecture disposition: VETO AV-011 — queue names must map safely and injectively beneath the configured file-store root.
- Evidence status: CONFIRMED BY SOURCE-LEVEL PATH ANALYSIS; OS-level exploit/collision reproduction NOT RUN.
- Evidence: `src/Naravel.Queue.File/FileQueueDriver.cs:36-40` combines `_basePath`, `Sanitize(queue)` and a subdirectory. `Sanitize` at line 87 removes characters from `Path.GetInvalidFileNameChars()` but leaves dots unchanged. Therefore `Sanitize("..")` is still `".."`; `Dir("..", "pending")` resolves one level above the configured base directory. On Windows and Unix-like systems, slash is an invalid filename character, so names such as `"a/b"` and `"ab"` are both reduced to `"ab"`. The public `DispatchOptions.OnQueue` setter simply stores the provided name and `QueuedMessage.Queue` is publicly settable; this provider does not validate the value before constructing the path.
- Affected contract: every File-queue operation that calls `Dir`, including enqueue, pop, ack, release, fail and size.
- User impact: a caller-controlled or malformed queue name can create/read/write pending, reserved and failed directories outside the configured root. Separately, distinct logical queues can alias to the same physical directory, allowing one queue consumer to read or settle another queue's messages. This is both a storage-boundary/security issue and a cross-queue isolation failure.
- Trigger / failure scenario:
  1. Construct `FileQueueDriver("/var/app/queue")`.
  2. Push a message with `Queue = ".."`.
  3. `Dir("..", "pending")` uses `/var/app/queue/../pending`, which is outside the configured root.
  4. Alternatively, push messages on `"a/b"` and `"ab"`; both can resolve to the same `ab` directory after sanitization.
- Why existing tests do not catch it: the shared contract uses ordinary alphanumeric queue names. File recovery tests use simple fixed names such as `"q"`, `"fence"` and `"stress"`. There are no tests for dot segments, invalid characters, path containment or name collisions.
- Required correction: queue names must be rejected or encoded injectively; deleting invalid characters is not safe. Every resolved queue directory must be proven to remain under a canonicalized base root. Keep path semantics independent of the host OS, and ensure two distinct accepted queue names cannot map to the same directory. Include protection against `.`, `..`, rooted/drive-qualified paths, separators, empty names and platform-specific invalid characters.
- Regression tests required: traversal attempts for `..`, `../x`, `..\x`, rooted paths and drive-qualified paths; collision pairs including `a/b` vs `ab`, `a:b` vs `ab` on Windows, and empty/invalid-only names; verify all accepted paths remain under the configured root on Windows and Linux; prove distinct logical queues remain isolated.
- Public API / PDR / release impact: queue-name acceptance and on-disk directory mapping may change. Existing pending/reserved/failed files may be stranded if path mapping changes; owner decision AD-014 is required before choosing strict validation versus an encoded/versioned layout and migration policy.

### [P2] FILE-002 — Static per-queue semaphores are never reclaimed

- Remediation state: OPEN.
- Architecture disposition: VETO AV-010 — process-local queue coordination state must have a bounded lifecycle.
- Evidence status: CONFIRMED BY SOURCE-LEVEL LIFETIME ANALYSIS; heap/soak reproduction NOT RUN.
- Evidence: `src/Naravel.Queue.File/FileQueueDriver.cs:107` declares a static `ConcurrentDictionary<string, SemaphoreSlim> QueueGates`. `GateFor` at lines 109-110 adds an entry keyed by the full base path plus sanitized queue name. No method removes or disposes entries, and the driver has no disposal lifecycle.
- Affected contract: long-running processes that create File drivers for many roots or use many distinct queue names.
- User impact: every distinct path/queue pair permanently retains a `SemaphoreSlim` in the process, even after its queue directory is empty, the File driver is no longer used, or a test's temporary directory is deleted. Dynamically named queues or temporary roots can cause unbounded process-local coordination state.
- Why existing tests do not catch it: File tests use a bounded set of queue names and short-lived test processes. They verify message correctness, not gate cardinality or reclamation.
- Required correction: introduce a safe gate-lifetime strategy that cannot dispose/remove a semaphore while a waiter or holder can still reference it. A reference-counted gate entry or a driver-scoped coordination owner may be considered; a naive `TryRemove` after release is unsafe because a concurrent waiter may already have fetched the old gate.
- Regression tests required: stress many distinct queue/root pairs; retire unused gates; race retirement against waiters and active holders; prove there is still only one effective gate for a given queue while operations overlap.
- Public API / PDR / release impact: internal lifecycle correction preferred; it must preserve cross-instance in-process coordination for the same filesystem path and queue.

### [P2] FILE-003 — Failed or cancelled temporary writes can leave orphan .tmp files

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY CONTROL-FLOW ANALYSIS; injected disk/cancellation reproduction NOT RUN.
- Evidence: `WriteAtomicAsync` at `src/Naravel.Queue.File/FileQueueDriver.cs:52-64` creates a unique temporary path and calls `File.WriteAllTextAsync(tmp, content, cancellationToken)` before entering the `try` block. Cleanup of the temporary file occurs only in the `catch` around the subsequent rename. If the write itself throws or is cancelled after creating a partial file, execution never enters that cleanup block.
- Affected contract: `PushAsync`, reservation rewrite, release and fail operations that use `WriteAtomicAsync`.
- User impact: repeated disk-full, I/O or cancellation failures can leave partial `.tmp` files beside queue records. These files are ignored by the `*.json` enumerations and do not block immediate polling, but they consume disk space indefinitely and can eventually exhaust the queue volume.
- Why existing tests do not catch it: no File tests inject a failure during the content write or cancellation between temporary-file creation and completion. The shared contract only tests successful operations.
- Required correction: put both temporary-file creation/write and rename inside one cleanup-protected scope; best-effort delete the temp file on every unsuccessful exit, including cancellation. Preserve the original failure and do not let cleanup mask it. Consider a bounded startup/maintenance cleanup for orphan temp files left by process termination, since process crashes cannot run the catch block.
- Regression tests required: inject a write failure after a partial temp file exists; cancel a write after temp creation; assert no `.tmp` file remains after handled failures; separately simulate process-crash leftovers and prove any recovery cleanup cannot delete active writes.
- Public API / PDR / release impact: implementation-only.

### [P2] FILE-004 — Zero or negative visibility timeout makes reservations immediately reclaimable

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; duplicate-delivery reproduction NOT RUN.
- Evidence: the `FileQueueDriver` constructor at lines 24-27 assigns the caller-supplied `visibilityTimeout` (or the five-minute default) without validating it. `IsExpired` at lines 156-160 considers a reservation expired when `now - lastWrite >= _visibilityTimeout`. The DI registration at `src/Naravel.Queue.File/ServiceCollectionExtensions.cs:15-19` parses `VisibilityTimeoutSeconds` and passes the result directly to `TimeSpan.FromSeconds`.
- Affected contract: crash recovery and the worker's no-concurrent-redelivery guarantee for File queues.
- User impact: when configured with zero or a negative timeout, a second worker can reclaim a reservation immediately after it is created while the first worker is still processing. The same job can execute concurrently, undermining the visibility-timeout/fencing guarantees and potentially duplicating side effects.
- Why existing tests do not catch it: recovery tests use positive values (150 ms and 600 ms); the shared contract uses the default positive timeout. There are no tests for constructor or configuration values less than or equal to zero.
- Required correction: reject non-positive visibility timeouts before creating the base directory/driver, and surface a configuration error naming the store and setting at the registration boundary. Keep the same positive default.
- Regression tests required: constructor rejects zero and negative values; DI rejects `VisibilityTimeoutSeconds` values of `0` and negative numbers; positive boundary values remain supported; two workers cannot reclaim a live reservation before the positive timeout.
- Public API / PDR / release impact: fail-fast validation of invalid configuration; no public API change.

### [P2] FILE-005 — Corrupt queue files are silently skipped or can block stale-reservation reclaim

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; corrupt-file reproduction NOT RUN.
- Evidence: in `PopCoreAsync` at `src/Naravel.Queue.File/FileQueueDriver.cs:132-148`, a `JsonException` while reading a pending file is caught and ignored with `continue`; the file remains in `pending` forever and `SizeAsync` continues to count it. A JSON literal `null` deserializes to a null message, then the subsequent `message.AvailableAt` dereference is outside the catch. In `ReclaimStaleReservationsAsync` at lines 180-213, malformed JSON in a stale reserved file is not handled as a quarantine event; the method catches only I/O/access exceptions, so a `JsonException` can escape and cause every subsequent pop on that queue to fail during reclaim.
- Affected contract: delivery and recovery when a queue record is corrupted, truncated or incompatible with the current envelope reader.
- User impact: malformed pending records are invisible to workers but remain on disk indefinitely with no diagnostic or operator recovery path. A malformed expired reserved record can prevent new work in that queue from being popped until someone manually repairs/removes it. A valid `null` JSON envelope can escape as a `NullReferenceException`.
- Why existing tests do not catch it: there are no tests for malformed JSON, JSON `null`, missing required envelope fields or corrupt reserved records. The legacy-envelope test covers valid old JSON only.
- Required correction: define a poison-message policy: quarantine the unreadable file with a diagnostic that includes safe path/queue metadata but not payload contents/secrets; do not silently ignore it forever and do not let one corrupt reserved record block every healthy message. Treat JSON `null` as invalid input explicitly. Preserve the original file for operator recovery or move it into a separate quarantine directory rather than deleting it silently.
- Regression tests required: malformed pending JSON followed by a valid message; JSON literal null; malformed expired reserved JSON followed by a valid pending message; assert the worker can keep polling and the corrupted record is discoverable in quarantine with a diagnostic.
- Public API / PDR / release impact: operational failure-handling policy; owner decision may be needed if quarantine directory layout becomes part of the public operations contract.

### [P2] FILE-006 — SizeAsync ignores its CancellationToken

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: `SizeAsync` at `src/Naravel.Queue.File/FileQueueDriver.cs:269-273` does not inspect or pass its `cancellationToken` to the synchronous `Directory.EnumerateFiles(...).Count()` operation. Every other File driver method either checks/waits with the token or passes it to asynchronous file I/O.
- Affected contract: `IQueueDriver.SizeAsync` under request cancellation or host shutdown.
- User impact: a cancelled caller can still enumerate a very large pending directory to completion and receive a size result, delaying cancellation and continuing filesystem work the caller asked to stop.
- Why existing tests do not catch it: the shared contract invokes `SizeAsync` without a cancelled token and checks only exact counts for this provider.
- Required correction: call `ThrowIfCancellationRequested` before directory access and use cancellable enumeration where available/appropriate; if the underlying directory API cannot be interrupted once enumeration begins, document that limitation honestly and keep each iteration bounded/cancellation-aware.
- Regression tests required: pre-cancelled token throws before filesystem access; large-directory cancellation stops enumeration within a bounded cooperative interval.
- Public API / PDR / release impact: contract-consistency correction; no public API change.

## Existing guarantees and test coverage

- The shared contract exercises empty queue, push/pop/ack, availability delay, priority, release with attempt propagation, terminal failure, queue isolation and concurrent consumers.
- File recovery tests cover visibility-timeout reclaim, attempts increment on reclaim, stale-worker fencing for ack/release/fail, legacy envelopes without reservation IDs and 16 concurrent consumers with exactly-once delivery within one process.
- The driver serializes reserve/reclaim/settle operations per queue path within one process using a static semaphore; cross-process claim arbitration relies on filesystem moves and file sharing.
- File-backed failed records are kept under each queue's `failed` directory; the package README correctly notes that the separate failed-job inspection/retry list is in memory unless an application registers a persistent `IFailedJobStore`.

## Test assessment

- Reviewed: `tests/Naravel.Queue.Tests/DriverTests.cs`, shared `QueueDriverContractTests`, `FileDriverRecoveryTests`, worker queue docs and package README.
- Missing regression coverage: malicious/colliding queue names and root containment; static gate reclamation; temporary write cleanup; non-positive timeout validation; corrupt/null pending and reserved envelopes; pre-cancelled and mid-enumeration `SizeAsync`.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Windows/Linux path tests: NOT RUN.
- Disk-full/cancellation injection: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Risk summary and next action

- P0: none established.
- P1: FILE-001 (AV-011).
- P2: FILE-002 (AV-010), FILE-003, FILE-004, FILE-005, FILE-006.
- Recommended next task: resolve queue-path containment and injective name mapping first, with an approved on-disk compatibility/migration policy; then address bounded gate lifetime, atomic-write cleanup, timeout validation and poison-file quarantine before accepting the File driver as safe for untrusted or multi-tenant queue names.

## Plain-language explanation

### FILE-001 — A queue name can escape the configured folder or overlap another queue

- **In everyday words:** The File driver removes some invalid characters from queue names but leaves `..` unchanged. It can also turn two different names into the same folder name.
- **A realistic example:** A queue named `..` can make the driver build a path outside the configured queue folder. Names `a/b` and `ab` can both become the folder `ab`, mixing their messages.
- **What we checked:** `src/Naravel.Queue.File/FileQueueDriver.cs`, around lines 36–40 and 87, and the public queue-name assignment path in `DispatchOptions.OnQueue` / `QueuedMessage.Queue`.
- **Why that proves the risk:** The sanitizing step deletes characters rather than proving that the final path stays under the configured root or that accepted names remain unique.
- **What is not yet proven here:** This is a source-level path analysis. No operating-system exploit or collision test was run.
- **Safe outcome and proof:** Reject unsafe names or encode them so each accepted name maps to one unique directory below the canonical queue root. Test dot segments, rooted paths, separators, Windows drive paths, invalid-only names and collision pairs on Windows and Linux. Owner decision AD-014 is needed because changing the folder mapping may affect existing queue files.

### FILE-002 — File queue keeps a semaphore for every queue path forever

- **In everyday words:** The driver stores one coordination object for each queue path in a static dictionary and never removes it. Using many temporary queue names can therefore grow process memory indefinitely.
- **A realistic example:** A test or service creates many temporary queue folders. Even after the folders are deleted, the process still holds the semaphore objects for those paths.
- **What we checked:** `src/Naravel.Queue.File/FileQueueDriver.cs`, around lines 107–110, defines `QueueGates` and adds entries in `GateFor`; no removal/disposal path is present.
- **Why that proves the risk:** Every new path can add another object, and the package does not retire old entries.
- **What is not yet proven here:** No heap or soak test was run.
- **Safe outcome and proof:** Retire unused coordination objects only after no active user or waiter can still use them. Test many path/name pairs and race cleanup against waiting and active operations; prove one queue still has only one effective gate while calls overlap.

### FILE-003 — A failed or cancelled file write can leave temporary files behind

- **In everyday words:** The driver creates a temporary file before entering the cleanup block. If writing that file fails or is cancelled, the cleanup code is never reached.
- **A realistic example:** The disk fills after part of a queue record is written. The operation fails, but the partial temporary file remains and takes up space.
- **What we checked:** `WriteAtomicAsync` in `src/Naravel.Queue.File/FileQueueDriver.cs`, around lines 52–64. The write occurs before the `try` block that cleans up after rename failure.
- **Why that proves the risk:** An exception during the write exits the method before the existing cleanup handler runs.
- **What is not yet proven here:** No disk-full or cancellation fault was injected during a real file write.
- **Safe outcome and proof:** Cleanup must cover both writing and renaming, preserve the original error and remove partial files after handled failures. Test a partial-write failure and cancellation after temp-file creation. Consider safe startup cleanup for leftovers from process crashes.

### FILE-004 — A zero or negative visibility timeout lets another worker take a job immediately

- **In everyday words:** The driver accepts a timeout of zero or less. That makes a newly reserved job look expired right away, so another worker can take it while the first worker is still running.
- **A realistic example:** Worker A reserves a job with a zero timeout. Worker B polls immediately and sees A's reservation as expired, then runs the same job.
- **What we checked:** The constructor in `src/Naravel.Queue.File/FileQueueDriver.cs`, around lines 24–27, accepts the value; `IsExpired` around lines 156–160 uses it; DI passes `VisibilityTimeoutSeconds` through without range validation.
- **Why that proves the risk:** A non-positive time span makes the expiry condition true at or immediately after reservation.
- **What is not yet proven here:** The source path is confirmed; no duplicate-delivery runtime reproduction was run.
- **Safe outcome and proof:** Reject zero and negative values in both the public constructor and DI configuration. Test invalid values and prove two workers cannot take the same live reservation when a positive timeout is configured.

### FILE-005 — A damaged queue file can be ignored forever or stop the queue from being read

- **In everyday words:** A damaged pending file may be skipped without warning and remain on disk forever. A damaged expired reservation can instead make later polls fail before healthy jobs are read.
- **A realistic example:** One pending JSON file is truncated. The worker silently skips it but keeps counting it. In another case, one damaged reserved file causes every later `PopAsync` for that queue to fail during recovery.
- **What we checked:** `PopCoreAsync` in `src/Naravel.Queue.File/FileQueueDriver.cs`, around lines 132–148, and `ReclaimStaleReservationsAsync` around lines 180–213. The report also notes that JSON `null` is not handled before a message property is read.
- **Why that proves the risk:** The pending-file catch ignores malformed JSON, while the stale-reservation path does not quarantine malformed JSON and can let the exception escape.
- **What is not yet proven here:** No corrupt-file test was run for malformed JSON, null JSON or broken reserved records.
- **Safe outcome and proof:** A damaged record must be visible to an operator and must not block healthy work. Move it to a quarantine location with a safe diagnostic; do not silently delete it. Test malformed pending data followed by a healthy message, JSON null, and malformed expired reservations.

### FILE-006 — SizeAsync does not stop when its caller cancels

- **In everyday words:** The size method scans files but does not check the cancellation token. A cancelled caller can still trigger a full directory scan.
- **A realistic example:** A large queue folder is being counted when the application shuts down. The method continues counting even though the caller has cancelled.
- **What we checked:** `SizeAsync` in `src/Naravel.Queue.File/FileQueueDriver.cs`, around lines 269–273, uses synchronous file enumeration without checking the token.
- **Why that proves the gap:** There is no cancellation check before directory access and the enumeration itself is not made cooperative.
- **What is not yet proven here:** No large-directory cancellation test was run.
- **Safe outcome and proof:** A pre-cancelled call must stop before accessing the directory; long enumeration should check cancellation between bounded steps where possible. Test both pre-cancellation and cancellation during a large scan, and document any file-system operation that cannot be interrupted once started.
