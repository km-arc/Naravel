# Package Audit Findings and Remediation Tracker

Baseline: `main` at `a69e0becca197ee8dc14d2a576b45dd763263833` (2026-11-08 synchronization baseline).

## State definitions

- `OPEN`: evidence recorded; no fix authorized or performed.
- `APPROVED FOR REMEDIATION`: owner explicitly authorized a separate fix task/PDR.
- `IN REMEDIATION`: approved fix is in progress on its own task/branch.
- `FIXED — WAITING VERIFICATION`: a change exists, but required tests/CI have not been proven green.
- `VERIFIED CLOSED`: regression tests and required CI/roadmap checks passed; evidence links are recorded.
- `ACCEPTED RISK`: owner explicitly accepted the risk and rationale is recorded.

An audit report does not itself fix a finding. A finding remains OPEN until the owner authorizes a separate remediation task. Never mark a finding closed merely because a commit exists.

## Findings

| ID | Package | Severity | Finding | State | Evidence / report |
|---|---|---:|---|---|---|
| FND-001 | Naravel.Foundation | P2 | Configuration fingerprint can collide | OPEN | [Report](reports/01-Naravel.Foundation.md) |
| FND-002 | Naravel.Foundation | P2 | Sync Driver race with async factory replacement | OPEN | [Report](reports/01-Naravel.Foundation.md) |
| FND-003 | Naravel.Foundation | P2 | Throwing cancellation callback can abort manager cleanup | OPEN | [Report](reports/01-Naravel.Foundation.md) |
| FND-004 | Naravel.Foundation | OBS | Late-created driver disposal errors are swallowed | OPEN — OWNER DECISION | [Report](reports/01-Naravel.Foundation.md) |
| CCH-001 | Naravel.Cache | P2 | Scoped flush clears the backing store beyond the scope | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-002 | Naravel.Cache | P2 | Distinct tag sets can generate the same cache key | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-003 | Naravel.Cache | P2 | Production expiry/lock/rate-limit paths bypass TimeProvider | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-004 | Naravel.Cache | P2 | Fixed 30-second rate-limit lock lease can expire mid-operation | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-005 | Naravel.Cache | P3 | RememberAsync does not validate a null non-token factory | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-006 | Naravel.Cache | DOC | Cache guide quickstart uses DateTime.UtcNow | OPEN | [Report](reports/02-Naravel.Cache.md) |
| CCH-007 | Naravel.Cache | OBS | Cache operation telemetry gap versus repository S3 DoD | OPEN — OWNER DECISION | [Report](reports/02-Naravel.Cache.md) |
| CCH-008 | Naravel.Cache | OBS | Per-tag version metadata has no expiry | OPEN — OWNER DECISION | [Report](reports/02-Naravel.Cache.md) |
| RED-001 | Naravel.Redis | P2 | FromConnectionString can collide with a configured implicit-prefixed name | OPEN | [Report](reports/03-Naravel.Redis.md) |
| RED-002 | Naravel.Redis | OBS | Synchronous first connection is a documented D-66 deviation from the PDR wording | OPEN — OWNER DECISION | [Report](reports/03-Naravel.Redis.md) |
| RED-003 | Naravel.Redis | OBS | The raw connection string is retained by a long-lived factory closure | OPEN — OWNER DECISION | [Report](reports/03-Naravel.Redis.md) |
| CRD-001 | Naravel.Cache.Redis | P2 | Invalid Database configuration silently falls back to the connection default | OPEN | [Report](reports/04-Naravel.Cache.Redis.md) |
| CRD-002 | Naravel.Cache.Redis | P2 | Redis FlushAsync skips disconnected endpoints and may report success without flushing | OPEN | [Report](reports/04-Naravel.Cache.Redis.md) |
| CRD-003 | Naravel.Cache.Redis | P2 | Cancellation tokens are checked only before Redis I/O, not while awaiting it | OPEN | [Report](reports/04-Naravel.Cache.Redis.md) |

| MCD-001 | Naravel.Cache.Memcached | P2 | Generic value-type cache misses can be reported as hits | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-002 | Naravel.Cache.Memcached | P2 | Failed writes and lock acquisition can be reported as success/contention | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-003 | Naravel.Cache.Memcached | P2 | Unsigned Memcached counter semantics corrupt the signed `long` contract | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-004 | Naravel.Cache.Memcached | P2 | Cancellation after async I/O starts does not cancel the caller's wait | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |
| MCD-005 | Naravel.Cache.Memcached | P3 | Null nullable-integral values are stored as empty text and cannot be read | OPEN | [Report](reports/05-Naravel.Cache.Memcached.md) |

## Remediation log

No remediation has been authorized or performed by the package audit branch. Once the owner approves a finding, record the fix PR/commit, regression test names, verification results, and the date/state transition here.