# PDR-013 — Mail (`Naravel.Mail`)

**Native-first verdict:** .NET has no modern, general-purpose mail sender; `System.Net.Mail.SmtpClient` is limited and should not be the default for new code. Naravel may add a typed mail contract, but any modern transport dependency must remain optional and receive explicit approval.

Status: **Approved 2026-10-07.**

## Context

Laravel provides a mailer and mailable abstraction, while .NET applications currently choose a provider directly. A small typed facade can make messages testable and keep application code independent of transport details. It must not grow into a template engine or duplicate provider-specific features.

Laravel surface: `Illuminate\Contracts\Mail\Mailer` and mailables. Native baseline: `System.Net.Mail`, `HttpClientFactory`, and provider SDKs. The value is a stable Naravel contract with an explicit transport boundary and optional queue integration.

## Proposed decision

Add `Naravel.Mail` with typed message and sender contracts. The first API supports explicit sender, recipients, subject, text and optional HTML body; attachments, templating, provider discovery, and Laravel-compatible global configuration are out of scope.

Common case:

```csharp
services.AddNaravelMail();
await mailer.SendAsync(new MailMessage(from, to, "Receipt", text), cancellationToken);
```

Define `IMailer` and an `IMailTransport` boundary. Do not introduce a driver manager unless multiple supported transports justify it; use normal DI registration for the selected transport. Provide `MailFake` for assertions and avoid hidden global state.

The default core package does not select a network transport. A separately installable SMTP/provider adapter may use MailKit, but that dependency and its package split require owner approval before implementation. Do not build a production transport on obsolete `SmtpClient` as a convenience fallback. Sending is asynchronous and cancellable; distinguish transport failure from successful acceptance and do not silently retry non-idempotent sends.

Optional queued delivery may be provided through a separate Queue adapter after R07; synchronous dispatch in the caller remains the core behavior.

## Contracts and quality

- Keep message data explicit and serializable if a queue adapter is added; do not store delegates or CLR type names.
- Validate recipient and sender address format at the boundary and report provider errors explicitly.
- `MailFake` records messages and supports assertions/reset.
- If a network transport is implemented, expose `Meter` and `ActivitySource` telemetry with bounded tags and no recipient/message content.
- No HTML rendering engine, implicit template lookup, or model magic is included.

## Verification required after approval

Test message construction, recipient validation, cancellation, transport failures, fake assertions, and any adapter-specific behavior. A transport adapter needs its own focused tests and may not be a core dependency. Update EN/FA documentation, parity records, changelog and the stage checklist; run the solution verification and roadmap checker.

## Alternatives considered

1. **Use `System.Net.Mail.SmtpClient` as the built-in transport.** Rejected as the default for new code because of its limitations and lack of a compelling cross-provider contract.
2. **Depend on MailKit from the core package.** Rejected; users should not receive a network dependency unless they opt into that transport.
3. **Use provider SDKs directly in application code.** Remains valid for provider-specific needs, but does not provide a consistent test seam or optional queued sending.
4. **Add a template engine and Laravel-style mailable discovery.** Rejected as unnecessary scope and runtime magic.

## Approval

The owner approved this proposal on 2026-10-07, including the `Naravel.Mail` contract and a separately installable MailKit-backed transport. This approval applies to R09; it does not authorize adding MailKit to the core package.
