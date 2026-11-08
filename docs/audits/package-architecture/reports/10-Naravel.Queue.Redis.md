# Review Report — Naravel.Queue.Redis

## Review metadata

- Package ID: §Naravel.Queue.Redis§ (SDK default from project/assembly name; no explicit §PackageId§).
- Source project: §src/Naravel.Queue.Redis/Naravel.Queue.Redis.csproj§.
- Source-review baseline: §main§ at §852db2c3c1be593a32fdce5318c18d0cd31149be§.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source, Lua control-flow, provider-test and documentation review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; Redis service verification not run in this audit).
- Files inspected: package project/references; shipped/unshipped API; RedisQueueDriver and all Lua scripts/key builders; RedisFailedJobStore; RedisBatchRepository and DI registration; IQueueDriver/IBatchRepository/IFailedJobStore; Redis atomicity proxy tests; real-Redis provider contract; batch/failed-store persistence tests; Queue/Redis guides; PDR-010 and PDR-012.
- Dependency map: references Naravel.Queue and StackExchange.Redis (provided through central package management); borrows the multiplexer from Naravel.Redis in the normal DI registration. It does not own the connection by default.
- Verification performed: Lua key/argument mapping review; enqueue, delayed promotion, reservation expiry/reclaim and fencing control flow; Redis batch hash/index lifecycle; failed-list access search; SizeAsync/cancellation review; provider test coverage mapping; Redis Cluster boundary check against PDR-010.
- Verification NOT RUN: §dotnet restore§, §dotnet build§, §dotnet test§, live Redis service integration, Redis Cluster topology test, Redis failure-injection tests, high-cardinality retention soak, mutation testing and independent review.

## Executive assessment

- The core Redis queue uses one Lua script for Push and one Lua script for Pop (promotion, expired reservation reclaim, priority selection and reservation); ack, release and fail are also token-aware scripts. This removes multi-command races for the primary queue state transitions.
- The provider contract includes a real-Redis stale-worker test: once a reservation expires and is reclaimed, a previous worker cannot ack, release or fail the new owner's copy. Redis atomicity proxy tests also assert that the expected scripts and reservation tokens are passed.
- Four concrete gaps remain: zero/negative visibility timeout is accepted; SizeAsync ignores cancellation; Redis batch registration writes the hash and global index in separate commands; and the queue driver writes an additional failed-message list that no code in the package reads or clears.
- Persistent batch state has no retention/removal API; every worker startup scans the full lifetime index. The exact guarantee for in-flight cancellation of Redis commands is also not documented: current calls check the token before issuing a command, but StackExchange.Redis operations do not accept the token.
- Redis Cluster is intentionally outside PDR-010's supported scope. The queue scripts use multiple keys without a Redis hash-tag, so they are not cross-slot safe; the package README should state that this provider is not Redis Cluster compatible rather than implying any StackExchange.Redis topology works.
- No runtime code, tests, dependency or public API was changed by this review.

## Findings

### [P2] RQR-001 — Non-positive visibility timeout makes reservations immediately reclaimable

