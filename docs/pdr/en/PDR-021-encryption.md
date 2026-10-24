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

## Amendment A1 (2026-10-24, proposed): the exact API, the envelope and the migration rule

The approval above fixed the direction but left the exact marker, API and envelope open ("open for owner approval and must be settled before implementation"). R18.T01 closes that here. Nothing below adds cryptography: every byte of protection comes from `IDataProtector`.

### Proposed API (S1: one registration line, two calls)

```csharp
services.AddNaravelEncryption();   // registers AddDataProtection() if the app has not, and IEncrypter

var token = encrypter.Encrypt("secret");              // Laravel: Crypt::encryptString
var text  = encrypter.Decrypt(token);                 // Crypt::decryptString
var card  = encrypter.Decrypt<CardInfo>(encrypter.Encrypt(new CardInfo(...)));   // Crypt::encrypt / decrypt, JSON inside the envelope
```

- `IEncrypter` has `Encrypt(string)`, `Decrypt(string)`, `TryDecrypt(string, out string?)`, `Encrypt<T>(T)`, `Decrypt<T>(string)` (typed values go through `System.Text.Json`; an overload takes a `JsonTypeInfo<T>` so source-generated metadata works), and `ForPurpose(string)` which returns an `IEncrypter` isolated under a sub-purpose.
- All members are **synchronous**, as approved: the primitive is synchronous and no async wrapper is invented.
- A value that cannot be decrypted (tampered, wrong purpose, missing or revoked key, malformed) throws `DecryptException` (Laravel: `DecryptException`) with a fixed message; the message and the exception data never contain the input or any plaintext. `TryDecrypt` returns false instead of throwing.
- The default purpose is `Naravel.Encryption.v1`; `ForPurpose("billing")` gives `Naravel.Encryption.v1/billing`. Different purposes cannot read each other's output.
- Key ring: the facade does not touch keys. Persistence, protection at rest, rotation (Data Protection creates a new key about every 90 days and keeps old keys to decrypt) and the shared application name are the application's `AddDataProtection()` configuration. Producers and workers of a queue **must share the key ring and the application name**; the docs say so first.
- Telemetry: a `Meter` `Naravel.Encryption` with counter `naravel.encryption.operations` tagged only by `operation` (`encrypt`, `decrypt`) and `outcome` (`ok`, `invalid`); never plaintext, ciphertext, purposes from user input or key data.
- No time-limited protection, no Laravel-compatible ciphertext, no `Crypt::` static facade (use DI).

### Encrypted jobs (`Naravel.Queue.Encryption`)

- **Marker:** an empty interface `IShouldBeEncrypted` in `Naravel.Queue.Encryption`. A job class opts in by implementing it (Laravel: `ShouldBeEncrypted`). `Naravel.Queue` does not reference Encryption or the marker.
- **Seam:** `AddNaravelQueueEncryption()` replaces `IJobSerializer` with a decorator over the registered serializer. `Serialize(job)` writes the inner payload for an unmarked job unchanged, and for a marked job writes `naravel:enc:v1:` + the Data Protection output of the inner payload, under the purpose `Naravel.Queue.Encryption.v1`. `Deserialize(payload, type)` is called only after the alias registry selected the type (OD-07), so decryption never chooses a type.
- **Chains, batches and failed jobs** already carry the serialized string, so they carry ciphertext and need no change.
- **Failure rules:** an unavailable key, a tampered payload or a wrong purpose makes the job fail with `DecryptException` through the normal failed-job path; a marked job is never written or sent as plaintext, and the envelope version `v1` is checked, an unknown version fails.
- **Migration:** a job that was queued as plaintext before its class was marked is, by default, **refused** with a clear message (no silent plaintext). `QueueEncryptionOptions.AcceptPlaintextForMarkedJobs` (default `false`) can be switched on for a deploy window so in-flight jobs finish, then switched off. Unmarked jobs keep the existing wire format; nothing is encrypted by default.

### Decisions for the owner

- **E1 Names:** Laravel-style `IEncrypter.Encrypt`/`Decrypt` (recommended), or the earlier quickstart's `Protect`/`Unprotect`.
- **E2 Marker location:** `IShouldBeEncrypted` in the adapter package (recommended: Queue core stays free of it, as the stage decision requires), or in `Naravel.Queue` core.
- **E3 Envelope:** the `naravel:enc:v1:` text prefix (recommended: it cannot start a JSON payload, so plaintext and ciphertext are told apart without parsing, and the version allows a later format).
- **E4 Migration option:** include `AcceptPlaintextForMarkedJobs`, default off (recommended), or no option and drain the queue before marking a job.
- **E5 Facade purpose and job purpose:** separate purposes (recommended), so a value from `IEncrypter` can never be accepted as a job payload.
- **E6 Package reference:** the facade references `Microsoft.AspNetCore.DataProtection` (recommended, so `AddNaravelEncryption()` is one line; it also brings the abstractions), or only `Microsoft.AspNetCore.DataProtection.Abstractions` and the application calls `AddDataProtection()` itself. The version is pinned at implementation from NuGet.
- **E7 Fake:** no `EncryptionFake` (recommended: the stage said not to fake cryptographic guarantees, and `EphemeralDataProtectionProvider` is a real, in-memory, no-persistence test double that Data Protection already ships; the docs show the two-line test setup), or a clearly labelled reversible fake.

### Tests required (T02, T03)

Round trip (string and typed), tamper detection, purpose isolation, `ForPurpose`, key persistence across two provider instances, `TryDecrypt`, an error message that never contains the input. Adapter: marked and unmarked jobs, the stored payload is not plaintext and starts with the envelope prefix, the job still runs on the Memory queue, alias-only type selection, missing key and tampered payload fail visibly with no plaintext fallback, unknown envelope version, plaintext-before-marking refused and accepted under the option, chains and failed jobs carrying ciphertext.

### Approval of A1

Not yet approved. After approval, R18.T01 is ticked and T02 (module) and T03 (adapter) run.
