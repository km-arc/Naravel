# Review Report — Naravel.Queue.Memory

## Review metadata

- Package ID: `Naravel.Queue.Memory` (SDK default from project/assembly name; no explicit `PackageId`).
- Source project: `src/Naravel.Queue.Memory/Naravel.Queue.Memory.csproj`.
- Source-review baseline: `main` at `c0aa29b4743760d879c0dc1041f5669e92f5b592`; the package source is unchanged at this baseline.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; runtime verification pending).
- Files inspected: package project and package metadata; shipped/unshipped API baselines; `MemoryQueueDriver`; registration extension; `IQueueDriver`; `IFailedJobStore`; worker terminal-failure flow; shared `QueueDriverContractTests`; Memory contract subclass; NuGet README and Queue guide.
- Dependency map: only a project reference to `Naravel.Queue`; no external package references. The package is optional and is not pulled into `Naravel.Queue` itself.
- Verification performed: static inventory of every driver method and its cancellation-token usage; lifecycle tracing of static queue/lock/failed dictionaries; comparison of XML remarks and package guide; test coverage mapping.
- Verification NOT RUN: `dotnet restore`, `dotnet build`, `dotnet test`, live runtime stress, high-cardinality queue soak, heap-retention profiling, mutation testing and independent review.

## Executive assessment

- The package is correctly optional and stores queue state only in the current process. It honors basic queue semantics in the shared contract: priority, availability time, release, retry attempt propagation, failure removal and concurrent pop/ack.
- The current static state model intentionally makes two driver instances with the same connection name share their queue inside one process; the package guide explicitly documents that behavior. This is distinct from the separate finding that those static dictionaries have no reclamation lifecycle.
- Two material gaps remain: cancellation tokens are accepted but ignored by every driver operation; and queue/lock/failed-message state is kept in static dictionaries without cleanup. In particular, every permanently failed message is also copied into a private static dictionary that no method ever reads or clears.
- The constructor XML documentation says the state is private to the driver instance, which conflicts with the documented and implemented same-name cross-instance sharing.
- No runtime source, test, dependency or API was changed by this review.

## Findings

### [P2] MEM-001 — Static queue, lock and failed-message dictionaries have no bounded reclamation lifecycle

- Remediation state: OPEN.
- Architecture disposition: VETO AV-010 — process-local queue state must have a bounded lifecycle.
- Evidence status: CONFIRMED BY SOURCE-LEVEL LIFETIME ANALYSIS; heap/soak reproduction NOT RUN.
- Evidence: `src/Naravel.Queue.Memory/MemoryQueueDriver.cs:18-20` declares three static `ConcurrentDictionary` instances: `Queues`, `Locks` and `Failed`. `GetQueue` and `GetLock` add entries for every connection/queue pair at lines 28-30; `PopAsync` and `SizeAsync` call them even for an empty or misspelled queue. `AckAsync` and `FailAsync` remove messages from the queue list, but never remove the now-empty queue list or its lock. `FailAsync` additionally writes the full `QueuedMessage` to `Failed` at line 82. The private `Failed` dictionary has no read or removal path anywhere in this package.
- Affected contract: long-running processes that use many distinct queue names, and every process that handles permanently failed Memory-driver jobs.
- User impact: a queue name used once leaves a permanent dictionary entry and lock object even after the queue empties. Since `PopAsync` and `SizeAsync` create state on empty names, even probing arbitrary queue names grows the dictionaries. More seriously, every permanently failed job leaves its full envelope and payload reachable for the remainder of the process through the private `Failed` dictionary. The worker already records failed jobs through the separately registered `IFailedJobStore`; this private copy is not exposed for inspection/retry and has no cleanup operation.
- Trigger / failure scenario: a long-running process receives messages from a queue name derived from a tenant/request identifier, or probes a typo/unique queue name; the empty queue list and lock remain forever. Each permanent job failure also stores another full message in `Failed`, which cannot be listed or forgotten.
- Why existing tests do not catch it: `MemoryQueueDriverContractTests` uses one unique connection name and a finite set of unique queue names per test. It verifies behavior but not static dictionary cardinality or failed-envelope retention. The private `Failed` collection is not exposed to a test or caller.
- Required correction: give the process-local state an explicit, bounded lifecycle while preserving the documented same-connection-name sharing behavior. Remove the inaccessible duplicate failed-message store or make its lifecycle/inspection contract explicit and bounded. Retire empty queue/lock entries only when no operation can still be using them (for example, a reference-counted per-queue state object); do not naively remove a lock while another caller holds it.
- Regression tests required: exercise many unique queue names and repeated empty `PopAsync`/`SizeAsync` probes; fail many distinct messages; prove idle state and failed payload references can be reclaimed; concurrently pop/ack while cleanup runs; verify two driver instances with the same connection name still share the same queue until its safe retirement.
- Public API / PDR / release impact: an internal lifecycle correction is preferred; preserve the existing documented same-name sharing contract unless the owner approves a change.