- Remediation state: OPEN.
- Architecture disposition: VETO AV-012 — reclaim-capable drivers must reject non-positive visibility timeouts.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; duplicate-delivery reproduction NOT RUN.
- Evidence: §src/Naravel.Queue.Redis/RedisQueueDriver.cs:43-48§ stores the public constructor's §visibilityTimeout§ without validation. §PopScript§ at lines 77-118 reclaims every reserved item whose deadline score is less than or equal to the current time. The DI registration at lines 270-275 parses §VisibilityTimeoutSeconds§ and passes §TimeSpan.FromSeconds§ directly to the constructor.
- Affected contract: Redis queue reservation/visibility behavior when the constructor or configured §VisibilityTimeoutSeconds§ is zero or negative.
- User impact: with a zero/negative value, the next PopAsync can reclaim a reservation before the first worker has finished. A second worker receives the same message and may run its side effects concurrently. The reservation token only fences the stale worker's later settlement; it does not prevent the duplicate execution caused by the invalid timeout.
- Trigger / failure scenario: configure §VisibilityTimeoutSeconds = "0"§; worker A reserves a job; worker B calls PopAsync while A is still processing. The reservation deadline is at or before the current time, so PopScript moves the job back to a ready list and can immediately reserve it for B.
- Why existing tests do not catch it: real Redis fencing tests use a positive one-second timeout and the shared contract uses a positive 30-second timeout. There are no zero/negative constructor or configuration tests.
- Required correction: reject non-positive values in the public constructor and at the configuration boundary. The error should identify the store and §VisibilityTimeoutSeconds§. Keep the positive 300-second default.
- Regression tests required: constructor rejects zero and negative values; DI rejects zero and negative configuration; a positive short timeout is reclaimed only after its deadline; two workers cannot reserve the same live message.
- Public API / PDR / release impact: fail-fast validation; no public API change expected.

### [P2] RQR-002 — SizeAsync ignores its CancellationToken entirely

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: §src/Naravel.Queue.Redis/RedisQueueDriver.cs:251-259§ begins by resolving §Db§ and then issues ten sequential §ListLengthAsync§ calls and one §SortedSetLengthAsync§ call. It neither checks §cancellationToken§ before the first Redis request nor applies a cancellable wait to the operations. All the other RedisQueueDriver public methods call §ThrowIfCancellationRequested§ before issuing their script.
- Affected contract: §IQueueDriver.SizeAsync§ for Redis under caller cancellation or host shutdown.
- User impact: a pre-cancelled caller still triggers up to eleven Redis round trips and receives a count rather than §OperationCanceledException§. A large or slow Redis request can keep doing work after the caller has cancelled.
- Trigger / failure scenario: call §SizeAsync(queue, alreadyCancelledToken)§. The method continues to query every ready list and the delayed sorted set because it never inspects the token.
- Why existing tests do not catch it: the shared queue contract calls SizeAsync with CancellationToken.None and checks only normal counts; no Redis test invokes SizeAsync with a cancelled token.
- Required correction: check §cancellationToken.ThrowIfCancellationRequested()§ before resolving/querying Redis. Decide and document separately whether cancellation after a Redis command has been sent cancels only the caller's wait or is not observable until the command completes; StackExchange.Redis does not expose a cancellation-token overload for these commands.
- Regression tests required: pre-cancelled token throws before any Redis request; normal size still returns ready+delayed count; cancellation semantics for in-flight commands are covered according to owner decision AD-020.
- Public API / PDR / release impact: implementation correction and cancellation-contract clarification; no public API change.

### [P2] RQR-003 — FailScript writes an additional failed list that has no read or removal path

