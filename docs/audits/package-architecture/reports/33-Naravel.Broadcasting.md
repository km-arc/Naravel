# Review Report — Naravel.Broadcasting

## Review metadata

- Package ID: `Naravel.Broadcasting`.
- Source project: `src/Naravel.Broadcasting/Naravel.Broadcasting.csproj`.
- Source-review baseline: `main` at `198b36f9e2753ccfb68354a02480cdc25e7eabc7`; this audit branch contains that commit.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source and contract review; independent second review NOT RUN.
- Status: REVIEWED WITH FINDINGS (2 P2; source inspection only).
- Files inspected: Broadcast contracts/message/manager; SignalR driver and hub; channel rules; presence store/service/tracker/heartbeat; registration; fake; `HubTests`, `PresenceHubTests`, `PresenceStoreTests`; README and guide..
- Dependencies: ASP.NET Core SignalR, `Naravel.Events` and `Naravel.Cache` abstractions. Cache is used for shared presence membership and locks..
- Verification performed: source-to-contract tracing and inspection of the listed tests and documentation.
- Verification NOT RUN: .NET restore/build/test, live provider or service execution unless explicitly stated below, full CI and mutation testing.

## Executive assessment

Protected channels require authentication and a matching rule. Presence lists are changed under a cache lock and can be shared between instances. Tests cover authorization, multiple connections, join/leave, expiry and concurrent joins. Two lifecycle gaps remain: re-subscribing on the same connection can announce a duplicate join; heartbeat interval does not follow a reloaded presence TTL.

## Package and dependency map

- Contracts: `IBroadcastable`, `IBroadcaster`, `IBroadcastDriver`.
- SignalR delivery: `BroadcastHub` and `SignalRBroadcastDriver`.
- Authorization: `ChannelRules`.
- Presence: `PresenceStore`, `PresenceService`, `PresenceTracker`, `PresenceHeartbeatService`.
- Cache is optional; local fallback warns when locks are not shared.

## Findings


### [P2] BRD-001 — Repeating Subscribe on one connection announces a duplicate join

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `BroadcastHub.Subscribe` calls `PresenceService.JoinAsync` for each request. `PresenceStore.JoinAsync` removes an existing entry with that connection ID before calculating whether the user is new. If it was the user's only connection, the subsequent check returns true and the hub sends `MemberJoined` again.
- Affected API/configuration: `BroadcastHub.Subscribe` for `presence-` channels.
- User impact: Other clients can receive duplicate join events for someone who never left.
- Trigger/example: A client calls `Subscribe` twice for the same presence channel without disconnecting.
- Why this proves the concern: The existing connection's only record is removed before checking for another connection with the same user ID.
- Existing test gap: `PresenceHubTests.Joining_returns_the_members_and_the_others_are_told_once_per_user` tests two distinct connections for one user, not two calls on the same connection.
- Required safe outcome: Make repeated subscription idempotent. It must not report a new member if this same connection was already subscribed.
- Regression tests required: Call `Subscribe` twice on one connection while another client listens; assert exactly one `MemberJoined`. Keep the existing two-connections/same-user test.
- Owner decision: Not required to record; required before runtime change only if public semantics change.

#### Plain-language explanation

**In everyday words:** Joining the same presence room twice from one connection can make the system announce that user twice.

**A realistic example:** Bob's client subscribes, then a UI reconnect handler repeats the call. Ann can receive another `MemberJoined` even though Bob did not leave.

**What we checked:** `BroadcastHub.Subscribe` calls `PresenceService.JoinAsync` for each request. `PresenceStore.JoinAsync` removes an existing entry with that connection ID before calculating whether the user is new. If it was the user's only connection, the subsequent check returns true and the hub sends `MemberJoined` again.

**Why that proves the concern:** The existing connection's only record is removed before checking for another connection with the same user ID.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Make repeated subscription idempotent. It must not report a new member if this same connection was already subscribed.

