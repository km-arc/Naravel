# PDR-013a — Mailable shape, named transports and queue adapter (R09.T01)

**Native-first verdict:** .NET still has no modern mail sender or message model, and nothing native gives a typed, testable "message as a class" or a way to pick a transport per environment. Naravel adds only the thin contract on top of [PDR-013](PDR-013-mail.md); the actual SMTP work stays in a separately installable MailKit package, as already approved.

Status: **PROPOSED 2026-10-14, awaiting owner approval.** No code before approval.

## Why an addendum

PDR-013 (approved 2026-10-07) fixed the boundary: `IMailer`, `IMailTransport`, `MailMessage`, `MailFake`, an optional MailKit adapter, no templating, **no driver manager unless several transports justify it**. The R09 stage plan (`roadmap/stages/R09-mail.md`) goes further: a `Mailable` class, `IMailer` on `Manager<,>`, built-in `log` and `array` transports, and a `QueueAsync` adapter. Those are decisions PDR-013 left open, so they need approval before R09.T02.

## Decision

### 1. Mailable shape

```csharp
public class WelcomeMail(User user) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.From("app@example.com").Subject("Welcome").Text($"Hi {user.Name}").Html($"<p>Hi {user.Name}</p>").Build();
}

await mailer.To(user.Email).SendAsync(new WelcomeMail(user), cancellationToken);
```

- `Mailable.Build` returns the explicit `MailMessage` from PDR-013. A mailable is just a typed factory for that message; no discovery, no convention lookup, no model magic.
- `IMailer` keeps `SendAsync(MailMessage)` (PDR-013) and adds the fluent `To(address)` returning a small `PendingMail` with `SendAsync(Mailable)`.
- **Why:** it matches Laravel's mental model (`Mail::to($user)->send(new Welcome($user))`) while staying plain C#. Build must be pure (no I/O), because the queue adapter calls it before enqueueing.
- Rendering views is **not** included. A `Mailable` may call an application-supplied `IMailRenderer` (`Task<string> RenderAsync(string template, object model, CancellationToken)`) that Naravel declares but does not implement, so no engine is mandated.

### 2. Named transports on `Manager<IMailTransport, MailOptions>`

Transports `smtp`, `log` and `array` are selected by name from configuration (`Mail:Default`, `Mail:Mailers:<name>`), with `Extend` and hot reload like Cache and Queue.

- **Why this reverses PDR-013's "no manager" caution:** with three supported transports and the common "log in development, SMTP in production, array in tests" pattern, the condition PDR-013 set ("unless multiple supported transports justify it") is met. An app with one transport only sets `Mail:Default`.
- `log`: writes the message through `ILogger` for development. It logs recipients and subject; the docs state it must not be used where logs are shared, because PDR-013 forbids recipient/content only in **telemetry**, not in an explicit dev logger.
- `array`: keeps sent messages in memory for tests and local runs. `MailFake` (in `Naravel.Mail.Testing`) implements `IMailer` for assertions (`AssertSent`, `AssertNothingSent`, `Reset`), the same way `QueueFake` does.

### 3. Queue adapter `Naravel.Mail.Queue`

```csharp
await mailer.To(user.Email).QueueAsync(new WelcomeMail(user), cancellationToken);
```

- `QueueAsync` calls `Build`, then enqueues a `SendQueuedMailJob` whose payload is the **built `MailMessage` data only** (addresses, subject, text/HTML). The mailable object itself is never serialized, so no CLR type names or delegates are stored (OD-07, PDR-013).
- The job uses normal `Job` retry/backoff. Provider quotas are handled by attaching the R08 `RateLimited` middleware, which the docs show as an optional recipe; `Naravel.Mail.Queue` has **no** dependency on `Naravel.Queue.Coordination`.
- Delivery is at-least-once, so a retry after an ambiguous failure can send a message twice. The docs say so and recommend an idempotency key at the transport when the provider supports one.
- The adapter references Queue; core `Naravel.Mail` does not (dependencies point one way).

### 4. Dependencies

`Naravel.Mail.Smtp` wraps MailKit (approved 2026-10-07). At R09.T03 I will propose the exact version, license (MIT) and `Directory.Packages.props` entry for confirmation before merging. No dependency enters core `Naravel.Mail`.

## Out of scope

Attachments, inline images, embedded template engine, provider-specific APIs (SES, SendGrid, ...), bounce/webhook handling, DKIM signing. Attachments can come later through their own PDR because they change the serialized queue payload.

## Alternatives considered

1. **Keep only `IMailer` + `MailMessage` (no Mailable).** Smallest, but loses the Laravel-familiar typed message class that the stage goal names, and leaves every app to write its own factories.
2. **Serialize the whole `Mailable` for queueing.** Rejected: it would store arbitrary CLR types in queue data and break when a class is renamed.
3. **Single DI-registered transport, no manager.** Works for one transport; it makes per-environment switching and runtime `Extend` ad-hoc, which Cache, Queue and Foundation already solve the same way.

## Verification required after approval

Tests for `Mailable` building, `To(...)` validation, transport selection/`Extend`/reload, `log` and `array` transports, `MailFake` assertions, cancellation and transport failure, queue adapter with the Memory queue (payload contains no type names), and an env-gated SMTP test (`NARAVEL_TEST_SMTP`). EN/FA docs, parity table, changelog, stage checklist, solution verification and `roadmap/check.py`. Anything not run is reported as NOT RUN.

## Approval

Not yet approved. Open questions for the owner:

- (a) Is the `Mailable` + `To(...).SendAsync(...)` shape accepted?
- (b) Do you accept reversing PDR-013's "no manager" caution in favor of named transports (`smtp`/`log`/`array`)?
- (c) Does `QueueAsync` serialize the built message only (recommended), as described?
- (d) Are attachments deferred to a later PDR?