- Remediation state: OPEN — OWNER DECISION.
- Evidence status: CONFIRMED BY SOURCE SEARCH AND CONTROL-FLOW ANALYSIS.
- Evidence: §src/Naravel.Queue.Redis/RedisQueueDriver.cs:68§ defines §FailedKey(queue)§ as §netqueue:{queue}:failed§. §FailScript§ at lines 160-171 removes the active message from the hash/reservation set and unconditionally §RPUSH§es the full serialized envelope onto this failed list. A repository-wide source search finds no code path that reads, trims or deletes this key. Separately, §RedisFailedJobStore§ stores failures in its own hash (default §netqueue:failed-jobs§) and supports List/Forget/Flush. The NuGet README says applications should add §AddRedisFailedJobStore()§ to make the FailedJobManager list persistent.
- Affected contract: terminal failure storage/retention for Redis queue messages, with or without the optional RedisFailedJobStore.
- User impact: every terminally failed job also leaves a full payload/error envelope in an undocumented Redis list that the public API cannot inspect or clear. The list grows indefinitely. When RedisFailedJobStore is registered, the same failed job is stored a second time under a separately managed key; flushing/forgetting the official store does not clear the per-queue list.
- Trigger / failure scenario: a job fails permanently and the worker calls TryFailAsync. The Lua script pushes the envelope into §netqueue:{queue}:failed§. Calling §FailedJobManager.ForgetAsync§ or §FlushAsync§ only changes the configured §IFailedJobStore§; it never removes the driver's list entry.
- Why existing tests do not catch it: real Redis tests cover only RedisFailedJobStore's hash-based List/Forget/Flush behavior. No test checks the per-queue §FailedKey§ list or its lifetime.
- Owner decision required: is this list intended as an administrative dead-letter list (to be documented with a supported read/retention/cleanup path), or is it obsolete duplicate storage and should be removed in favor of §IFailedJobStore§?
- Architect recommendation: use §IFailedJobStore§ as the supported failed-job source of truth. Remove the inaccessible duplicate list unless the owner explicitly wants a Redis-native dead-letter queue; if retained, expose/document its retrieval and bounded cleanup semantics and define how it relates to RedisFailedJobStore.
- Regression tests required: permanently fail a message with and without RedisFailedJobStore; assert exactly which Redis keys retain it; verify Forget/Flush or the new administrative retention path removes all intended copies; stress many failed messages and prove bounded/operable retention.
- Public API / PDR / release impact: failure-storage contract and potentially breaking on-disk/keyspace behavior; owner decision AD-018 required.

### [P2] RQR-004 — Redis batch registration can leave a hash invisible to callback recovery

- Remediation state: OPEN.
- Architecture disposition: related to AV-009 — batch registration and recovery must not leave an unreachable pending batch.
- Evidence status: CONFIRMED BY SOURCE-LEVEL CONTROL-FLOW ANALYSIS; second-command failure reproduction NOT RUN.
- Evidence: §src/Naravel.Queue.Redis/RedisBatchRepository.cs:45-66§ first calls §HashSetAsync(BatchKey(batch.Id), ...)§ to write the batch state and callbacks, then separately calls §SetAddAsync(_index, batch.Id)§ to make the ID discoverable by §ResumePendingCallbacksAsync§. These writes are not in one Redis transaction or Lua script. §ResumePendingCallbacksAsync§ at lines 116-127 only scans members of the index set.
- Affected contract: §IBatchRepository.RegisterAsync§ when the first write succeeds but adding the batch ID to the index fails or the connection is interrupted.
- User impact: the batch hash remains in Redis, but the ID is absent from the index used for startup callback recovery. Since §JobDispatcher.BatchAsync§ awaits RegisterAsync before publishing any members and returns the batch ID only after the entire call finishes, the caller receives an exception without an accessible recovery handle. The orphan batch remains permanently pending and is never visited by ResumePendingCallbacksAsync.
- Trigger / failure scenario: §HashSetAsync§ succeeds; §SetAddAsync§ fails because the connection drops or the command errors. The hash has TotalJobs/callback data, the index lacks the ID, and no job messages have yet been enqueued.
- Why existing tests do not catch it: §RedisBatchRepositoryTests.Batch_state_survives_repository_recreation§ covers only successful registration and manually deletes the hash and index member at the end. There is no failure injection between HashSet and SetAdd.
- Required correction: write the batch hash and index membership atomically (for example with one Lua script/transaction on supported topology), or implement a deterministic orphan-discovery/reconciliation strategy. The failure path must expose the batch identity and must not silently leave an unindexed pending hash. Preserve the cross-process callback recovery contract.
- Regression tests required: inject failure after the hash write but before index membership; recreate the repository; prove the orphan is discoverable and reaches an explicit recoverable/terminal state; test cancellation and retry around the registration boundary; retain the happy-path recreation test.
- Public API / PDR / release impact: implementation/storage reliability correction; no public API change necessarily required. This also reinforces QUE-001/AV-009.

