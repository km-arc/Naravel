# Mail

`Naravel.Mail` is a typed mail contract: a `Mailable` class describes a message, a named *transport* delivers it, and a fake makes it testable. The core package has **no network dependency**; SMTP lives in the optional `Naravel.Mail.Smtp` package and queued sending in the optional `Naravel.Mail.Queue` adapter (both arrive with R09.T03; this page marks what is available today).

Decisions: [PDR-013](../pdr/en/PDR-013-mail.md) and [PDR-013a](../pdr/en/PDR-013a-mail-mailable-and-transports.md). Persian version: [docs/fa/mail.md](../fa/mail.md).

## Quickstart

```csharp
builder.Services.AddNaravelMail(builder.Configuration);   // Mail:Default defaults to "log": nothing is delivered

public class WelcomeMail(User user) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {user.Name}").Html($"<p>Hi {user.Name}</p>").Build();
}

await mailer.To(user.Email).SendAsync(new WelcomeMail(user), cancellationToken);   // mailer is an injected IMailer
```

```json
{ "Mail": { "Default": "log", "From": "app@example.com" } }
```

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
| `smtp` | Sends through MailKit. | `Naravel.Mail.Smtp` (R09.T03, not yet available) |

`log` and `array` also exist under those exact names without any configuration. `IMailer` (injected) uses the default transport and resolves it on every send, so changing `Mail:Default` at runtime takes effect without re-injecting. To use another transport explicitly:

```csharp
await mailManager.Mailer("test").To(user.Email).SendAsync(new WelcomeMail(user), ct);
```

Add your own at runtime with `Extend`:

```csharp
mailManager.Extend("custom", sp => new MyTransport());
```

A transport implements `IMailTransport.SendAsync(MailMessage, CancellationToken)`: return normally only when the message was accepted, throw otherwise. Naravel never swallows a transport failure and never retries a send for you, because a retry after an ambiguous failure can deliver twice.

### Queued sending (R09.T03, not yet available)

`mailer.To(user.Email).QueueAsync(new WelcomeMail(user))` builds the mailable and passes the resulting **message data** to the `IMailQueue` seam implemented by `Naravel.Mail.Queue`. Until that package is registered, `QueueAsync` throws `InvalidOperationException` that says what to install. The mailable object itself is never serialized, so no CLR type name or delegate is ever stored in queue data.

### Testing

```csharp
var fake = new MailFake { DefaultFrom = "app@example.com" };
var service = new SignupService(fake);          // takes IMailer

await service.RegisterAsync("ada@example.com");

fake.AssertSent(m => m.To.Contains("ada@example.com") && m.Subject == "Welcome");
fake.AssertSent(1);
fake.AssertNothingQueued();
```

`MailFake` is in `Naravel.Mail.Testing` and implements `IMailer`. It records `Sent` and `Queued` messages, offers `AssertSent`, `AssertQueued`, `AssertNothingSent`, `AssertNothingQueued` and `Reset`, and throws `InvalidOperationException` with a readable message when an assertion fails. To inspect real `MailManager` sends instead, use the `array` transport.

## Configuration reference

| Key | Meaning | Default |
|---|---|---|
| `Mail:Default` | Transport name used by the injected `IMailer`. | `log` |
| `Mail:From` | Sender used when a mailable does not call `From(...)`. | none |
| `Mail:Mailers:<name>:Driver` | `log`, `array`, or a driver from an installed package. | - |
| `Mail:Mailers:<name>:*` | Driver-specific settings. A change rebuilds only that transport; unrelated keys never do. | - |

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
