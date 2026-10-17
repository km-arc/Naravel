# Roadmap backlog

Items here are ideas, not approved scope or a `NEXT` pointer. Evaluate their .NET value before assigning a stage or PDR.
Do not implement these in an active stage unless its owner-approved scope explicitly includes them.

| Candidate | Initial disposition | Dependency / next action |
|---|---|---|
| Images | Unassessed | Compare .NET imaging libraries, licensing, and provider boundaries; owner selects scope and PDR. |
| Search | Unassessed | Define domain/query requirements and provider scope before choosing a Laravel analogue. |
| General application/request context | Partial existing behavior (`JobContext` only) | Identify concrete ASP.NET Core gaps; avoid duplicating `AsyncLocal` or request services without a PDR. |
| General Laravel collections / string helpers | Prefer .NET native APIs | Add only concrete helpers with measurable ergonomics/value. |
| Laravel-style service-provider lifecycle | Prefer .NET DI extension registration | Keep package registration idiomatic; no separate lifecycle without demonstrated need. |
| Controller resource routing | Deferred routing follow-up | Include in R05 only if accepted scope and GO approval allow it. |
| Cross-provider live integration suite | Planned under R01 | Owner approval required for Testcontainers/test dependency and CI platform cost. |
| Database/Redis provider semantics outside listed stages | Unassessed | Add audit finding and owning stage if a verified defect appears. |
| Encryption (`Crypt`, `ShouldBeEncrypted`) | Assigned: R18 (OD-03) | Thin facade over Data Protection. |
| Validation / Form Requests | Not built (OD-04) | Document FluentValidation/DataAnnotations; revisit after R17. |
| Testing fakes | Assigned: DoD S3 per stage; Queue in R07.T07 | - |
| Observability (`Meter`, `ActivitySource`) | Assigned: DoD S3 per stage; Queue in R07.T07 | - |
| Queue job debounce (Laravel-style "only the latest dispatch runs") | Moved out of R08 by owner on 2026-10-13 | Needs a durable "latest dispatch" record per key; revisit after PDR-012b middleware ships. |
| Mail attachments and inline images | Deferred from R09 (PDR-013a); owner kept the previous priority on 2026-10-14 | Needs PDR-013b: how files are referenced in the queue payload (path, storage disk and name, never raw bytes), size limits, and MailKit `BodyBuilder` mapping. |
| Mail template rendering (`IMailRenderer` implementations) | Not built (PDR-013 leaves the engine to the app) | Document Razor/Scriban options; revisit after R17. |
| Scheduled CI run (weekly `schedule:` in `ci.yml`) | Proposed 2026-10-17 after two advisories turned `main` red without a commit (D-38, D-40) | Needs owner decision; a weekly run would surface new NuGet advisories before the next PR. |