### [OBS — OWNER DECISION] RQR-005 — The Redis batch index has no retention/removal lifecycle and is scanned at every worker startup

- Remediation state: OPEN — OWNER DECISION.
- Evidence status: CONFIRMED BY SOURCE.
- Evidence: §RedisBatchRepository.RegisterAsync§ adds every batch ID to a persistent Redis set (§_index§); no method removes an ID or deletes its batch hash after callbacks complete. §IBatchRepository§ has no Forget/Delete/Prune member. §ResumePendingCallbacksAsync§ retrieves every ID with §SetMembersAsync(_index)§ and performs a §HashGetAllAsync§ for each one on every worker startup, including batches whose Then/Catch/Finally flags are already complete.
- Affected contract: long-running applications that create many persistent batches over time.
- User impact: the index and batch hashes grow monotonically; worker startup work and Redis round trips grow with the total historical batch count, not just unfinished batches. A deployment with a large batch history can increasingly delay worker startup and generate significant Redis traffic.
- Owner decision required: define whether persistent batch history is intentionally retained forever, or whether the repository needs a retention/pruning contract and a minimum audit-history duration.
- Architect recommendation: retain completed batch state only for an explicitly configurable/documented period or until an application-controlled Forget/Prune operation; keep unfinished batches and callback-pending batches protected from deletion. Do not add a blind TTL to the batch hash while the index still references it.
- Regression tests required: create many terminal batches, apply the approved retention policy, recreate the repository and prove pending callbacks remain recoverable while old completed records are safely reclaimed.
- Public API / PDR / release impact: potential additive repository API/configuration and retention semantics; owner decision AD-019 required.

### [OBS — OWNER DECISION] RQR-006 — Redis command cancellation is preflight-only once a StackExchange.Redis operation has been issued

- Remediation state: OPEN — OWNER DECISION / CONTRACT CLARIFICATION.
- Evidence status: CONFIRMED BY SOURCE AND CLIENT API SHAPE.
- Evidence: Push, Pop, Ack, Release and TryFail call §ThrowIfCancellationRequested§ before invoking §IDatabase.ScriptEvaluateAsync§; the command itself has no CancellationToken overload. RedisBatchRepository and RedisFailedJobStore similarly check before calling Redis methods but cannot pass the token to the in-flight command. SizeAsync currently does not even perform the preflight check (RQR-002).
- Affected contract: Queue Redis driver, persistent batch repository and Redis failed-job store when the caller cancels after a Redis command has started or while Redis is unavailable.
- User impact: cancellation requested after command issuance does not stop the caller's wait, and the Redis command may still commit. The caller may remain blocked until the connection timeout and cannot infer that the operation was rolled back. A blind WaitAsync wrapper would cancel only the caller's wait; it would not undo a command already sent to Redis.
- Owner decision required: define the public semantics as (a) cancellation is a preflight check only once an atomic Redis command is sent, with that limitation documented, or (b) cancellation stops the caller's wait but the operation may still complete server-side, with an explicit indeterminate-outcome contract.
- Architect recommendation: (a) for mutating queue operations, because returning cancellation while an already-sent command may still mutate state can create misleading retry/duplicate behavior. Check the token before each separate command and make multi-command sequences atomic where possible. Document the remote-command limitation.
- Regression tests required: pre-cancelled calls send no Redis commands; cancellation between separate non-atomic commands is not silently ignored; if (b) is chosen, tests prove that caller cancellation can occur while server-side completion remains possible and the result is treated as indeterminate.
- Public API / PDR / release impact: cancellation-contract clarification; owner decision AD-020 required.

### [DOC] RQR-007 — Redis Queue package README does not state that Cluster is unsupported by its multi-key Lua scripts

