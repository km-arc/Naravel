# Encryption

`Naravel.Encryption` is a thin `Encrypt` / `Decrypt` facade over ASP.NET Core Data Protection. It adds no cryptography: every byte of protection, and every key, belongs to Data Protection (OD-03, PDR-021). `Naravel.Queue.Encryption` optionally stores the payload of selected queue jobs encrypted.

## Read this first: the key ring

Data Protection keeps keys in a **key ring**. Anything encrypted here can be read only by a process that has the same key ring **and the same application name**.

- When the producer of a job and its worker are different applications or instances, they **must share the key ring and the application name**. Otherwise the worker cannot decrypt, and every marked job fails with `DecryptException`.
- Call `SetApplicationName(...)` with the same value in every application that shares data.
- Persist the key ring where all of them can read it (a shared directory, a database, Redis or blob storage) and protect it at rest: on some hosts Data Protection writes keys unencrypted by default.
- Data Protection creates a new key about every 90 days and keeps old keys to decrypt old data. Never delete the key ring while encrypted values or jobs may still exist.
- A separate key ring per instance (the default on a container with a throw-away file system) breaks decryption across instances and after every redeploy.

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("my-shop")
    .PersistKeysToFileSystem(new DirectoryInfo("/shared/keys"));   // or EF Core, Redis, blob storage
```

## Quickstart

```csharp
builder.Services.AddNaravelEncryption();   // calls AddDataProtection() only if you have not

var token = encrypter.Encrypt("secret");            // Laravel: Crypt::encryptString
var text  = encrypter.Decrypt(token);               // Crypt::decryptString
var card  = encrypter.Decrypt<CardInfo>(encrypter.Encrypt(new CardInfo("Ada", 4242)));   // Crypt::encrypt / decrypt
```

## Using it

### Values

`Encrypt(string)` returns URL-safe text. `Encrypt<T>` / `Decrypt<T>` store the value as JSON (`System.Text.Json`) inside the protected text; an overload takes a `JsonTypeInfo<T>` for source-generated metadata. All members are synchronous, because the Data Protection primitive is.

A value that cannot be decrypted (tampered, malformed, protected for another purpose, key missing) throws `DecryptException` with a fixed message that never contains the input or any plaintext. `TryDecrypt` returns `false` instead.

### Purposes

`encrypter.ForPurpose("billing")` returns an encrypter isolated under its own purpose: it cannot read what the default encrypter or another purpose wrote. The default purpose is `Naravel.Encryption.v1`. Use fixed names, never user input.

### Encrypted queue jobs (`Naravel.Queue.Encryption`)

```csharp
builder.Services.AddQueue(configuration).AddMemoryDriver(configuration);   // first
builder.Services.AddNaravelQueueEncryption();

public sealed class ChargeCard : Job, IShouldBeEncrypted { public string CardNumber { get; set; } = ""; ... }
```

- Only classes that implement the empty marker `IShouldBeEncrypted` are encrypted. Unmarked jobs keep the existing wire format; nothing is encrypted by default.
- A marked job is stored as `naravel:enc:v1:` + the Data Protection output of its normal payload, under the purpose `Naravel.Queue.Encryption.v1` (separate from the facade's purpose, so an `IEncrypter` value is never accepted as a job).
- The job type is still chosen only by the alias registry. Decryption happens afterwards, so decrypted data never selects a type (OD-07).
- Chains, batches and failed jobs already carry the stored string, so they carry ciphertext.
- An undecryptable marked job fails through the normal failed-job path with `DecryptException`; its message names the shared key ring and application name as the first thing to check. A marked job is never sent or run as plaintext. An unknown envelope version fails too.

**Migrating.** A job queued as plaintext before its class was marked is refused by default. To let in-flight jobs finish, set `AddNaravelQueueEncryption(o => o.AcceptPlaintextForMarkedJobs = true)` for a deploy window, then switch it off. If you remove the marker later, workers still decrypt messages that were stored encrypted.

### Testing

There is no `EncryptionFake`: a reversible fake would fake a cryptographic guarantee. Data Protection ships a real in-memory test double:

```csharp
services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
services.AddNaravelEncryption();
```

## Observability

`Meter` `Naravel.Encryption`, counter `naravel.encryption.operations`, tags `operation` (`encrypt`, `decrypt`) and `outcome` (`ok`, `invalid`). Values, tokens, purposes and key data are never tagged.

## How it is built

- `Encrypter` wraps one `IDataProtector` per purpose (`Naravel.Encryption.v1`, `.../<name>`). `Encrypt` is `Protect`, `Decrypt` is `Unprotect`; Data Protection's `CryptographicException` becomes `DecryptException`.
- `AddNaravelEncryption` registers Data Protection only if no `IDataProtectionProvider` exists, and `IEncrypter` as a singleton.
- `AddNaravelQueueEncryption` replaces the registered `IJobSerializer` with a decorator over it; call it after `AddQueue`.
- `Naravel.Encryption` references the full `Microsoft.AspNetCore.DataProtection` package.

## Not included (by decision)

Laravel-compatible ciphertext, an `APP_KEY` setting, algorithm choice, a static `Crypt::` facade, time-limited protection, an `EncryptionFake`, and encrypting every job by default.

## Laravel mapping

| Laravel | Naravel |
|---|---|
| `Crypt::encryptString($v)` / `decryptString($c)` | `encrypter.Encrypt(v)` / `Decrypt(c)` |
| `Crypt::encrypt($obj)` / `decrypt($c)` | `encrypter.Encrypt<T>(obj)` / `Decrypt<T>(c)` |
| `DecryptException` | `DecryptException` |
| `ShouldBeEncrypted` job | `IShouldBeEncrypted` job + `AddNaravelQueueEncryption()` |
| Data Protection `Protect` / `Unprotect` | `Encrypt` / `Decrypt` |