**How we will prove the fix:** Call `Subscribe` twice on one connection while another client listens; assert exactly one `MemberJoined`. Keep the existing two-connections/same-user test.


### [P2] BRD-002 — Presence heartbeat keeps the original interval after a TTL reload

- Remediation state: OPEN.
- Evidence status: CONFIRMED BY SOURCE; runtime reproduction NOT RUN.
- Exact source evidence: `PresenceStore.Ttl` reads `options.CurrentValue.PresenceEntryTtl` on every store operation. `PresenceHeartbeatService.ExecuteAsync` reads the TTL once at start and creates a `PeriodicTimer(ttl / 3)` which is never recreated. `BindOptions` registers a configuration reload token.
- Affected API/configuration: `Broadcasting:PresenceEntryTtl` configuration and `PresenceHeartbeatService`.
- User impact: After shortening the TTL, active users can expire from the presence list before the heartbeat timer refreshes them.
- Trigger/example: Service starts with 90-second TTL (30-second heartbeat); config reload changes TTL to 3 seconds, but timer still waits 30 seconds while entries expire after 3.
- Why this proves the concern: The store observes the new lifetime but the timer keeps using the old lifetime.
- Existing test gap: Presence store tests use fixed options and direct refresh; no hosted-service options reload test.
- Required safe outcome: Either update/recreate the heartbeat timer on options change or explicitly make this setting startup-only and stop promising hot reload.
- Regression tests required: Run the hosted heartbeat, change TTL from long to short and verify refresh occurs before entries expire; also test increasing TTL.
- Owner decision: Not required if hot reload remains supported; owner decision needed only if changing to startup-only.

#### Plain-language explanation

**In everyday words:** The presence list and heartbeat can use different timeout values after a settings reload. The list may expire users before the timer refreshes them.

**A realistic example:** A 90-second lifetime is changed to 3 seconds. The heartbeat still waits 30 seconds, so other clients may see an active user disappear.

**What we checked:** `PresenceStore.Ttl` reads `options.CurrentValue.PresenceEntryTtl` on every store operation. `PresenceHeartbeatService.ExecuteAsync` reads the TTL once at start and creates a `PeriodicTimer(ttl / 3)` which is never recreated. `BindOptions` registers a configuration reload token.

**Why that proves the concern:** The store observes the new lifetime but the timer keeps using the old lifetime.

**What is not yet proven:** the review inspected source and existing test code only. The behavior was not reproduced by running a test or a live service in this review.

**What should change:** Either update/recreate the heartbeat timer on options change or explicitly make this setting startup-only and stop promising hot reload.

**How we will prove the fix:** Run the hosted heartbeat, change TTL from long to short and verify refresh occurs before entries expire; also test increasing TTL.


## Test assessment

- Existing tests inspected: `HubTests`, `PresenceHubTests`, `PresenceStoreTests` and other broadcasting unit tests..
- Coverage seen in source: anonymous and protected joins, authorization failure, event bridge, multiple connections for one user, disconnect, unsubscribe, expiry, refresh, parallel joins..
- Gaps: same-connection repeated Subscribe; hosted heartbeat behavior after settings reload; real multi-process Redis/Memcached/SignalR cluster.
- Tests actually run for this review: NONE.
- Build/restore: NOT RUN.
- External service/provider integration: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Documentation and packaging assessment

Guide accurately requires rules for protected channels and warns that process-local locks do not share presence across instances.

## Risk summary and next action

- P2: BRD-001, BRD-002.
- P0 / P1 / P3 / OBS / DOC: none recorded.

Recommended next task: make repeated presence joins idempotent and keep heartbeat timing synchronized with the configured entry lifetime. Do not change runtime code in this audit without owner approval.

## Plain-language explanation

This package broadcasts events with SignalR and manages members of presence rooms. Source and tests were inspected; no build, tests or real multi-process SignalR cluster ran.