- Remediation state: OPEN — DOCUMENTATION.
- Evidence status: CONFIRMED BY SOURCE AND APPROVED PDR SCOPE.
- Evidence: §RedisQueueDriver.PopAsync§ executes one Lua script with 13 keys (messages, delayed, reserved and ten priority lists); Push/Release/Fail also execute multi-key scripts. The key builder creates ordinary §netqueue:{queue}:...§ names without a literal Redis hash-tag, so these keys do not share one Redis Cluster hash slot and the script will fail with CROSSSLOT on a cluster. §docs/en/redis.md§ and PDR-010 explicitly put Redis Cluster/Sentinel support outside the approved module scope, but §docs/nuget/Naravel.Queue.Redis.md§ does not surface this limitation on the provider package page.
- Affected contract: deployment of §Naravel.Queue.Redis§ against Redis Cluster.
- User impact: a consumer can pass a cluster connection string that the underlying multiplexer recognizes, but queue operations fail when the Lua script spans multiple hash slots. The module's PDR explicitly excludes cluster support; the risk is the package README does not make this boundary clear to the person choosing the provider.
- Recommended correction: add a brief "Redis Cluster is not supported by this queue driver" note to the Redis Queue package README and link the broader Redis topology decision. Do not imply that the underlying StackExchange.Redis client's cluster capability automatically makes this driver's multi-key scripts cluster-compatible.
- Regression / verification required: documentation check. A live cluster test is only required if the owner later approves cluster support; a future implementation would need a hash-slot design and a multi-node integration suite.
- Public API / PDR / release impact: documentation-only within the approved PDR-010 non-goal.

## Existing guarantees and verified coverage

- Push uses one Lua script to write the envelope hash and choose ready-list versus delayed-sorted-set state atomically.
- Pop uses one Lua script to promote due messages, reclaim expired reservations, select the highest-priority ready list, reserve the message and stamp a fresh ReservationId.
- Ack/Release/Fail compare the stored ReservationId when the caller has one; the real-Redis provider test proves a stale worker cannot settle a message after another worker reclaimed it.
- RedisQueueAtomicityTests assert the use of atomic scripts and reservation tokens at the proxy boundary.
- RedisFailedJobStore has a real-Redis test for persistence across store recreation and the public List/Forget/Flush operations.
- RedisBatchRepository has a real-Redis happy-path test for persistence across repository recreation and cancellation; the test manually deletes the hash and index entry because the public repository exposes no removal API.
- Redis Cluster is an explicit non-goal in PDR-010; no Cluster integration test is claimed or expected under the current decision.

## Test assessment

- Reviewed: §tests/Naravel.Queue.Providers.Tests/RedisQueueAtomicityTests.cs§, RedisQueueProviderTests shared contract and stale-worker test, RedisFailedJobStoreTests, RedisBatchRepositoryTests, §tests/Naravel.Queue.Providers.Tests/QueueProviderContractTests.cs§ and Redis/Queue docs.
- Missing regression coverage: zero/negative visibility timeout; cancelled SizeAsync; failed-list retention/inspection; failure between Redis batch hash and index writes; persistent-batch retention policy; in-flight command cancellation contract; package README's Redis Cluster boundary.
- Tests run for this review: NONE.
- Build/CI: NOT RUN by this audit session.
- Live Redis service: NOT RUN by this audit session.
- Redis Cluster topology: NOT RUN (explicitly out of scope in PDR-010).
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Risk summary and next action

- P1: none established in this package review.
- P2: RQR-001 (AV-012), RQR-002 (SizeAsync cancellation), RQR-003 (unreadable/unbounded failed list), RQR-004 (partial batch index registration).
- OBS / owner decisions: RQR-005 (batch retention), RQR-006 (in-flight cancellation semantics).
- DOC: RQR-007 (explicit Redis Cluster non-support on package page).
- Recommended next task: obtain AD-018 through AD-020; add tests for positive visibility, SizeAsync cancellation and the registration failure boundary; then decide whether to remove/expose the per-queue failed list and implement a safe batch history retention policy. Do not change runtime code or the Redis key layout during this audit pass.
