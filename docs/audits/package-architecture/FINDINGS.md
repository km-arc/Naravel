# Package Audit Findings and Remediation Tracker

Current main baseline: `198b36f9e2753ccfb68354a02480cdc25e7eabc7` (audit branch is 0 commits behind; main itself was not modified).

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

The following are vetoed as target architecture. AV-001, AV-002 and AV-011 are P1 release blockers; AV-003 through AV-010 and AV-012 through AV-019 are mandatory P2 architecture work before the platform can be considered production-grade, reliable and scalable.

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
| AV-009 | P2 | Batch dispatch must recover from partial publication; no planned member may become permanently pending and unreachable. | QUE-001, RQR-004 |
| AV-010 | P2 | Process-local queue coordination state must have a bounded lifecycle without removing active gates. | MEM-001, FILE-002 |
| AV-011 | P1 | File queue names must map injectively beneath the configured root; no traversal or logical queue aliasing. | FILE-001 |
| AV-012 | P2 | Reclaim-capable queue drivers must reject zero/negative visibility timeouts; a live reservation must not be immediately reclaimable. | DBQ-001, FILE-004, RQR-001 |

Veto status is separate from finding lifecycle status. Related findings remain OPEN; a veto is cleared only after its required design is implemented, regression tests and relevant CI pass, and verification evidence is recorded. Historical PDR approvals remain in the files as design history, but veto sections in the PDRs supersede them for the target architecture.

## Findings

| ID | Package | Severity | Finding | State | Evidence / report |
|---|---|---:|---|---|---|
| FND-001 | Naravel.Foundation | P2 | Configuration fingerprint collision | VERIFIED CLOSED — PR #244 fix; full CI run #42478727741 on PR #246 | [Report](reports/01-Naravel.Foundation.md) |
| FND-002 | Naravel.Foundation | P2 | Narrow sync Driver race remains while an in-flight async creation is being replaced by a sync factory (D-83) | OPEN — OWNER DECISION | [Report](reports/01-Naravel.Foundation.md) |
| FND-003 | Naravel.Foundation | P2 | Throwing lifetime callback can abort manager cleanup | VERIFIED CLOSED — PR #244 fix; full CI run #42478727741 on PR #246 | [Report](reports/01-Naravel.Foundation.md) |
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


## Naravel.Queue

| Finding | Package | Severity | Summary | State | Evidence |
|---|---|---:|---|---|---|
| QUE-001 | Naravel.Queue | P2 | BatchAsync can strand an unreachable, permanently pending batch after partial publish (AV-009) | OPEN | [Report](reports/06-Naravel.Queue.md) |
| QUE-002 | Naravel.Queue | OBS | Failed-job retry preserves BatchId and can be skipped by a cancelled/terminal batch; owner decision required | OPEN — OWNER DECISION | [Report](reports/06-Naravel.Queue.md) |
| QUE-003 | Naravel.Queue | OBS | In-memory batch callback failure is not retried although the guide's recovery statement is unqualified | OPEN | [Report](reports/06-Naravel.Queue.md) |
| QUE-004 | Naravel.Queue | OBS | Empty batches are accepted but have provider-dependent callback behavior | OPEN — OWNER DECISION | [Report](reports/06-Naravel.Queue.md) |
| QUE-005 | Naravel.Queue | OBS | Queue runtime uses direct wall-clock reads/delays instead of injected TimeProvider | OPEN — OWNER DECISION | [Report](reports/06-Naravel.Queue.md) |

Owner decisions AD-009 through AD-013 are recorded in [OWNER-DECISIONS.md](OWNER-DECISIONS.md). No public API or runtime change is implied.


## Naravel.Queue.Memory

| Finding | Package | Severity | Summary | State | Evidence |
|---|---|---:|---|---|---|
| MEM-001 | Naravel.Queue.Memory | P2 | Static queue/lock entries and failed envelopes have no bounded reclamation lifecycle (AV-010) | OPEN | [Report](reports/07-Naravel.Queue.Memory.md) |
| MEM-002 | Naravel.Queue.Memory | P2 | All six driver operations ignore their CancellationToken | OPEN | [Report](reports/07-Naravel.Queue.Memory.md) |
| MEM-003 | Naravel.Queue.Memory | DOC | Constructor XML incorrectly says state is private to one driver instance | OPEN | [Report](reports/07-Naravel.Queue.Memory.md) |


## Naravel.Queue.File