### [P2] MEM-002 — Every Memory driver operation ignores its CancellationToken

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE SEARCH; behavioral cancellation reproduction NOT RUN.
- Evidence: `MemoryQueueDriver.PushAsync`, `PopAsync`, `AckAsync`, `ReleaseAsync`, `FailAsync` and `SizeAsync` all accept a `CancellationToken` but never read or check it (`src/Naravel.Queue.Memory/MemoryQueueDriver.cs:33-90`). By contrast, the core in-memory failed-job store checks cancellation before every operation. PDR-012's failed-job/storage API decision requires asynchronous and cancellable storage/dispatch operations.
- Affected contract: all public `IQueueDriver` operations for the Memory provider.
- User impact: a caller whose request or host token is already cancelled can still enqueue a message, reserve a job, delete a queued message, release or fail a job, or obtain a size result. This differs from the Redis, File and Database drivers, which pass the token into their I/O operations, and from `InMemoryFailedJobStore`, which throws before mutation.
- Trigger / failure scenario: call `PushAsync` with an already-cancelled token; the current implementation still inserts the message and returns a completed task. Similarly, `AckAsync` with a cancelled token still removes the message.
- Why existing tests do not catch it: the reusable `QueueDriverContractTests` covers queue semantics, but no test invokes each operation with an already-cancelled token or proves that no state mutation occurred.
- Required correction: check `cancellationToken.ThrowIfCancellationRequested()` before acquiring the queue lock or mutating/reading state in every operation. The in-memory operations are synchronous and short, so pre-operation cancellation is the relevant guarantee; do not add artificial asynchronous work.
- Regression tests required: for each operation, pass a pre-cancelled token and assert `OperationCanceledException` and unchanged queue/failed state. Include `PopAsync` to prove a cancelled pop does not reserve a message.
- Public API / PDR / release impact: behavior correction to match the existing `IQueueDriver` cancellation surface; no public API change.

### [DOC] MEM-003 — Constructor XML says queue state is private to an instance although same-name instances share it

- Remediation state: OPEN — DOCUMENTATION.
- Evidence status: CONFIRMED.
- Evidence: `src/Naravel.Queue.Memory/MemoryQueueDriver.cs:16-20` uses static dictionaries keyed by connection name and queue. The constructor XML at line 24 says the state is “private to this instance.” The maintained package README at `docs/nuget/Naravel.Queue.Memory.md:34` correctly states that two hosts in the same process which use the same store name share the queue.
- User impact: IDE-generated API documentation gives consumers the opposite impression from the package README and implementation; callers may incorrectly assume instance isolation.
- Recommended correction: change the constructor XML to say that the driver shares in-process state with other instances using the same connection name, and keep process/restart boundaries explicit.
- Regression / verification required: docs check and XML documentation review; no runtime change.

## Existing guarantees and verified coverage

