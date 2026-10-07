# PDR-014 — Notifications (`Naravel.Notifications`)

**Native-first verdict:** .NET DI and provider APIs already handle individual delivery channels. Naravel should add only a small typed fan-out contract when one notification must reach multiple explicit channels; it should not add model traits, implicit routing, or another transport stack.

Status: **Approved 2026-10-07.**

## Context

Laravel notifications provide one typed notification sent through one or more channels, with recipient routing and queue support. In .NET, applications can call each provider directly, but repeated channel selection and test setup can become application boilerplate. A minimal orchestration layer may add value while preserving native channels.

Laravel surface: `Illuminate\Contracts\Notifications\Dispatcher`, channel manager and notifiable routing. Native baseline: DI, typed provider interfaces, Mail, and Queue. This proposal depends on R07 and R09 as stated in the roadmap; it does not require the Events module.

## Proposed decision

Add a small `Naravel.Notifications` package with explicit notification and channel contracts. Callers provide the recipient and selected channel names explicitly. Do not infer channels from model attributes or add global notification routing.

Common case:

```csharp
services.AddNaravelNotifications();
await notifications.SendAsync(recipient, new InvoiceReady(invoiceId), ["mail"], cancellationToken);
```

Define `INotificationSender`, `INotificationChannel<TNotification>`, and a notification context containing the explicit recipient and cancellation token. DI registration order is not channel selection; requested channel names are honored in the caller's order. Missing channels fail clearly. A channel failure stops dispatch by default and is surfaced; partial delivery is not reported as total success.

Mail and Queue integrations remain separate adapters. Queued notifications use explicit job aliases and the approved queue contracts; do not serialize arbitrary CLR type names. Other channels may be supplied by applications without changing the core. Provide `NotificationFake` for deterministic assertions.

## Contracts and quality

- Notification types are ordinary typed objects; no model traits, reflection discovery, or magic recipient properties.
- Dispatch is asynchronous and accepts `CancellationToken`.
- The fake records recipient, notification, and requested channels; avoid storing sensitive payloads in telemetry.
- Emit `Meter` and `ActivitySource` for dispatch and channel outcomes, using bounded channel/outcome tags only.
- Keep the core package free of provider dependencies; channel adapters are independently opt-in.

## Verification required after approval

Test multiple channel ordering, missing channel handling, cancellation, channel failure and partial-delivery reporting, fake assertions, Mail integration, and queued execution using the Memory queue. Add a benchmark if dispatch is a measured hot path. Update EN/FA docs, parity records, changelog and stage checklist, then run full solution verification and the roadmap checker.

## Alternatives considered

1. **Call every provider directly.** Valid and preferred for a single channel; repetitive when one typed notification must fan out consistently and be tested as a unit.
2. **Laravel-style model traits and inferred routing.** Rejected; recipients and channels should be explicit in .NET.
3. **Put all transports in the notification package.** Rejected; Mail and Queue remain optional adapters and provider ownership stays separate.
4. **Use Events as a mandatory dependency.** Rejected; notification delivery is explicit orchestration and R10's roadmap dependencies do not include R06.

## Approval

The owner approved this proposal on 2026-10-07, including explicit typed channel fan-out, the stated Mail/Queue dependencies, and separate opt-in adapters.
