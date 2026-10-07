# PDR-021 — Encryption facade and encrypted jobs (`Naravel.Encryption`)

**Native-first verdict:** ASP.NET Core Data Protection already provides authenticated protection, purpose isolation, and key management. Naravel must not implement cryptographic primitives; value is limited to a concise facade and an explicit opt-in Queue integration.

Status: **Approved 2026-10-07.** OD-03 pre-approves the Data Protection direction; the owner also approved the facade API, explicit job marker/registration boundary, Queue adapter, and envelope compatibility approach.

## Context

Laravel exposes convenient encrypt/decrypt operations and can mark queue jobs as encrypted. .NET applications should use `IDataProtectionProvider` and `IDataProtector` directly for most needs. A thin facade may make the common operation consistent, while encrypted jobs need careful compatibility, key-ring, and alias-registry behavior.

Laravel surface: `Illuminate\Encryption\Encrypter`, `Crypt`, and `ShouldBeEncrypted`. Native baseline: ASP.NET Core Data Protection. No custom cryptography, algorithm selection, or independent key format is proposed.

## Proposed decision

Add a thin `Naravel.Encryption` package backed only by Data Protection. Configure an application-specific purpose and expose a small typed service for string protection and unprotection. The facade must not expose keys or claim compatibility with Laravel ciphertext.

Common case:

```csharp
services.AddNaravelEncryption();
var protectedValue = protector.Protect(value);
var value = protector.Unprotect(protectedValue);
```

The simple facade is synchronous because the Data Protection primitive is synchronous; callers must use ASP.NET Core Data Protection key persistence and protection-at-rest configuration appropriate to their deployment. Do not invent an async wrapper or block on asynchronous work.

For Queue integration, propose an opt-in adapter in `Naravel.Queue.Encryption` and an explicit job marker/registration policy. Encryption must happen to the serialized payload while preserving the existing explicit job alias registry (OD-07); decrypted payloads are deserialized only after the registered alias has selected the job type. Unmarked jobs retain the existing wire format. Key rotation, unavailable keys, and incompatibility must fail visibly; never fall back to plaintext for a marked job.

The exact marker/API and envelope versioning are open for owner approval and must be settled with the Queue maintainers before implementation. Existing jobs and deployments require a documented migration strategy; do not silently change all queue payloads.

## Contracts and quality

- Use `IDataProtectionProvider`/`IDataProtector` only; do not implement cryptography or persist raw keys in Naravel data.
- Separate protection purposes by application and feature; callers remain responsible for key-ring persistence/access across producer and worker processes.
- Invalid, tampered, expired-by-policy, or undecryptable protected data surfaces a clear failure.
- If the queue adapter emits telemetry, use `Meter`/`ActivitySource` with bounded outcomes; never tag plaintext, protected payloads, aliases containing user data, or key material.
- Provide an in-memory fake only if a useful application-facing encryption contract exists; do not fake cryptographic guarantees.

## Verification required after approval

Test round-trip, tamper detection, purpose isolation, key persistence across provider instances, cancellation where applicable, and failure behavior. Queue adapter tests must cover marked/unmarked jobs, alias-only type selection, payload compatibility, missing keys, and no plaintext fallback. Update EN/FA docs, parity records, changelog and stage checklist; run full solution verification and roadmap checker.

## Alternatives considered

1. **Use Data Protection directly everywhere.** Preferred for applications that do not need a shared Naravel API or encrypted queue-job contract.
2. **Implement Laravel-compatible encryption or custom AES/key management.** Rejected; it duplicates security-sensitive primitives and is explicitly outside OD-03.
3. **Encrypt every job payload by default.** Rejected; it changes wire compatibility, requires shared key-ring operations, and can break existing workers.
4. **Resolve CLR types from encrypted payload metadata.** Rejected by OD-07; the alias registry remains the only type-selection mechanism.

## Approval

The owner approved this proposal on 2026-10-07. OD-03 remains binding: implementation must use Data Protection and must not add custom cryptography.
