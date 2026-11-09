# Review Report — Naravel.Broadcasting

## Review metadata

- Package ID: Naravel.Broadcasting.
- Source project: src/Naravel.Broadcasting/Naravel.Broadcasting.csproj.
- Baseline: main at d3f4342db8aa7d0f8085d1205f76f6ae5bfa8ba3.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, security, concurrency and test review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; distributed cache/lock verification pending).
- Files inspected: hub, channel names/rules, options and registration, SignalR/log/null drivers, broadcaster, presence service/store/heartbeat/tracker/member, fake, hub/presence/channel tests and docs.
- Dependencies: ASP.NET Core SignalR and Naravel.Cache abstractions; the core package does not own a Redis/Memcached client.
- Verification performed: protected-channel authorization, group join/leave, presence lifecycle, cached list updates, lock usage, time-based expiry and telemetry reviewed.
- Verification NOT RUN: restore, build, SignalR tests, real Redis/Memcached presence concurrency, slow-cache lock-expiry reproduction, mutation testing and independent review.

## Executive assessment

The package has an explicit authorization boundary: public channels are open, while private and presence channels require an authenticated user and a matching rule. Missing or throwing rules deny the subscription. Presence members are kept in a shared cache list and refreshed; closed connections are removed, with expiry as a fallback when cleanup fails.

One concurrency risk remains in the presence list update. The store holds a distributed lock with a fixed 10-second lifetime while it awaits cache reads and writes. If the cache operation takes longer than that, the lock can expire while the first update is still running. A second server can then acquire the same lock and perform its own read/change/write at the same time. This relates directly to the repository's existing distributed-lock architecture veto AV-002.

## Package and dependency map

- Main service: BroadcastManager and DriverBroadcaster.
- Hub and authorization: BroadcastHub, ChannelRules and channel-name validation.
- Drivers: SignalR, log and null broadcasters.
- Presence: PresenceService, PresenceStore, PresenceHeartbeatService and PresenceTracker.
- Test support: BroadcastFake.
- Dependencies: ASP.NET Core SignalR and Naravel.Cache abstractions; optional Cache providers supply shared storage/locks.
- Resource ownership: SignalR connections are owned by ASP.NET Core; the package tracks presence by connection ID and expiry.

## Findings

### [P2] BRC-001 — A presence-list update can outlive its 10-second lock

- Remediation state: OPEN.
- Architecture disposition: related to AV-002 — the protected update must remain mutually exclusive for its entire lifetime; a fixed lease alone is not enough unless the operation is bounded or fenced.
- Evidence status: SEEN IN SOURCE; slow-cache interleaving reproduction NOT RUN.
- Evidence: WithLockAsync in src/Naravel.Broadcasting/PresenceStore.cs acquires the per-channel lock using TimeSpan.FromSeconds(10), then awaits the supplied body. JoinAsync and LeaveAsync call LoadAsync (cache TryGetAsync) and SaveAsync (cache SetAsync/RemoveAsync) inside that body. There is no renewal or fencing check before the write. Existing PresenceStoreTests use an in-memory cache and MemoryLock, including 30 parallel joins, but do not pause a cache call beyond 10 seconds.
- In everyday words: The presence list is protected by a lock that lasts 10 seconds. If a cache read or write takes longer, the lock can expire before the update is finished, allowing another server to edit the same list at the same time.
- Realistic example: Server A takes the channel lock and pauses while Redis is slow. Ten seconds pass. Server B takes the now-expired lock, reads the old list and adds a member. Server A then resumes and saves its older copy, overwriting B's new member.
- Why the evidence proves the risk: the code awaits remote cache operations while holding a lock whose expiry is fixed and not extended. After expiry, the lock no longer prevents another owner from entering. The source does not check that the original owner still holds the lock before saving.
- What is not proven: this is a source-level interleaving analysis. The audit did not run a delayed Redis test or reproduce data loss with two live servers.
- Required safe outcome: two updates to one channel must never overwrite one another, even when a cache call is slow. Either enforce a strict operation deadline shorter than the lock, renew/fence the lease, or use an atomic backend update. A stale holder must not save after another holder has taken over.
- Regression tests required: use two PresenceStore instances over a controllable shared cache/lock. Pause the first read or write until after 10 seconds, let the second instance join/leave, resume the first and prove both correct state and membership notifications are preserved. Include cancellation, cache timeout and real Redis/Memcached provider tests.
- Public API / PDR / release impact: distributed presence correctness; linked to AV-002. Do not claim cross-instance consistency for a provider unless its lock guarantees support the required critical section.

## Existing guarantees and coverage

- Protected channel subscription requires authentication and an allow-listed channel rule.
- A missing or throwing authorization rule denies access.
- Presence identity is based on an explicit user ID and supports multiple connections for one user.
- Each connection record has its own expiry; heartbeat refreshes known connections.
- Disconnected connections are removed, and an entry expires if cleanup cannot complete.
- The package warns when a process-local lock is used with multiple instances; in that setup the presence list is not globally shared.
- Tests cover authorization, presence joining/leaving, multiple connections, refresh/expiry, parallel joins and SignalR hub behavior. They were inspected but not executed here.

## Test assessment

- Tests inspected: HubTests, PresenceHubTests, PresenceStoreTests, ChannelRulesTests, BroadcasterTests, PresenceRedisTests and PresenceMemberJsonTests.
- Missing test: slow cache operation crossing the fixed 10-second lock lifetime.
- Tests actually run in this review: NONE.
- Live Redis/Memcached, build/CI, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The guide explains public/private/presence channels, authorization and presence events.
- It says a shared cache/lock is required for presence state to be consistent across instances.
- The source's 10-second lock lifetime is not presented as an operator-configurable upper bound for the cache operations.

## Risk summary and next action

- P2: BRC-001, related to AV-002.
- Recommended next task: define and test the safe lifetime/atomicity rule for presence-list updates before relying on this feature across application instances. No runtime change is authorized by this audit.

## Plain-language explanation

Broadcasting checks access before a client joins a private or presence channel. It also keeps track of connected users in a shared cache. The remaining risk is the lock around updates: the lock lasts ten seconds, but the cache operations inside it have no matching deadline or renewal. A slow cache call could let another server update the same list at the same time. The audit found the risk in source; it did not reproduce it against a live Redis or Memcached service.
