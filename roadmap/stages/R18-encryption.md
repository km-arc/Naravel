# R18 — Encryption facade and encrypted jobs

Parity section: `R18` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: PDR-021 (EN+FA) approved first.
Read: `src/Naravel.Queue/Serialization/IJobSerializer.cs`, `src/Naravel.Queue/Jobs/IJob.cs`; ASP.NET Core Data Protection docs.
Touch: new `src/Naravel.Encryption/**`, `src/Naravel.Queue.Encryption/**` (adapter), `tests/Naravel.Encryption.Tests/**`, `Naravel.slnx`, docs/PDR-021.
Out of scope: custom cryptography (never); cookie/session encryption beyond Data Protection (R11).
Decisions: OD-03: facade over `IDataProtector`; Queue core must not depend on Encryption.

## Tasks
- [x] **R18.T01 — PDR-021:** facade `IEncrypter` (`Encrypt/Decrypt` for string and typed values, purpose string, key-ring/rotation notes), marker `IShouldBeEncrypted` for jobs. **Accept:** PDR EN+FA with quickstart. PDR-021 was approved in 2026-10 but left the exact API, marker and envelope open; amendment A1 (EN+FA, decisions E1 to E7) settles them and was approved on 2026-10-24 as recommended, with reasons in the PDR.
- [ ] **R18.T02 — Module:** `Naravel.Encryption` over `IDataProtectionProvider`, plus `EncryptionFake`. **Accept:** round-trip, tamper detection, purpose isolation tests.
- [ ] **R18.T03 — Encrypted jobs adapter:** `Naravel.Queue.Encryption` decorates `IJobSerializer` so jobs marked `IShouldBeEncrypted` store ciphertext. **Accept:** Memory-queue test shows the stored payload is not plaintext and the job still runs; the failure message of an undecryptable marked job names the shared key ring and application name as the first thing to check, with a test, and `docs/en|fa/encryption.md` opens with the key-ring requirement of PDR-021 A1.
- [ ] **R18.T04 — Validate:** docs EN/FA, parity/status/changelog, full verification. **Accept:** DoD 5–7.
