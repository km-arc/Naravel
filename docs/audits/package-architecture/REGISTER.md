# Package Review Register

Current synchronization baseline: `main` at `852db2c3c1be593a32fdce5318c18d0cd31149be` (PR #247 merged; audit branch merge-base is this commit). Each report retains its original package source-review baseline and records the latest relevant source revalidation.

Inventory: **36** packable source projects. There are 35 package pages in `docs/nuget/`; `Naravel.Foundation` uses `docs/en/foundation.md` as its packed README.

| Order | Package ID | Source project | Package README | Status | Findings |
|---:|---|---|---|---|---|
| 01 | `Naravel.Foundation` | `src/Naravel.Foundation/Naravel.Foundation.csproj` | `docs/en/foundation.md` | REVIEWED WITH FINDINGS | [Report](reports/01-Naravel.Foundation.md): 2 P2 OPEN, 2 VERIFIED CLOSED |
| 02 | `Naravel.Cache` | `src/Naravel.Cache/Naravel.Cache.csproj` | `docs/nuget/Naravel.Cache.md` | REVIEWED WITH FINDINGS | [Report](reports/02-Naravel.Cache.md): 2 P1, 4 P2, 1 P3, 1 DOC |
| 03 | `Naravel.Redis` | `src/Naravel.Redis/Naravel.Redis.csproj` | `docs/nuget/Naravel.Redis.md` | REVIEWED WITH FINDINGS | [Report](reports/03-Naravel.Redis.md): 2 P2, 1 OBS |
| 04 | `Naravel.Cache.Redis` | `src/Naravel.Cache.Redis/Naravel.Cache.Redis.csproj` | `docs/nuget/Naravel.Cache.Redis.md` | REVIEWED WITH FINDINGS | [Report](reports/04-Naravel.Cache.Redis.md): 3 P2 |
| 05 | `Naravel.Cache.Memcached` | `src/Naravel.Cache.Memcached/Naravel.Cache.Memcached.csproj` | `docs/nuget/Naravel.Cache.Memcached.md` | REVIEWED WITH FINDINGS | [Report](reports/05-Naravel.Cache.Memcached.md): 2 P1, 7 P2, 1 P3, 1 OBS |
| 06 | `Naravel.Queue` | `src/Naravel.Queue/Naravel.Queue.csproj` | `docs/nuget/Naravel.Queue.md` | REVIEWED WITH FINDINGS | [Report](reports/06-Naravel.Queue.md): 1 P2, 4 OBS |
| 07 | `Naravel.Queue.Memory` | `src/Naravel.Queue.Memory/Naravel.Queue.Memory.csproj` | `docs/nuget/Naravel.Queue.Memory.md` | REVIEWED WITH FINDINGS | [Report](reports/07-Naravel.Queue.Memory.md): 2 P2, 1 DOC |
| 08 | `Naravel.Queue.File` | `src/Naravel.Queue.File/Naravel.Queue.File.csproj` | `docs/nuget/Naravel.Queue.File.md` | REVIEWED WITH FINDINGS | [Report](reports/08-Naravel.Queue.File.md): 1 P1, 5 P2 |
| 09 | `Naravel.Queue.Database` | `src/Naravel.Queue.Database/Naravel.Queue.Database.csproj` | `docs/nuget/Naravel.Queue.Database.md` | REVIEWED WITH FINDINGS | [Report](reports/09-Naravel.Queue.Database.md): 3 P2, 1 DOC, 1 OBS |
| 10 | `Naravel.Queue.Redis` | `src/Naravel.Queue.Redis/Naravel.Queue.Redis.csproj` | `docs/nuget/Naravel.Queue.Redis.md` | PENDING | — |
| 11 | `Naravel.Queue.RabbitMQ` | `src/Naravel.Queue.RabbitMQ/Naravel.Queue.RabbitMQ.csproj` | `docs/nuget/Naravel.Queue.RabbitMQ.md` | PENDING | — |
| 12 | `Naravel.Queue.Kafka` | `src/Naravel.Queue.Kafka/Naravel.Queue.Kafka.csproj` | `docs/nuget/Naravel.Queue.Kafka.md` | PENDING | — |
| 13 | `Naravel.Queue.Coordination` | `src/Naravel.Queue.Coordination/Naravel.Queue.Coordination.csproj` | `docs/nuget/Naravel.Queue.Coordination.md` | PENDING | — |
| 14 | `Naravel.Queue.Encryption` | `src/Naravel.Queue.Encryption/Naravel.Queue.Encryption.csproj` | `docs/nuget/Naravel.Queue.Encryption.md` | PENDING | — |
| 15 | `Naravel.Events` | `src/Naravel.Events/Naravel.Events.csproj` | `docs/nuget/Naravel.Events.md` | PENDING | — |
| 16 | `Naravel.Events.Queue` | `src/Naravel.Events.Queue/Naravel.Events.Queue.csproj` | `docs/nuget/Naravel.Events.Queue.md` | PENDING | — |
| 17 | `Naravel.Database` | `src/Naravel.Database/Naravel.Database.csproj` | `docs/nuget/Naravel.Database.md` | PENDING | — |
| 18 | `Naravel.Database.Sqlite` | `src/Naravel.Database.Sqlite/Naravel.Database.Sqlite.csproj` | `docs/nuget/Naravel.Database.Sqlite.md` | PENDING | — |
| 19 | `Naravel.Database.MySql` | `src/Naravel.Database.MySql/Naravel.Database.MySql.csproj` | `docs/nuget/Naravel.Database.MySql.md` | PENDING | — |
| 20 | `Naravel.Database.Postgres` | `src/Naravel.Database.Postgres/Naravel.Database.Postgres.csproj` | `docs/nuget/Naravel.Database.Postgres.md` | PENDING | — |
| 21 | `Naravel.Database.SqlServer` | `src/Naravel.Database.SqlServer/Naravel.Database.SqlServer.csproj` | `docs/nuget/Naravel.Database.SqlServer.md` | PENDING | — |
| 22 | `Naravel.Database.EntityFrameworkCore` | `src/Naravel.Database.EntityFrameworkCore/Naravel.Database.EntityFrameworkCore.csproj` | `docs/nuget/Naravel.Database.EntityFrameworkCore.md` | PENDING | — |
| 23 | `Naravel.Pagination` | `src/Naravel.Pagination/Naravel.Pagination.csproj` | `docs/nuget/Naravel.Pagination.md` | PENDING | — |
| 24 | `Naravel.Pagination.EntityFrameworkCore` | `src/Naravel.Pagination.EntityFrameworkCore/Naravel.Pagination.EntityFrameworkCore.csproj` | `docs/nuget/Naravel.Pagination.EntityFrameworkCore.md` | PENDING | — |
| 25 | `Naravel.Encryption` | `src/Naravel.Encryption/Naravel.Encryption.csproj` | `docs/nuget/Naravel.Encryption.md` | PENDING | — |
| 26 | `Naravel.Filesystem` | `src/Naravel.Filesystem/Naravel.Filesystem.csproj` | `docs/nuget/Naravel.Filesystem.md` | PENDING | — |
| 27 | `Naravel.Mail` | `src/Naravel.Mail/Naravel.Mail.csproj` | `docs/nuget/Naravel.Mail.md` | PENDING | — |
| 28 | `Naravel.Mail.Smtp` | `src/Naravel.Mail.Smtp/Naravel.Mail.Smtp.csproj` | `docs/nuget/Naravel.Mail.Smtp.md` | PENDING | — |
| 29 | `Naravel.Mail.Queue` | `src/Naravel.Mail.Queue/Naravel.Mail.Queue.csproj` | `docs/nuget/Naravel.Mail.Queue.md` | PENDING | — |
| 30 | `Naravel.Notifications` | `src/Naravel.Notifications/Naravel.Notifications.csproj` | `docs/nuget/Naravel.Notifications.md` | PENDING | — |
| 31 | `Naravel.Notifications.Mail` | `src/Naravel.Notifications.Mail/Naravel.Notifications.Mail.csproj` | `docs/nuget/Naravel.Notifications.Mail.md` | PENDING | — |
| 32 | `Naravel.Notifications.Queue` | `src/Naravel.Notifications.Queue/Naravel.Notifications.Queue.csproj` | `docs/nuget/Naravel.Notifications.Queue.md` | PENDING | — |
| 33 | `Naravel.Broadcasting` | `src/Naravel.Broadcasting/Naravel.Broadcasting.csproj` | `docs/nuget/Naravel.Broadcasting.md` | PENDING | — |
| 34 | `Naravel.Routing` | `src/Naravel.Routing/Naravel.Routing.csproj` | `docs/nuget/Naravel.Routing.md` | PENDING | — |
| 35 | `Naravel.Routing.Cache` | `src/Naravel.Routing.Cache/Naravel.Routing.Cache.csproj` | `docs/nuget/Naravel.Routing.Cache.md` | PENDING | — |
| 36 | `Naravel.Scheduling` | `src/Naravel.Scheduling/Naravel.Scheduling.csproj` | `docs/nuget/Naravel.Scheduling.md` | PENDING | — |

## Inventory verification

- [x] Read the recursive repository tree and identified all projects under `src/**/*.csproj`.
- [x] Read each of the 36 source project files and checked its package ID / default name, explicit `IsPackable` setting, README metadata, project references, and package references.
- [x] Confirmed that none of the 36 source project files sets `IsPackable=false`.
- [x] Counted 35 Markdown package pages in `docs/nuget/`.
- [x] Confirmed that `Naravel.Foundation` packs `docs/en/foundation.md` as `README.md`, which explains the 35-vs-36 documentation-page count.
- [ ] Verify the final generated package count from a real `dotnet pack` archive (NOT RUN in this remote source-inspection pass).

## Finding lifecycle

Individual findings and their remediation states are tracked in [FINDINGS.md](FINDINGS.md). A finding remains OPEN until the owner authorizes remediation. It then moves through APPROVED FOR REMEDIATION, IN REMEDIATION, FIXED — WAITING VERIFICATION, and only becomes VERIFIED CLOSED after the exact regression tests and required CI checks pass. ACCEPTED RISK requires an explicit owner decision recorded with rationale. A package can be REVIEWED WITH FINDINGS while its findings remain open.

## Architecture veto gate

Owner decisions that must be settled before remediation is scheduled are recorded in [OWNER-DECISIONS.md](OWNER-DECISIONS.md). These are not pre-approved by the audit.


[ARCHITECTURE-VETOES.md](ARCHITECTURE-VETOES.md) records eleven mandatory architecture vetoes. They supersede historical PDR acceptance for the target production architecture; they are not accepted risks and remain open until verified remediation.

## Cross-package findings

Cross-package findings are recorded once in [FINDINGS.md](FINDINGS.md), with every affected package linked. Package count and documentation placement are inventory clarifications, not implementation defects.
