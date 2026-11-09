# Review Report — Naravel.Encryption

## Review metadata

- Package ID: Naravel.Encryption.
- Source project: src/Naravel.Encryption/Naravel.Encryption.csproj.
- Baseline: main at 282a507bead92ae9ca40a9cb060baa2ee1dd7287.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, test and security-contract review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH OBSERVATION (source inspection only; runtime verification pending).
- Files inspected: Encrypter, IEncrypter, DecryptException, EncryptionTelemetry, service registration, tests, package README and encryption guide.
- Dependencies: Microsoft.AspNetCore.DataProtection and the Microsoft.Extensions registration/logging abstractions; no custom cryptographic primitive.
- Verification performed: protect/unprotect flow, purpose separation, typed JSON conversion, error messages, source-generated metadata overloads, Data Protection registration and telemetry tags inspected.
- Verification NOT RUN: restore, build, tests, persisted-key multi-process test in this audit session, full CI, mutation testing and independent review.

## Executive assessment

The package delegates cryptographic protection to ASP.NET Core Data Protection. It does not implement its own cipher or key storage. Purpose separation, tamper detection, persisted key-ring compatibility and source-generated JSON metadata overloads are covered by the inspected tests.

One telemetry accuracy issue remains: a typed Decrypt<T> first calls the string Decrypt method, which records a successful decrypt as soon as unprotecting succeeds. If JSON conversion then fails, the typed method records an invalid decrypt too. A single typed operation is counted once as successful and again as invalid.

## Package and dependency map

- Public interface: IEncrypter.
- Main implementation: Encrypter.
- Supporting public error: DecryptException.
- Registration: AddNaravelEncryption, using the application's Data Protection provider or registering Data Protection when absent.
- Purpose isolation: ForPurpose creates a child purpose from the current purpose.
- AOT path: overloads accepting JsonTypeInfo<T>; reflection-based overloads carry trimming/dynamic-code annotations.

## Findings

### [OBS] ENC-001 — A typed decrypt that contains invalid JSON is counted as both success and failure

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE; telemetry assertion for invalid typed JSON NOT RUN.
- Evidence: src/Naravel.Encryption/Encrypter.cs. Decrypt<T> calls FromJson; FromJson first calls Decrypt(payload), and Decrypt records operation=decrypt/outcome=ok after unprotecting the payload. If JsonSerializer.Deserialize then throws JsonException, FromJson records operation=decrypt/outcome=invalid and throws DecryptException.
- In everyday words: The package successfully opens the protected text, but cannot turn that text into the requested type. The metric counts the same call as both a successful decrypt and a failed decrypt.
- Realistic example: A caller stores a protected string value, then calls Decrypt<CardInfo> on it. The Data Protection step succeeds, JSON conversion fails, and telemetry increments both ok and invalid.
- Why the evidence proves the mismatch: both telemetry calls are inside the same public typed-decrypt execution path; there is no distinction in the metric between “protection opened” and “the requested typed value was decoded.”
- What is not proven: source inspection establishes the double increment path; the current telemetry test covers string decrypt and malformed encrypted input, not a valid protected payload with invalid JSON for the requested type.
- Required safe outcome: one public operation should produce one final outcome. A typed decrypt whose JSON conversion fails should count as invalid, not also as ok.
- Regression tests required: encrypt a valid string that is not valid JSON for a requested type, call Decrypt<T>, and assert exactly one decrypt/invalid measurement and no decrypt/ok measurement for that call. Keep normal string and typed round trips.
- Public API / PDR / release impact: telemetry correction; no public API change expected.

## Existing guarantees and coverage

- Data Protection purpose separation is tested: values protected under one purpose cannot be read under another.
- Tampered and malformed tokens fail without echoing the input or plaintext in the exception message.
- Persisted-key tests cover reading through a new provider over the same key directory and application name, and failure with an unrelated key ring or application name.
- Typed serialization is tested both through reflection and through source-generated JsonTypeInfo metadata.
- Telemetry tags are limited to operation and outcome; the tests assert no extra tag dimensions.
- The Queue-specific purpose has a separate adapter-level coverage gap recorded in report 14; that does not establish a defect in this core package.

## Test assessment

- Tests inspected: EncrypterTests.
- Missing test: invalid JSON after successful unprotect must count as one invalid typed operation, not both ok and invalid.
- Tests actually run in this review: NONE.
- Build/CI, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The README explains shared Data Protection key rings, application names, purposes, tamper failures and the AOT/source-generated JSON path.
- The package does not supply a custom cryptographic format or secret-management system.
- No sample compilation or persisted-key test was run in this audit.

## Risk summary and next action

- OBS: ENC-001 (telemetry counts one typed failure twice).
- No source-level cryptographic defect was established in the reviewed path.
- Recommended next task: correct the metric boundary and add the one-operation/one-outcome test. No runtime change is authorized by this audit.

## Plain-language explanation

The package uses ASP.NET Core Data Protection to protect and recover values. The cryptographic boundary and purpose separation look sound in the inspected source and tests. The one observation is about monitoring, not encryption: when protected text opens but cannot be read as the requested JSON type, the metric records both success and failure for the same call. No encryption tests were run in this audit.
