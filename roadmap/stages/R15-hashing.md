# R15 — Hashing

Parity section: `R15` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-019 (EN+FA) approved first. Expected per OD-08: `DECLINED`.
Read: ASP.NET Core Identity `IPasswordHasher<TUser>` docs; .NET `Rfc2898DeriveBytes`.
Touch: new `src/Naravel.Hashing/**` only if not declined, tests, docs/PDR-019.
Out of scope: custom cryptographic primitives (never).
Decisions: Native baseline: `IPasswordHasher<TUser>`.

## Tasks
- [ ] **R15.T01 — PDR-019:** is `Make/Check/NeedsRehash` over `IPasswordHasher` worth a package (Bcrypt/Argon2 would need a dependency)? Conclude `DECLINED` with a docs recipe if not. **Accept:** PDR EN+FA with a verdict.
- [ ] **R15.T02 — Implementation (only if kept):** small typed `IHasher`. **Accept:** known-answer, verify, rehash tests; no secret logged.
- [ ] **R15.T03 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7 (or DECLINED evidence).