| Finding | Package | Severity | Summary | State | Evidence |
|---|---|---:|---|---|---|
| FILE-001 | Naravel.Queue.File | P1 | Queue-name sanitization allows path traversal outside the base root and collisions between distinct queue names (AV-011) | OPEN — OWNER DECISION | [Report](reports/08-Naravel.Queue.File.md) |
| FILE-002 | Naravel.Queue.File | P2 | Static per-queue SemaphoreSlim gate table has no bounded reclamation lifecycle (AV-010) | OPEN | [Report](reports/08-Naravel.Queue.File.md) |
| FILE-003 | Naravel.Queue.File | P2 | Failed/cancelled temporary writes can leave orphan .tmp files | OPEN | [Report](reports/08-Naravel.Queue.File.md) |
| FILE-004 | Naravel.Queue.File | P2 | Non-positive visibility timeout makes reservations immediately reclaimable | OPEN | [Report](reports/08-Naravel.Queue.File.md) |
| FILE-005 | Naravel.Queue.File | P2 | Corrupt pending records are silently skipped; corrupt reserved records can stop queue polling | OPEN | [Report](reports/08-Naravel.Queue.File.md) |
| FILE-006 | Naravel.Queue.File | P2 | SizeAsync ignores its CancellationToken | OPEN | [Report](reports/08-Naravel.Queue.File.md) |

Owner decision AD-014 is recorded in [OWNER-DECISIONS.md](OWNER-DECISIONS.md). No path mapping or on-disk migration was performed by this audit.


## Naravel.Queue.Database

| Finding | Package | Severity | Summary | State | Evidence |
|---|---|---:|---|---|---|
| DBQ-001 | Naravel.Queue.Database | P2 | Zero/negative visibility timeout can reclaim a live reservation immediately | OPEN | [Report](reports/09-Naravel.Queue.Database.md) |
| DBQ-002 | Naravel.Queue.Database | P2 | SizeAsync counts reserved/in-flight rows despite the ready+delayed contract | OPEN | [Report](reports/09-Naravel.Queue.Database.md) |
| DBQ-003 | Naravel.Queue.Database | P2 | DatabaseJobEnqueuer persists a non-Database/default Connection contrary to PDR-023, misrouting chain/retry | OPEN — OWNER DECISION | [Report](reports/09-Naravel.Queue.Database.md) |
| DBQ-004 | Naravel.Queue.Database | DOC | SQLite package quickstart omits the required UTC-ticks converter | OPEN | [Report](reports/09-Naravel.Queue.Database.md) |
| DBQ-005 | Naravel.Queue.Database | OBS | PDR-012 SQL-script implementation record is stale; SQLite-only vs multi-provider DDL scope needs owner confirmation | OPEN — OWNER DECISION | [Report](reports/09-Naravel.Queue.Database.md) |

Owner decisions AD-015 and AD-016 are recorded in [OWNER-DECISIONS.md](OWNER-DECISIONS.md). No runtime code, schema or public API was changed by this audit.


## Naravel.Queue.Redis

| Finding | Package | Severity | Summary | State | Evidence |
|---|---|---:|---|---|---|
| RQR-001 | Naravel.Queue.Redis | P2 | Non-positive visibility timeout can reclaim an active reservation immediately (AV-012) | OPEN | [Report](reports/10-Naravel.Queue.Redis.md) |
| RQR-002 | Naravel.Queue.Redis | P2 | SizeAsync ignores its CancellationToken and issues 11 Redis requests | OPEN | [Report](reports/10-Naravel.Queue.Redis.md) |
| RQR-003 | Naravel.Queue.Redis | OBS | Driver-native failed list has no public inspection or cleanup path; intentional DLQ vs duplicate storage requires owner choice | OPEN — OWNER DECISION | [Report](reports/10-Naravel.Queue.Redis.md) |
| RQR-004 | Naravel.Queue.Redis | P2 | Batch hash and callback-recovery index are written separately; failure can leave an unreachable batch (AV-009) | OPEN | [Report](reports/10-Naravel.Queue.Redis.md) |
| RQR-005 | Naravel.Queue.Redis | OBS | Persistent batch index grows without retention and is fully scanned at every worker startup | OPEN — OWNER DECISION | [Report](reports/10-Naravel.Queue.Redis.md) |
| RQR-006 | Naravel.Queue.Redis | OBS | Cancellation after issuing a StackExchange.Redis command is not represented in the caller wait/result contract | OPEN — OWNER DECISION | [Report](reports/10-Naravel.Queue.Redis.md) |
| RQR-007 | Naravel.Queue.Redis | DOC | Redis Cluster unsupported by multi-key Lua scripts is not stated on the Redis Queue package page | OPEN | [Report](reports/10-Naravel.Queue.Redis.md) |

Owner decisions AD-018 through AD-020 are recorded in [OWNER-DECISIONS.md](OWNER-DECISIONS.md).


| QCO-001 | Naravel.Queue.Coordination | P2 | Unique-job owner metadata can overwrite/delete a newer lock owner's record (AV-013) | OPEN — OWNER DECISION | [Report](reports/13-Naravel.Queue.Coordination.md) |

Owner decision AD-021 is recorded in [OWNER-DECISIONS.md](OWNER-DECISIONS.md). No Cache API or runtime implementation change is implied.

