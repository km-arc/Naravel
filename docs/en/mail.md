# Mail

`Naravel.Mail` is a typed mail contract: a `Mailable` class describes a message, a named *transport* delivers it, and a fake makes it testable. The core package has **no network dependency**; SMTP lives in the optional `Naravel.Mail.Smtp` package and queued sending in the optional `Naravel.Mail.Queue` adapter (both are available now).

Decisions: [PDR-013](../pdr/en/PDR-013-mail.md) and [PDR-013a](../pdr/en/PDR-013a-mail-mailable-and-transports.md). Persian version: [docs/fa/mail.md](../fa/mail.md).

## Quickstart

```sh
dotnet add package Naravel.Mail
```

```json
{ "Mail": { "From": "app@example.com" } }
```

```csharp
using Naravel.Mail;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelMail(builder.Configuration);   // Mail:Default is "log": the message is logged, nothing is delivered

var app = builder.Build();

app.MapPost("/welcome", async (string email, string name, IMailer mailer, CancellationToken ct) =>
{
    await mailer.To(email).SendAsync(new WelcomeMail(name), ct);
    return Results.Accepted();
});

app.Run();

public sealed class WelcomeMail(string name) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();
}
```

`POST /welcome?email=ada@example.com&name=Ada` answers `202 Accepted`, and the application log shows one line with the sender, the recipient and the subject. To deliver for real, point `Mail:Default` at an SMTP transport ([below](#smtp-naravelmailsmtp)).

Without `Mail:From`, call `mail.From("app@example.com")` inside `Build`; a mail with no sender throws `InvalidOperationException` that names `Mail:From`.

## Using it

### Sending a prepared message

`IMailer.SendAsync(MailMessage)` sends an explicit message with no mailable:

```csharp
await mailer.SendAsync(new MailMessage("app@example.com", "user@example.com", "Receipt", "Thanks!"), ct);
```

`MailMessage` is immutable. Its constructors validate every address (`Name <user@example.com>` is accepted), require at least one recipient and at least one body (text, HTML or both), and reject a `null` subject (an empty subject is allowed). Invalid input throws `ArgumentException` at construction, before any transport is touched.

### Mailables

`Mailable.Build(MailBuilder)` returns the message. The builder already holds the recipients passed to `To(...)` and the configured default sender; it also offers `To`, `Cc`, `Bcc`, `From`, `Subject`, `Text`, `Html`, and `Build()`.

- `Build` **must be pure**: no I/O, no clock reads, no randomness. The queue adapter calls it before enqueueing and stores only the resulting data.
- `mailer.To(a).To(b)` adds recipients; `To(...)` returns a `PendingMail` with `SendAsync(Mailable)` and `QueueAsync(Mailable)`.
- A `Build` that returns `null` throws `InvalidOperationException` naming the class.

Copies and a sender of your own:

```csharp
public sealed class MonthlyReport : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.From("reports@example.com")               // overrides Mail:From for this mail
            .Cc("manager@example.com")
            .Bcc("archive@example.com")                // delivery only; never written into the headers
            .Subject("Monthly report")
            .Text("Your monthly report is ready.")
            .Build();
}
```

### Rendering views

Naravel mandates no template engine. If you have one, implement the declared `IMailRenderer` (`Task<string> RenderAsync(string template, object model, CancellationToken)`), render **before** constructing the mailable, and pass the string in:

```csharp
var html = await renderer.RenderAsync("welcome", model, ct);
await mailer.To(user.Email).SendAsync(new WelcomeMail(user, html), ct);
```

### Transports

Pick a transport by name in configuration. Names under `Mail:Mailers` select a `Driver`:

```json
{
  "Mail": {
    "Default": "dev",
    "From": "app@example.com",
    "Mailers": {
      "dev":   { "Driver": "log" },
      "test":  { "Driver": "array" }
    }
  }
}
```

| Driver | What it does | Package |
|---|---|---|
| `log` | Writes sender, recipients and subject to `ILogger`; delivers nothing. The body is not logged. Recipients are visible in logs, so do not use it where logs are shared. | `Naravel.Mail` |
| `array` | Keeps every sent message in memory (`ArrayMailTransport.Sent`) for tests and local runs. | `Naravel.Mail` |
| `smtp` | Sends through MailKit over TLS. | `Naravel.Mail.Smtp` |

### SMTP (`Naravel.Mail.Smtp`)

```csharp
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelMailSmtp(builder.Configuration);   // after AddNaravelMail
```

```json
{
  "Mail": {
    "Default": "relay",
    "From": "app@example.com",
    "Mailers": {
      "relay": { "Driver": "smtp", "Host": "smtp.example.com", "Port": 587, "Security": "StartTls", "Username": "app", "Timeout": 30 }
    }
  }
}
```

| Key | Meaning | Default |
|---|---|---|
| `Host` | SMTP server. **Required**; a missing value fails with a message naming the key. | none |
| `Port` | Server port. | `587` |
| `Security` | `StartTls` (TLS required, port 587), `SslOnConnect` (implicit TLS, port 465) or `None` (clear text, local test servers only). | `StartTls` |
| `Username` / `Password` | Credentials. Without `Username` no authentication is attempted. Put the password in user secrets or `Mail__Mailers__relay__Password`, never in a committed file. | none |
| `Timeout` | Seconds for each network operation. | `30` |

Behavior you can rely on:

- TLS is **required by default**. Naravel never falls back to clear text silently; you must write `"Security": "None"`.
- Each send opens its own connection, authenticates, sends and disconnects (no pooling), so concurrent sends are safe.
- A server rejection (for example `550` for an unknown recipient) or a network failure is thrown to the caller as the MailKit exception. Nothing is retried inside the transport. Queued mail is retried by the queue job (`MaxAttempts`, backoff), which means a send whose outcome was unclear can be delivered twice.
- `Bcc` recipients are used for delivery only and never appear in headers. Header values are encoded by MimeKit, so a subject containing line breaks cannot inject a header.
- The package depends on MailKit (MIT, pinned to 4.18.1; 4.16.0 fixed CVE-2026-41319) and raises its transitive `System.Security.Cryptography.Pkcs` to 10.0.13 (fix for CVE-2026-70162 and CVE-2026-84008). Core `Naravel.Mail` does not.

`log` and `array` also exist under those exact names without any configuration. `IMailer` (injected) uses the default transport and resolves it on every send, so changing `Mail:Default` at runtime takes effect without re-injecting. To use another transport explicitly:

```csharp
await mailManager.Mailer("test").To(user.Email).SendAsync(new WelcomeMail(user), ct);
```

Add your own at runtime with `Extend`:

```csharp
mailManager.Extend("custom", sp => new MyTransport());
```

A transport implements `IMailTransport.SendAsync(MailMessage, CancellationToken)`: return normally only when the message was accepted, throw otherwise. Naravel never swallows a transport failure and never retries a send for you, because a retry after an ambiguous failure can deliver twice.

### Queued sending (`Naravel.Mail.Queue`)

`mailer.To(user.Email).QueueAsync(new WelcomeMail(user))` builds the mailable and passes the resulting **message data** to the `IMailQueue` seam implemented by `Naravel.Mail.Queue`. Until that package is registered, `QueueAsync` throws `InvalidOperationException` that says what to install. The mailable object itself is never serialized, so no CLR type name or delegate is ever stored in queue data.

```csharp
builder.Services.AddQueue(builder.Configuration).AddRedisDriver(builder.Configuration); // any queue driver
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelMailQueue(options =>        // placement is optional
{
    options.Connection = "redis";                       // default: the queue module's default connection
    options.Queue = "emails";                           // default: "default"
});
// worker process only:
builder.Services.AddQueueWorker(options => { options.Connection = "redis"; options.Queues = ["emails"]; });
```

How it works:

- `AddNaravelMailQueue()` registers `IMailQueue` and the `SendQueuedMailJob` under the stable alias `naravel.mail.send`. It does not register a queue or a worker; you add those as for any job.
- The stored payload is plain data: `From`, `To`, `Cc`, `Bcc`, `Subject`, `TextBody`, `HtmlBody` and the transport name (`null` = default). The worker rebuilds a `MailMessage` (every address is validated again) and sends it through `MailManager.Mailer(transport)`.
- The transport is chosen by name at enqueue time: `manager.Mailer("smtp").To(a).QueueAsync(...)` stores `smtp`, and the worker uses the same name even if `Mail:Default` changed meanwhile.
- **Delivery is at-least-once.** A worker that stops after the transport accepted the message but before the job is acknowledged causes a second delivery. Failed sends are retried with the standard job `MaxAttempts` and backoff. Naravel does not de-duplicate mail; use `IUniqueJob`-style protection in your own code when a duplicate would hurt.
- Attachments are not part of the payload yet (see PDR-013a); a future PDR will decide how files are referenced.


### Testing

```csharp
var fake = new MailFake { DefaultFrom = "app@example.com" };
var service = new SignupService(fake);          // takes IMailer

await service.RegisterAsync("ada@example.com");

fake.AssertSent(m => m.To.Contains("ada@example.com") && m.Subject == "Welcome");
fake.AssertSentCount(1);
fake.AssertNothingQueued();
```

`MailFake` is in `Naravel.Mail.Testing` and implements `IMailer`. It records `Sent` and `Queued` messages, offers `AssertSent`, `AssertSentCount`, `AssertQueued`, `AssertNothingSent`, `AssertNothingQueued` and `Reset`, and throws `InvalidOperationException` with a readable message when an assertion fails. To inspect real `MailManager` sends instead, use the `array` transport.

## Configuration reference

| Key | Meaning | Default |
|---|---|---|
| `Mail:Default` | Transport name used by the injected `IMailer`. | `log` |
| `Mail:From` | Sender used when a mailable does not call `From(...)`. | none |
| `Mail:Mailers:<name>:Driver` | `log`, `array`, or a driver from an installed package. | - |
| `Mail:Mailers:<name>:*` | Driver-specific settings. A change rebuilds only that transport; unrelated keys never do. | - |

## Errors

| When | Exception and message |
|---|---|
| The mail has no sender (no `Mail:From` and no `From(...)`) | `InvalidOperationException`: `The mail has no sender. Call From(...) in Mailable.Build or set Mail:From in configuration.` |
| `Mailable.Build` returns `null` | `InvalidOperationException`: `WelcomeMail.Build returned null. Return mail.Build().` (the class name is yours) |
| An address is not valid | `ArgumentException`: `'not-an-address' is not a valid e-mail address.` |
| There is no recipient | `ArgumentException`: `A mail message needs at least one recipient.` |
| There is no text and no HTML body | `ArgumentException`: `A mail message needs a text body, an HTML body, or both.` |
| `QueueAsync` without the queue adapter | `InvalidOperationException`: `No mail queue adapter is registered. Install Naravel.Mail.Queue and call services.AddNaravelMailQueue(), or use SendAsync.` |
| A transport fails to deliver | The transport's own exception, unchanged; nothing is retried for you. |

`ArgumentException` messages end with ` (Parameter 'to')` or the name of the offending argument. The three `ArgumentException` cases are raised when the `MailMessage` is built, before any transport is touched.

The `smtp` transport reports a bad setting the first time it is used, naming the key:

| Setting | Message |
|---|---|
| No `Host` | `The smtp mail transport needs a Host (Mail:Mailers:<name>:Host).` |
| `Port` not a number | `Mail:Mailers:relay:Port 'abc' is not a number.` |
| `Port` outside 1 to 65535 | `The smtp mail transport Port 70000 is not between 1 and 65535.` |
| `Security` not recognised | `Mail:Mailers:relay:Security 'x' must be StartTls, SslOnConnect or None.` |
| `Timeout` not a positive number | `Mail:Mailers:relay:Timeout 'x' must be a positive number of seconds.` |

Foundation's own errors (an unknown transport name, no default) are in the [Foundation guide](foundation.md#errors).

`MailFake` assertions throw `InvalidOperationException`:

| Assertion | Message |
|---|---|
| `AssertSent()` when nothing was sent | `No mail message was sent.` |
| `AssertSent(predicate)` when nothing matches | `No matching mail message was sent (1 sent in total).` |
| `AssertSentCount(2)` when one was sent | `Expected 2 sent mail message(s), but found 1.` |
| `AssertNothingSent()` when one was sent | `Expected no mail to be sent, but 1 message(s) were sent.` |
| `AssertNothingQueued()` when one was queued | `Expected no mail to be queued, but 1 message(s) were queued.` |

`AssertQueued` has the same two messages with `queued` in place of `sent`.

`tests/Naravel.Mail.Tests/DocsExampleTests.cs` runs the quickstart mailable, the copies example and every message above except the `smtp` table, whose settings are covered by `tests/Naravel.Mail.Smtp.Tests`. Change one, change the other.

## Complete example

A mailable, a service that sends it, the registration and a test with `MailFake`. `SignupRequest` is your own request type.

```csharp
public sealed class WelcomeMail(string name) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();   // pure: no I/O, no clock
}

public sealed class SignupService(IMailer mailer)
{
    public Task RegisterAsync(string email, string name, CancellationToken ct = default) =>
        mailer.To(email).SendAsync(new WelcomeMail(name), ct);
}

// Program.cs: Mail:Default = "log" in development, so nothing is delivered; switch the transport in configuration for production.
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddScoped<SignupService>();

app.MapPost("/signup", async (SignupRequest body, SignupService signup, CancellationToken ct) =>
{
    await signup.RegisterAsync(body.Email, body.Name, ct);
    return Results.Accepted();
});

// Test
[Fact]
public async Task Register_sends_a_welcome_mail()
{
    var fake = new MailFake { DefaultFrom = "app@example.com" };
    await new SignupService(fake).RegisterAsync("ada@example.com", "Ada");

    fake.AssertSent(m => m.To.Contains("ada@example.com") && m.Subject == "Welcome");
    fake.AssertSentCount(1);
    fake.AssertNothingQueued();
}
```

## Observability

`Meter` and `ActivitySource` are both named `Naravel.Mail`. Counters `naravel.mail.sent` and `naravel.mail.failed` carry one tag, `transport` (the configured name). Activity `mail.send` carries the same name. Recipients, subjects and bodies never appear in telemetry.

## How it is built

- `MailManager` derives from `Manager<IMailTransport, MailOptions>` ([Foundation](architecture.md)), so driver caching, runtime `Extend`, disposal and configuration reload are shared with Cache, Queue and Filesystem; nothing is reimplemented.
- `MailOptions` binds `Mail:Default`, `Mail:From` and `Mail:Mailers`. `Mailers` is the same dictionary the manager reads as `Stores`.
- `MailManager.Mailer(name)` returns a small `IMailer` bound to a transport *name*. It resolves the transport per send, validates nothing itself (messages validate in their constructor), records telemetry, and rethrows failures. `OperationCanceledException` is not counted as a failure.
- `PendingMail` builds the mailable with a `MailBuilder` pre-filled with recipients and the default sender, then calls the mailer.
- `AddMailTransport(...)` is the hook transport packages use: it registers one factory per `Mail:Mailers:<name>` whose `Driver` matches, so each name gets its own instance.
- Core `Naravel.Mail` depends on Foundation and `Microsoft.Extensions.*` abstractions only. It does not reference Queue; `IMailQueue` is the seam.

## Not included (by decision)

Attachments and inline images (they change the queue payload and need their own PDR), a template engine, provider-specific APIs (SES, SendGrid, ...), bounce/webhook handling and DKIM signing. See PDR-013a "Out of scope".

## Laravel mapping

| Laravel | Naravel |
|---|---|
| `Mail::to($u)->send(new Welcome($u))` | `mailer.To(u.Email).SendAsync(new WelcomeMail(u))` |
| `Mailable::build()` | `Mailable.Build(MailBuilder)` |
| `Mail::mailer('smtp')` | `mailManager.Mailer("smtp")` |
| `Mail::fake()` / `assertSent` | `MailFake` / `AssertSent` |
| `config/mail.php` `default`, `mailers`, `from` | `Mail:Default`, `Mail:Mailers`, `Mail:From` |
| `Mail::raw`, facades, `__call` | Not ported |
