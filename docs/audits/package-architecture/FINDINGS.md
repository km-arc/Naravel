# Package Audit Findings and Remediation Tracker

Baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9` (2026-11-08 post-Revert second-pass revalidation baseline).

## State definitions

- `OPEN`: evidence recorded; no fix authorized or performed.
- `APPROVED FOR REMEDIATION`: owner explicitly authorized a separate fix task/PDR.
- `IN REMEDIATION`: approved fix is in progress on its own task/branch.
- `FIXED — WAITING VERIFICATION`: a change exists, but required tests/CI have not been proven green.
- `VERIFIED CLOSED`: regression tests and required CI/roadmap checks passed; evidence links are recorded.
- `ACCEPTED RISK`: owner explicitly accepted the risk and rationale is recorded.

An audit report does not itself fix a finding. A finding remains OPEN until the owner authorizes a separate remediation task. Never mark a finding closed merely because a commit exists.

A prior PDR approval is not a waiver from this architecture review. Items in the veto register below are not acceptable final-state trade-offs and must not be moved to ACCEPTED RISK solely because an older PDR or document described them as intentional. A veto can be superseded only by an explicit later architecture decision with a documented rationale; code changes still require regression tests and verification.

## Architecture vetoes

The following are vetoed as target architecture. AV-001 and AV-002 are P1 release blockers; AV-003 through AV-008 are mandatory P2 architecture work before the platform can be considered production-grade, reliable and scalable.

| Veto ID | Severity | Required architectural outcome | Related findings |
|---|---:|---|---|
| AV-001 | P1 | Store/scope flush must remain within its namespace and report partial/unavailable invalidation as failure; no generic cache flush may call cluster-wide Memcached FlushAll. | CCH-001, CRD-002, MCD-006 |
| AV-002 | P1 | Distributed locks and CacheRateLimiter must preserve mutual exclusion across lease expiry and stale-owner release; use atomic ownership/fencing or do not expose the backend as a correctness-grade lock. | CCH-004, MCD-007 |
| AV-003 | P2 | Tag metadata must have a safe bounded lifecycle; it must not grow forever or be expired in a way that resurrects old values. | CCH-008 |
| AV-004 | P2 | No synchronous network I/O on request-path threads; Redis connection opening and Memcached counter operations must be genuinely asynchronous. | RED-002, MCD-009 |
| AV-005 | P2 | Named Memcached stores support independent clusters while sharing clients for identical normalized topologies and disposing each client exactly once. | MCD-008 |
| AV-006 | P2 | Late resource disposal failures remain observable without unobserved background exceptions or skipped cleanup. | FND-004 |
| AV-007 | P2 | Cache stores and locks expose low-cardinality operational telemetry without keys, values, subjects, tokens or secrets. | CCH-007 |
| AV-008 | P2 | Positive TTLs must not become infinite because a provider truncates sub-second durations to zero. | MCD-010 |

Veto status is separate from finding lifecycle status. Related findings remain OPEN; a veto is cleared only after its required design is implemented, regression tests and relevant CI pass, and verification evidence is recorded. Historical PDR approvals remain in the files as design history, but veto sections in the PDRs supersede them for the target architecture.

## Findings

| ID | Package | Severity | Finding | State | Evidence / report |
|---|---|---:|---|---|---|
| FND-001 | Naravel.Foundation | P2 | Configuration fingerprint can collide | OPEN | [Report](reports/01-Naravel.Foundation.md) |
| FND-002 | Naravel.Foundation | P2 | Sync Driver race with async factory replacement | OPEN | [Report](reports/01-Naravel.Foundation.md) |
| FND-003 | Naravel.Foundation | P2 | Throwing cancellation callback can abort manager cleanup | OPEN | [Report](reports/01-Naravel.Foundation.md) |
| FND-004 | Naravel.Foundation | P2 | Late-created driver disposal errors are swallowed without diagnostics (AV-006) | OPEN | [Report](reports/01-Naravel.Foundation.md) |
| CCH-001 | Naravel.Cache | P1 | Scoped flush clears the backing store beyond the scope (AV-001) | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-002 | Naravel.Cache | P2 | Distinct tag sets can generate the same cache key | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-003 | Naravel.Cache | P2 | Production expiry/lock/rate-limit paths bypass TimeProvider | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-004 | Naravel.Cache | P1 | Fixed 30-second rate-limit lock lease can expire mid-operation (AV-002) | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-005 | Naravel.Cache | P3 | RememberAsync does not validate a null non-token factory | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-006 | Naravel.Cache | DOC | Cache guide quickstart uses DateTime.UtcNow | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-007 | Naravel.Cache | P2 | Cache/Lock telemetry gap and unbounded caller-controlled limiter metric tags (AV-007) | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-008 | Naravel.Cache | P2 | Per-tag version metadata has no safe bounded lifecycle (AV-003) | OPEN | [Report](reports/02-Naravel.Cache.md) |
| RED-001 | Naravel.Redis | P2 | FromConnectionString can collide with a configured implicit-prefixed name | OPEN | [Report](reports/03-Naravel.Redis.md) |
| RED-002 | Naravel.Redis | P2 | Synchronous first connection blocks the first caller (AV-004) | OPEN | [Report](reports/03-Naravel.Redis.md) |
| RED-003 | Naravel.Redis | OBS | The raw connection string is retained by a long-lived factory closure | OPEN — OWNER DECISION | [Report](reports/03-Naravel.Redis.md) |
| CRD-001 | Naravel.Cache.Redis | P2 | Invalid Database configuration silently falls back to the connection default | OPEN | [Report](reports/04-Naravel.Cache.Redis.md) |
| CRD-002 | Naravel.Cache.Redis | P2 | Redis FlushAsync skips disconnected endpoints and may report success without flushing | OPEN | [Report](reports/04-Naravel.Cache.Redis.md) |
| CRD-003 | Naravel.Cache.Redis | P2 | Cancellation tokens are checked only before Redis I/O, not while awaiting it | OPEN | [Report](reports/04-Naravel.Cache.Redis.md) |

| MCD-001 | Naravel.Cache.Memcached | P2 | Generic value-type cache misses can be reported as hits | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-002 | Naravel.Cache.Memcached | P2 | Failed writes and lock acquisition can be reported as success/contention | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-003 | Naravel.Cache.Memcached | P2 | Unsigned Memcached counter semantics corrupt the signed `long` contract | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-004 | Naravel.Cache.Memcached | P2 | Cancellation after async I/O starts does not cancel the caller's wait | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-005 | Naravel.Cache.Memcached | P3 | Null nullable-integral values are stored as empty text and cannot be read | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |

| MCD-006 | Naravel.Cache.Memcached | P1 | Store-level FlushAsync clears the entire shared Memcached cluster (AV-001) | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-007 | Naravel.Cache.Memcached | P1 | Memcached lock release can delete a different owner's lock (AV-002) | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-008 | Naravel.Cache.Memcached | P2 | All named Memcached stores are forced onto one server pool (AV-005) | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-009 | Naravel.Cache.Memcached | P2 | Synchronous counter I/O is queued onto the shared ThreadPool (AV-004) | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-010 | Naravel.Cache.Memcached | P2 | Positive sub-second TTLs become no-expiry in Enyim (AV-008) | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-011 | Naravel.Cache.Memcached | OBS | Explicit zero-TTL semantics differ between the Memcached adapter and CacheFake; public contract needs owner decision | OPEN — OWNER DECISION | [Report](reports/05-Naravel.Cache.Memcached.md) |

## Remediation log

No remediation has been authorized or performed by the package audit branch. Once the owner approves a finding, record the fix PR/commit, regression test names, verification results, and the date/state transition here.

## Owner decisions

See [OWNER-DECISIONS.md](OWNER-DECISIONS.md). MCD-011 is intentionally an observation, not a veto, until the owner defines the public zero-TTL contract.