| QEN-001 | Naravel.Queue.Encryption | OBS | Queue-specific Data Protection purpose lacks a persisted-key-ring cross-provider integration test | OPEN — TEST COVERAGE | [Report](reports/14-Naravel.Queue.Encryption.md) |

| DBQ-001 | Naravel.Database | P2 | JSON-path update of JSON null throws before compilation on SQLite/MySQL/PostgreSQL (AV-014) | OPEN | [Report](reports/17-Naravel.Database.md) |
| DBQ-002 | Naravel.Database | P2 | InsertGetId does not enforce final expanded Raw binding count (AV-015) | OPEN | [Report](reports/17-Naravel.Database.md) |
| DBQ-003 | Naravel.Database | P2 | Direct raw SQL does not reject reserved @pN marker collisions (AV-016) | OPEN | [Report](reports/17-Naravel.Database.md) |
| DBQ-004 | Naravel.Database | P2 | Detached child ExecutionContexts can retain a stale ambient transaction (AV-017) | OPEN — OWNER DECISION | [Report](reports/17-Naravel.Database.md) |
| DBQ-005 | Naravel.Database | P2 | Provider transaction disposal failure can skip owned lease disposal (AV-018) | OPEN | [Report](reports/17-Naravel.Database.md) |
| DBQ-006 | Naravel.Database | OBS | Transaction-bound Connection rejects aliases of the same default connection | OPEN | [Report](reports/17-Naravel.Database.md) |
| DBQ-007 | Naravel.Database | OBS | Multi-statement rollback failure masks the original statement failure | OPEN | [Report](reports/17-Naravel.Database.md) |
| DBQ-008 | Naravel.Database | OBS | DatabaseFake scalar conversion and cancellation diverge from production behavior | OPEN | [Report](reports/17-Naravel.Database.md) |

| SQL-001 | Naravel.Database.Sqlite | P2 | Named Mode=Memory connections are not normalized to unique shared-cache databases (AV-019) | OPEN | [Report](reports/18-Naravel.Database.Sqlite.md) |
| SQL-002 | Naravel.Database.Sqlite | DOC | English guide reports SQLitePCLRaw.lib.e_sqlite3 2.1.13 while central pin is 2.1.14 | OPEN | [Report](reports/18-Naravel.Database.Sqlite.md) |

| EF-001 | Naravel.Database.EntityFrameworkCore | OBS | Using(DbContext) forwards listeners/query log only for concrete DatabaseService, not IDatabase decorators | OPEN | [Report](reports/22-Naravel.Database.EntityFrameworkCore.md) |


## Findings added by the 2026-11-09 remaining-module review

| RQM-001 | Naravel.Queue.RabbitMQ | P1 | Retry/failure acknowledges the original before replacement publication, so a crash can lose the only copy | OPEN | [Report](reports/11-Naravel.Queue.RabbitMQ.md) |
| RQM-002 | Naravel.Queue.RabbitMQ | P2 | SizeAsync counts ready jobs but omits delayed jobs | OPEN | [Report](reports/11-Naravel.Queue.RabbitMQ.md) |
| KFK-001 | Naravel.Queue.Kafka | P2 | ConsumeException is returned as an empty queue and can stop StopWhenEmpty workers | OPEN | [Report](reports/12-Naravel.Queue.Kafka.md) |
| PGN-001 | Naravel.Pagination | OBS | Page result constructors retain caller-owned mutable item lists despite immutable wording | OPEN | [Report](reports/23-Naravel.Pagination.md) |
| PGE-001 | Naravel.Pagination.EntityFrameworkCore | P2 | Nullable cursor keys can produce predicates that skip SQL NULL rows on some providers | OPEN | [Report](reports/24-Naravel.Pagination.EntityFrameworkCore.md) |
| FSS-001 | Naravel.Filesystem | P2 | Local and non-expiring S3 URL generation does not escape reserved key characters | OPEN | [Report](reports/26-Naravel.Filesystem.md) |
| BRD-001 | Naravel.Broadcasting | P2 | Repeating Subscribe on the same presence connection can emit a duplicate MemberJoined event | OPEN | [Report](reports/33-Naravel.Broadcasting.md) |
| BRD-002 | Naravel.Broadcasting | P2 | Presence heartbeat timer does not follow a reloaded PresenceEntryTtl | OPEN | [Report](reports/33-Naravel.Broadcasting.md) |
| RTG-001 | Naravel.Routing | P2 | Fallback-created middleware instances have no disposal path | OPEN | [Report](reports/34-Naravel.Routing.md) |
| RTC-001 | Naravel.Routing.Cache | P2 | Cache-store recreation after reload can block before throttle StoreTimeout starts | OPEN | [Report](reports/35-Naravel.Routing.Cache.md) |
