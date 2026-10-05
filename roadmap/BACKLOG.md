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

