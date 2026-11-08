# Review Report — Naravel.Queue.Encryption

## Review metadata

- Package ID: `Naravel.Queue.Encryption` (explicit `PackageId` in `src/Naravel.Queue.Encryption/Naravel.Queue.Encryption.csproj`).
- Source project: `src/Naravel.Queue.Encryption/Naravel.Queue.Encryption.csproj`.
- Source-review baseline: `main` at `44e957ca3db6d9f15665fc8fa070f70522c163c9`; rechecked against current `main` at `c6a7d58ffb84d885741998bb32d2082c54c4ab61`. Package source and tests are unchanged across that range.
- Review date: 2026-11-08.
- Reviewer mode: principal-architect source and contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH OBSERVATION (source inspection only; runtime verification pending).
- Files inspected: package project and references; `EncryptedJobSerializer`; marker and options; DI decorator registration; shipped public API; Queue serializer and envelope; failed-job persistence/retry flow; Queue and Encryption package tests; PDR-021; English encryption guide and NuGet README.
- Dependency boundary: the optional adapter references `Naravel.Queue` and `Naravel.Encryption`; Queue core does not reference Encryption. Cryptography remains delegated to ASP.NET Core Data Protection.
- Verification performed: source-level trace of marked/unmarked serialization and deserialization, alias-before-decrypt ordering, chain/failed-job ciphertext propagation, migration fallback and DI decorator lifetime.
- Verification NOT RUN: `.NET restore/build/test`, full CI, persisted-key-ring integration for the Queue-specific purpose across separate providers, mutation testing and independent review.

## Executive assessment

- The adapter uses Data Protection only; no custom cryptographic primitive, raw key storage or type resolution from decrypted payload is introduced.
- The explicit job alias is resolved by Queue before the serializer's `Deserialize(payload, type)` call; the encrypted payload never selects the CLR type.
- Marked jobs are encrypted with a feature-specific purpose and versioned prefix. Unknown versions, tampering, wrong purpose/key ring and default plaintext-for-marked payloads fail visibly. The migration switch is explicit and opt-in.
- Unmarked jobs retain their old payload wire format. Chains serialize each remaining job through the same serializer; failed-job storage and retry clone the opaque ciphertext string without decrypting it.
- No source-level correctness or security defect was identified in the inspected adapter.
- One Queue-adapter integration test gap remains: the queue-specific purpose is not tested with two independent Data Protection providers loading the same persisted key ring. The base `Naravel.Encryption` package tests persisted-key-ring compatibility for its own purpose, but that does not exercise the Queue decorator's purpose and registration path.
- No runtime code, tests, package references, public API or dependencies were changed by this review.

## Findings

### [OBS] QEN-001 — Queue-specific encrypted payloads lack a persisted-key-ring cross-provider integration test

- Remediation state: OPEN — TEST COVERAGE.
- Evidence status: CONFIRMED TEST-COVERAGE GAP.
- Evidence: `tests/Naravel.Queue.Encryption.Tests/EncryptedJobTests.cs` uses an `EphemeralDataProtectionProvider` shared as the same provider instance for the positive producer/worker interoperability test (`A_worker_sharing_the_key_ring_reads_the_job`). The test for another key ring uses two independent ephemeral providers and proves failure. Separately, `tests/Naravel.Encryption.Tests/EncrypterTests.cs` proves that the base facade can read a value using a new provider over the same persisted key directory, but it exercises the facade's purpose, not `EncryptedJobSerializer.Purpose`.
- Affected contract: producer and worker processes configured with separate Data Protection provider instances but the same persisted key ring and application name must decrypt a marked Queue job successfully.
- User impact: a deployment configuration that works with one shared in-process provider is not directly covered at the Queue adapter boundary. The source uses a fixed purpose and Data Protection correctly, so this is an integration assurance gap, not evidence of a current cryptographic defect.
- Required test: create two separate service providers, each configured with `AddDataProtection().SetApplicationName(...).PersistKeysToFileSystem(theSameTemporaryDirectory)`; serialize a marked job with the first provider and deserialize it with the second through `AddNaravelQueueEncryption`. Also verify that changing the application name or key directory fails with `DecryptException`. Clean up the temporary key directory after both providers are disposed.
- Public API / PDR / release impact: test-only; no API or dependency change is required.

## Existing guarantees and coverage

- Tests cover ciphertext storage, unmarked wire-format compatibility, round-trip, alias-only type selection, tamper detection, unknown envelope versions, purpose isolation, wrong-key failure, default plaintext refusal, opt-in migration acceptance, and ciphertext preservation in queue storage, chains and failed-job records.
- The base Encryption package separately tests persisted key-ring interoperability across new provider instances and application-name isolation.
- `AddNaravelQueueEncryption` requires `AddQueue` first, retains the existing serializer lifetime, and guards against wrapping the same descriptor twice.
- The Queue envelope stores the ciphertext as an opaque string; queue drivers and failed-job stores do not need to know the encryption format.
- No custom crypto, encryption of all jobs by default, plaintext fallback by default, or decrypted-type selection is present.

## Test assessment

- Reviewed: `EncryptedJobTests`, `EncrypterTests`, Queue serializer/registry, Queue envelope, failed-job retry path, PDR-021 and EN package documentation.
- Tests run for this review: NONE.
- Build/CI: NOT RUN.
- Mutation testing: NOT RUN.
- Independent review: NOT RUN.

## Owner decision gate

No owner decision is required to add the missing integration test. This audit does not authorize runtime or API changes.