- `PushAsync` queues work rather than running it inline; worker-based execution is consistent with the package summary and docs.
- Multiple queue connections are separated by connection name; different queue names are separated within a connection.
- Same-name driver instances share state inside the process, as the package guide explicitly documents.
- `PopAsync` reserves one currently available message and orders candidates by descending priority, then ascending availability timestamp.
- `ReleaseAsync` preserves the worker-supplied attempt count and error and resets reservation/availability.
- `FailAsync` removes the message from the live queue; the worker separately persists terminal failure details through `IFailedJobStore`.
- The shared contract test covers empty queues, push/pop/ack, delay, priority, release attempt propagation, failure removal, queue isolation and concurrent consumers.
- Not covered: cancellation behavior, static dictionary lifetime, concurrent state retirement, same-name cross-instance sharing regression.

## Test assessment

- Reviewed: `tests/Naravel.Queue.Tests/DriverTests.cs` and the shared contract base; the Memory provider contract runs with a fresh connection name but does not inspect retained static state.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Runtime/heap soak: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Risk summary and next action

- P0: none established.
- P1: none established.
- P2: MEM-001 (AV-010), MEM-002.
- DOC: MEM-003.
- Recommended next task: implement bounded state reclamation without breaking same-name sharing, and make all six operations honor pre-cancelled tokens; add the targeted tests before marking either finding closed.

## Plain-language explanation

### MEM-001 — Process-wide queue dictionaries keep old queue names and failed messages forever

- **In everyday words:** The Memory driver stores queue state in static dictionaries shared by the process. It creates entries even when a queue is only checked and never removes empty queue entries or its private failed-message copies.
- **A realistic example:** A service checks thousands of queue names created from tenant IDs. Even if those queues never receive a job, their dictionary entries and lock objects remain in memory. Failed jobs also remain in a private collection that callers cannot inspect or clear.
- **What we checked:** `src/Naravel.Queue.Memory/MemoryQueueDriver.cs`, around lines 18–30 and 82, defines the static dictionaries and adds queue/lock entries from normal operations. The report found no read or removal path for the private failed collection.
- **Why that proves the risk:** The dictionaries are static, entries are added as names are seen, and the package has no matching cleanup operation.
- **What is not yet proven here:** The lifetime problem is visible in source; no heap or long-running soak test was run.
- **Safe outcome and proof:** Process-local queue state and failed payloads need a safe bounded lifetime. Test many unique names, empty `PopAsync` / `SizeAsync` calls and permanent failures; prove old state can be reclaimed without breaking two live driver instances that share a connection name.

### MEM-002 — Memory queue operations ignore cancellation

- **In everyday words:** All queue methods accept a cancellation token but do not check it. A cancelled caller can still change queue state.
- **A realistic example:** A request is already cancelled, but `PushAsync` still adds the message. A cancelled `AckAsync` can still remove the job.
- **What we checked:** `src/Naravel.Queue.Memory/MemoryQueueDriver.cs`, across `PushAsync`, `PopAsync`, `AckAsync`, `ReleaseAsync`, `FailAsync` and `SizeAsync`. The report contrasts this with `InMemoryFailedJobStore`, which does check cancellation.
- **Why that proves the risk:** The driver never reads the token before it returns or mutates data.
- **What is not yet proven here:** Source inspection confirms the missing checks; no per-method cancellation test was run.
- **Safe outcome and proof:** Each operation must reject an already-cancelled token before it reads or changes queue state. Test all six methods and assert the queue and failed-job state remain unchanged.

### MEM-003 — The constructor documentation says queue state is private to one instance, but it is shared

- **In everyday words:** The XML documentation says each driver instance has its own queue, while the actual code shares queue state between instances with the same connection name.
- **A realistic example:** A developer creates two Memory drivers with the same connection name and expects separate queues because of the constructor description. In practice, both use the same in-process queue.
- **What we checked:** `src/Naravel.Queue.Memory/MemoryQueueDriver.cs` around lines 16–24, and the package README `docs/nuget/Naravel.Queue.Memory.md` around line 34.
- **Why that proves the mismatch:** The source uses shared static dictionaries, and the README correctly documents same-name sharing, but the constructor comment says the opposite.
- **What is not yet proven here:** This is a documentation mismatch; no new runtime behavior was alleged or tested.
- **Safe outcome and proof:** Correct the XML text to explain same-name sharing within one process and clarify that it does not survive process restart. Verify the generated API documentation and package README tell the same story.
