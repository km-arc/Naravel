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

A minimal API that seals a text and opens it again. Nothing needs to be installed or started besides the package.

```sh
dotnet new web -n EncryptionDemo
cd EncryptionDemo
dotnet add package Naravel.Encryption
```

Replace `Program.cs`:

```csharp
using Naravel.Encryption;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelEncryption();   // calls AddDataProtection() only if you have not
var app = builder.Build();

app.MapPost("/seal", (string text, IEncrypter encrypter) => new { token = encrypter.Encrypt(text) });   // Laravel: Crypt::encryptString

app.MapGet("/open/{token}", (string token, IEncrypter encrypter) =>                                      // Crypt::decryptString
    encrypter.TryDecrypt(token, out var text) ? Results.Ok(new { text }) : Results.BadRequest());

app.Run();
```

Try it, with `$URL` set to the address `dotnet run` prints:

```sh
curl -X POST "$URL/seal?text=hello"      # {"token":"<URL-safe text>"}
curl "$URL/open/<token>"                 # {"text":"hello"}
curl "$URL/open/garbage"                 # 400 Bad Request
```

This uses the key ring that Data Protection chooses by default, which is enough to try it. Set one up as described above before you deploy. For typed values (`Crypt::encrypt`) see [Values](#values).

## Using it

### Values

`Encrypt(string)` returns URL-safe text. `Encrypt<T>` / `Decrypt<T>` store the value as JSON (`System.Text.Json`) inside the protected text; an overload takes a `JsonTypeInfo<T>` for source-generated metadata. All members are synchronous, because the Data Protection primitive is.

A value that cannot be decrypted (tampered, malformed, protected for another purpose, key missing) throws `DecryptException` with a fixed message that never contains the input or any plaintext. `TryDecrypt` returns `false` instead; it exists for strings only, so around `Decrypt<T>` you catch `DecryptException`. See [Errors](#errors).

**Trimming and Native AOT.** `Encrypt<T>(T)` and `Decrypt<T>(string)` use reflection-based JSON, so they carry `RequiresUnreferencedCode` and `RequiresDynamicCode`: in a project that enables the trim or AOT analyzers (`PublishAot`, `IsTrimmable`) every call site gets warning IL2026 and IL3050. Strings (`Encrypt(string)`, `Decrypt(string)`) are not affected. For typed values in such a project, pass the metadata of a source-generated context to the `JsonTypeInfo` overloads:

```csharp
[JsonSerializable(typeof(CardInfo))]
public partial class AppJson : JsonSerializerContext;

var token = encrypter.Encrypt(new CardInfo("Ada", 4242), AppJson.Default.CardInfo);
var card  = encrypter.Decrypt(token, AppJson.Default.CardInfo);
```

The two forms read each other's tokens. See [Trimming and Native AOT](trimming-aot.md).

### Purposes

`encrypter.ForPurpose("billing")` returns an encrypter isolated under its own purpose: it cannot read what the default encrypter or another purpose wrote. The default purpose is `Naravel.Encryption.v1`. Use fixed names, never user input.

### Encrypted queue jobs (`Naravel.Queue.Encryption`)

Add it to the app of the [queue quickstart](queue.md#quickstart); the job below replaces its `SendWelcomeEmailJob`.

```csharp
using Naravel.Queue.Encryption;   // IShouldBeEncrypted
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;

builder.Services.AddQueue(builder.Configuration).AddMemoryDriver(builder.Configuration);   // first
builder.Services.AddJob<ChargeCard>("charge-card");
builder.Services.AddQueueWorker();
builder.Services.AddNaravelQueueEncryption();                                              // after AddQueue

public sealed class ChargeCard : Job, IShouldBeEncrypted
{
    public string CardNumber { get; set; } = "";

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;   // your work
}
```

- Only classes that implement the empty marker `IShouldBeEncrypted` are encrypted. Unmarked jobs keep the existing wire format; nothing is encrypted by default.
- A marked job is stored as `naravel:enc:v1:` + the Data Protection output of its normal payload, under the purpose `Naravel.Queue.Encryption.v1` (separate from the facade's purpose, so an `IEncrypter` value is never accepted as a job).
- The job type is still chosen only by the alias registry. Decryption happens afterwards, so decrypted data never selects a type (OD-07).
- Chains, batches and failed jobs already carry the stored string, so they carry ciphertext.
- An undecryptable marked job fails through the normal failed-job path with `DecryptException`; its message names the shared key ring and application name as the first thing to check. Where `AddNaravelQueueEncryption` is registered, a marked job is never sent or run as plaintext; register it in every application that dispatches marked jobs and in every worker, because a dispatching application without it sends them as plaintext. An unknown envelope version fails too. The messages are in [Errors](#queue-jobs-naravelqueueencryption).

**Migrating.** A job queued as plaintext before its class was marked is refused by default. To let in-flight jobs finish, set `AddNaravelQueueEncryption(o => o.AcceptPlaintextForMarkedJobs = true)` for a deploy window, then switch it off. If you remove the marker later, workers still decrypt messages that were stored encrypted.

## Errors

The messages below are the ones in the source. .NET adds ` (Parameter 'value')` to the message of an argument exception, which the table leaves out. `Naravel.Encryption` writes no log lines: an unreadable value is either an exception or, with `TryDecrypt`, a `false` and the `invalid` outcome of the [metric](#observability).

| When | Exception and message |
|---|---|
| `AddNaravelEncryption` with a null `services` | `ArgumentNullException`, parameter `services` |
| `Encrypt(null)` | `ArgumentNullException`, parameter `value` |
| `Decrypt(null)`, `TryDecrypt(null, out _)` or `Decrypt<T>(null)` | `ArgumentNullException`, parameter `payload` |
| `Encrypt<T>` or `Decrypt<T>` with a null `JsonTypeInfo` | `ArgumentNullException`, parameter `typeInfo` |
| `ForPurpose` with a null, empty or blank name | `ArgumentNullException` for null, `ArgumentException` for the others; the framework's message |
| A token that cannot be opened: text that is not a token (the empty string too), a token changed after it was made, one made under another purpose, another application name or another key ring, or one whose key is gone | `DecryptException`: `The value could not be decrypted. First check that every application and instance that reads it shares the same Data Protection key ring and application name; then check that it is not malformed or tampered with and was written under the same purpose.` `TryDecrypt` returns `false` and sets `value` to `null` instead |
| `Decrypt<T>` of a token that opens but whose text is not JSON for `T` | `DecryptException`: `The value was decrypted but is not valid for the requested type.` |

A token is opened before its type is read, so `Decrypt<T>` of a token that cannot be opened gives the first message, not the second. Every cause in the table is covered by a test except the lost key, which is read from Data Protection's own behavior.

**What a `DecryptException` carries.** It derives from `Exception` directly, so catch it by its own type. Neither message contains the token, the decrypted text or a purpose. When Data Protection reported the failure with a `CryptographicException`, that is the inner exception (text that is not valid base64 has none), and the type mismatch has none at all, because a JSON error can quote a part of the plaintext.

**Not wrapped.** `Encrypt` does not catch what the Data Protection protector throws, so a failure of the key ring on the way in (it cannot create or write a key) reaches you as Data Protection's own exception, not as a `DecryptException`. No test makes the key ring fail, so this is read from the source.

**No error at all.** `TryDecrypt` with a token that cannot be opened (only the metric counts it), `Encrypt("")` (the empty string round-trips), `ForPurpose` with the same name twice (the two encrypters read each other's tokens), and `AddNaravelEncryption` more than once (one `IEncrypter`; a Data Protection registration of your own is kept).

### Queue jobs (`Naravel.Queue.Encryption`)

| When | Exception and message |
|---|---|
| `AddNaravelQueueEncryption` with a null `services` | `ArgumentNullException`, parameter `services` |
| `AddNaravelQueueEncryption` before `AddQueue`, so that no `IJobSerializer` is registered | `InvalidOperationException`: `No IJobSerializer is registered. Call AddQueue before AddNaravelQueueEncryption.` |

A worker that reads a job throws a `DecryptException` (the facade's type) with one of these messages. The alias registry has chosen the job class by then; the payload is read after that.

| The stored payload | Message |
|---|---|
| Starts with `naravel:enc:v1:` and cannot be opened: another key ring or application name, a changed text, text that is not base64 after the prefix, another purpose, or a value made by `IEncrypter` | `A queued job payload could not be decrypted. First check that this worker and the application that queued the job share the same Data Protection key ring and application name; then check that the payload was not altered and that every worker runs a version that understands the envelope.` |
| Starts with `naravel:enc:` and any other version, such as `naravel:enc:v9:` | `A queued job payload uses an encryption envelope version this worker does not understand. Update the worker, or check that the producer and worker run compatible versions and share the same Data Protection key ring and application name.` |
| Has no envelope, the job class implements `IShouldBeEncrypted`, and `AcceptPlaintextForMarkedJobs` is `false` (the default) | `A job of a class marked IShouldBeEncrypted was stored as plaintext and was refused. If it was queued before the class was marked, let it drain or set QueueEncryptionOptions.AcceptPlaintextForMarkedJobs for a deploy window; otherwise treat the message as untrusted.` |

None of the messages contains the payload. When Data Protection reported the failure with a `CryptographicException`, it is the inner exception of the first message. Every cause of the first row is covered by a test of the adapter except another application name, which the facade's test covers and the adapter reads through the same Data Protection provider.

**What happens to the job.** The exception is thrown while the worker reads the payload, before the job runs. It takes the path of any exception that is thrown before a job runs: the attempt is counted, the job is retried with its backoff, and when `MaxAttempts` is reached it is recorded as a failed job. The record keeps the stored payload, which is still ciphertext, and the text of the exception, which names the key ring first. A key ring that differs fails the same way on every attempt, so the retries only delay the record. The test of this guide uses one attempt; the retry is read from `QueueWorkerService`.

**The two sides must agree.** A dispatching application without `AddNaravelQueueEncryption` raises nothing and sends a marked job as plaintext; a worker that has the registration then refuses it with the third message (with `AcceptPlaintextForMarkedJobs` it runs the job). A worker without the registration reads an envelope as ordinary JSON and fails with the JSON serializer's own `JsonException`, which does not mention encryption, so check that every worker registers it.

**No error at all.** A second call of `AddNaravelQueueEncryption` does nothing, so its `configure` is not run: call it once, with the options. An unmarked job keeps the existing wire format. An unmarked class still reads a payload that was stored encrypted.

## Testing

There is no `EncryptionFake`: a reversible fake would fake a cryptographic guarantee. Data Protection ships a real in-memory test double, `EphemeralDataProtectionProvider`. Register it before `AddNaravelEncryption` and the library keeps it, because it adds Data Protection only when none is registered. A helper that builds an `IEncrypter` over a key ring of its own:

```csharp
static IEncrypter NewEncrypter(IDataProtectionProvider? keys = null)
{
    var services = new ServiceCollection();
    services.AddSingleton<IDataProtectionProvider>(keys ?? new EphemeralDataProtectionProvider());   // without `keys`, a new key ring
    services.AddNaravelEncryption();
    return services.BuildServiceProvider().GetRequiredService<IEncrypter>();
}
```

Round trips, a reader that cannot read, and a reader that shares the key ring:

```csharp
[Fact]
public void A_value_round_trips_and_the_token_is_not_the_plaintext()
{
    var encrypter = NewEncrypter();

    var token = encrypter.Encrypt("secret");

    token.Should().NotContain("secret");
    encrypter.Decrypt(token).Should().Be("secret");
}

[Fact]
public void A_reader_with_another_key_ring_is_refused()
{
    var token = NewEncrypter().Encrypt("secret");
    var other = NewEncrypter();     // another application, or the same one after it lost its keys

    other.TryDecrypt(token, out _).Should().BeFalse();
    var act = () => other.Decrypt(token);
    act.Should().Throw<DecryptException>().Which.Message.Should().Contain("key ring");
}

[Fact]
public void A_reader_that_shares_the_key_ring_agrees()
{
    var keys = new EphemeralDataProtectionProvider();

    var token = NewEncrypter(keys).Encrypt("secret");

    NewEncrypter(keys).Decrypt(token).Should().Be("secret");
}
```

**Queue jobs.** Build the serializer that your application registers over an in-memory key ring, and look at what is stored:

```csharp
static IJobSerializer NewSerializer(IDataProtectionProvider? keys = null)
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["NaravelQueue:Default"] = "local",
        ["NaravelQueue:Stores:local:Driver"] = "memory"
    }).Build();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IDataProtectionProvider>(keys ?? new EphemeralDataProtectionProvider());
    services.AddQueue(configuration).AddMemoryDriver(configuration);   // your application's queue registrations
    services.AddJob<ChargeCard>("charge-card");
    services.AddNaravelQueueEncryption();
    return services.BuildServiceProvider().GetRequiredService<IJobSerializer>();
}

[Fact]
public void A_charge_card_job_is_stored_as_an_envelope_and_reads_back()
{
    var serializer = NewSerializer();

    var stored = serializer.Serialize(new ChargeCard { CardNumber = "4242424242424242" });

    stored.Should().StartWith("naravel:enc:v1:").And.NotContain("4242");
    ((ChargeCard)serializer.Deserialize(stored, typeof(ChargeCard))).CardNumber.Should().Be("4242424242424242");
}

[Fact]
public void A_worker_reads_the_job_only_when_it_shares_the_key_ring()
{
    var keys = new EphemeralDataProtectionProvider();
    var stored = NewSerializer(keys).Serialize(new ChargeCard { CardNumber = "4242424242424242" });

    NewSerializer(keys).Deserialize(stored, typeof(ChargeCard)).Should().BeOfType<ChargeCard>();

    var act = () => NewSerializer().Deserialize(stored, typeof(ChargeCard));   // a new key ring
    act.Should().Throw<DecryptException>().Which.Message.Should().Contain("key ring");
}
```

To see a failed job end to end, push a `QueuedMessage` whose `Payload` you changed to the driver (`QueueManager.Connection(...).PushAsync`), set its `MaxAttempts` to `1`, start the worker's hosted services, and read `IFailedJobStore`: the record holds the changed payload and an `Error` that contains the `DecryptException` text.

Code that handles a `DecryptException`, such as the invite endpoint of the [complete example](#complete-example), is tested the same way: make a token with one encrypter, change or replace it, and assert the answer.

## Complete example

A typed secret stored encrypted per tenant, an invite token that rejects tampering, and a test with the in-memory Data Protection provider. `IIntegrationStore` is your own data access.

```csharp
using Naravel.Encryption;

public sealed record ApiCredentials(string Key, string Secret);

// Program.cs: the key ring is shared and persisted (see "Read this first").
builder.Services.AddDataProtection().SetApplicationName("my-shop").PersistKeysToFileSystem(new DirectoryInfo("/shared/keys"));
builder.Services.AddNaravelEncryption();

// 1. Store a typed secret encrypted: the database column holds only the token.
public sealed class IntegrationService(IEncrypter encrypter, IIntegrationStore store)
{
    private readonly IEncrypter _secrets = encrypter.ForPurpose("integrations");     // a fixed name, never user input

    public Task SaveAsync(int tenantId, ApiCredentials credentials, CancellationToken ct) =>
        store.SetTokenAsync(tenantId, _secrets.Encrypt(credentials), ct);              // Crypt::encrypt

    public async Task<ApiCredentials?> LoadAsync(int tenantId, CancellationToken ct)
    {
        var token = await store.GetTokenAsync(tenantId, ct);
        return token is not null && _secrets.TryDecrypt(token, out var json)           // no exception for a bad value
            ? System.Text.Json.JsonSerializer.Deserialize<ApiCredentials>(json!)
            : null;
    }
}

// 2. An opaque value for a link or a cookie. Anyone who tampers with it gets a DecryptException.
app.MapGet("/invite/{token}", (string token, IEncrypter encrypter) =>
{
    try
    {
        var email = encrypter.ForPurpose("invites").Decrypt(token);                   // Crypt::decryptString
        return Results.Ok(new { email });
    }
    catch (DecryptException)
    {
        return Results.BadRequest("invalid invite");                                   // the message never contains the input
    }
});

app.MapPost("/invite", (string email, IEncrypter encrypter) =>
    Results.Ok(new { token = encrypter.ForPurpose("invites").Encrypt(email) }));      // URL-safe text
```

```csharp
// 3. Test: a real in-memory Data Protection provider, no fake cryptography.
[Fact]
public void Invite_token_round_trips_and_purposes_are_isolated()
{
    var services = new ServiceCollection();
    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    services.AddNaravelEncryption();
    var encrypter = services.BuildServiceProvider().GetRequiredService<IEncrypter>();

    var token = encrypter.ForPurpose("invites").Encrypt("ada@example.com");

    encrypter.ForPurpose("invites").Decrypt(token).Should().Be("ada@example.com");
    encrypter.ForPurpose("billing").TryDecrypt(token, out _).Should().BeFalse();       // another purpose cannot read it
}
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
