# Notifications

`Naravel.Notifications` sends **one typed notification to a recipient through channels you name**, in the order you name them, and ships a fake that makes it testable. The core package has no mail, queue or network dependency: Mail and Queue are separate opt-in packages, and any other channel is an ordinary class or delegate you register.

Decisions: [PDR-014](../pdr/en/PDR-014-notifications.md). Persian version: [docs/fa/notifications.md](../fa/notifications.md).

## Quickstart

```csharp
builder.Services.AddNaravelNotifications();
builder.Services.AddLogNotificationChannel<InvoiceReady>();          // a channel named "log"

public sealed record InvoiceReady(int InvoiceId);

await notifications.SendAsync(user, new InvoiceReady(7), ["log"], cancellationToken);   // notifications is an injected INotificationSender
```

A channel is registered **per notification type** and chosen **by name** on every call. Nothing is inferred from the recipient or from registration order.

## Using it

### Channels

A channel implements `INotificationChannel<TNotification>`: a `Name` and a `SendAsync(NotificationContext, TNotification, CancellationToken)`. Register it as a class (resolved from DI per scope, so it can take dependencies), as a delegate, or use the built-in log channel:

```csharp
services.AddNotificationChannel<InvoiceReady, SmsChannel>();                       // class
services.AddNotificationChannel<InvoiceReady>("audit", (context, invoice, ct) =>   // delegate
    audit.WriteAsync(context.Recipient, invoice, ct));
services.AddLogNotificationChannel<InvoiceReady>("log");                           // logs type names only
```

`NotificationContext` holds the explicit `Recipient` (an `object`) and the `Channel` name. `context.GetRecipient<User>()` casts it and throws an `InvalidOperationException` that names both types when the recipient is something else. Channel names are unique per notification type and compared ignoring case; registering the same name twice is reported when a notification is sent. Registering the same channel class twice is a no-op.

The log channel writes one line per delivery with the notification and recipient **type names only**, never their contents, and works without any logging registration.

### Sending

```csharp
await notifications.SendAsync(user, new InvoiceReady(7), ["mail", "sms", "log"], ct);
```

- Channels run **in the order given**, one after another, not in registration order.
- Every name is resolved **before the first delivery**: a typo or a missing channel throws `InvalidOperationException` (it lists the registered names) and nothing is sent.
- `channels` must have at least one name, no blank name and no duplicate (compared ignoring case); otherwise `ArgumentException`. A `null` recipient or notification throws `ArgumentNullException`.
- A channel that throws **stops the dispatch**: the later channels are not tried and the call throws `NotificationDeliveryException` with `Channel` (the one that failed), `DeliveredChannels` (those that had already delivered, in order) and the original exception as `InnerException`. Partial delivery is never reported as success, and a caller that retries knows what already went out.
- Cancellation is not a failure: `OperationCanceledException` is thrown as is, never wrapped, and a cancelled token runs no channel.

`INotificationSender` is registered **scoped**, so inject it into request handlers, jobs and other scoped code.

### Mail channel (`Naravel.Notifications.Mail`)

```csharp
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddMailNotificationChannel<InvoiceReady>(
    address: c => c.GetRecipient<User>().Email,
    mailable: n => new InvoiceMail(n));

await notifications.SendAsync(user, new InvoiceReady(7), ["mail", "log"], ct);
```

Two plain delegates replace Laravel's `routeNotificationForMail()` and `toMail()`: one reads the address from the explicit recipient, one builds the `Mailable`. The message goes through the named mailer (`mailer:` argument, the default mailer when omitted) with `PendingMail`, so the mailer's default `From` and the address validation of Naravel.Mail apply; an invalid address fails the channel. The channel name is `mail` unless you pass `name:`. For queued mail use Naravel.Mail's own `QueueAsync` or the queue adapter below.

### Queued notifications (`Naravel.Notifications.Queue`)

```csharp
builder.Services.AddNaravelNotificationQueue();      // plus AddQueue, a queue driver and (in worker processes) AddQueueWorker
builder.Services.AddQueuedNotification<InvoicePayload>("invoice.ready", async (payload, scope, ct) =>
{
    var user = await scope.GetRequiredService<IUserRepository>().FindAsync(payload.UserId, ct);   // fresh data
    await scope.GetRequiredService<INotificationSender>().SendAsync(user, new InvoiceReady(payload.InvoiceId), ["mail"], ct);
});

await notificationQueue.QueueAsync("invoice.ready", new InvoicePayload(user.Id, 7), ct);   // INotificationQueue
```

The queue stores **an alias and the payload as JSON, nothing else**: no CLR type name, no delegate, no notification and no recipient object (PDR-014, owner decision OD-07). The handler registered under the alias runs in the worker with the deserialized payload and the job's service scope, so it loads the recipient from your own services and builds the notification itself.

- An alias that is not registered fails the job instead of guessing a type; queueing an unregistered alias, or a payload of another type than the one registered, throws immediately.
- Registering one alias twice is refused. **Never rename an alias** while jobs that use it are queued.
- `AddNaravelNotificationQueue(options => { options.Connection = ...; options.Queue = ...; })` places the jobs; both are optional and default to the queue module's defaults.
- Delivery is **at-least-once**, like every Naravel job: if the worker stops after the channels delivered but before the job is acknowledged, the notification is sent again, and retries use the standard job backoff. Make the handler idempotent where a duplicate matters. The payload is serialized with System.Text.Json: keep it small, plain data.

### Testing

```csharp
var fake = new NotificationFake();                      // an INotificationSender that delivers nothing
await fake.SendAsync(user, new InvoiceReady(7), ["mail", "log"]);

var sent = fake.AssertSent<InvoiceReady>();             // first one; throws with a clear message when none was sent
sent.Recipient.Should().BeSameAs(user);
sent.Notification.InvoiceId.Should().Be(7);
sent.Channels.Should().Equal("mail", "log");
fake.AssertSent<InvoiceReady>(1);                       // exact count
fake.AssertNotSent<InvoiceCancelled>();
fake.AssertNothingSent();                               // after Reset(), or when nothing was sent
```

Register it with `services.AddSingleton<INotificationSender>(fake)`. It applies the same argument checks as the real sender (empty or duplicate channel names throw), so a bad call fails in the test too; it records the **requested** channel names and runs no channel. To test a channel itself, call `SendAsync` on it directly, or register it with the real sender and an `array` mail transport.

## Observability

`Meter` and `ActivitySource` are both named `Naravel.Notifications`.

| Instrument | Meaning | Tags |
|---|---|---|
| `naravel.notifications.dispatched` | Dispatches in which every channel delivered. | none |
| `naravel.notifications.channel.deliveries` | One per channel attempt. | `channel` (the name you registered), `outcome` (`sent` or `failed`) |
| `naravel.notifications.dispatch.duration` | Milliseconds per dispatch, failed or not. | none |

Activity `notifications.send` is marked as error when a channel fails. Recipients and notification contents are **never** tagged or logged.

## How it is built

- `NotificationSender` resolves `INotificationChannel<TNotification>` from DI, matches the requested names (ignoring case) and runs the channels one by one. There is no manager and no configuration section, because a channel is typed and chosen by the caller, not by a configured default (PDR-014). "Extending at run time" therefore means registering a channel before the provider is built.
- The core depends on `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` only. `Naravel.Notifications.Mail` references Naravel.Mail; `Naravel.Notifications.Queue` references Naravel.Queue; neither is referenced by the core.
- The queue adapter keeps an explicit alias registry (`alias → handler`), the same rule as job aliases: a stored string is never turned into a CLR type.
- Dispatch is I/O-bound (the channels do the work), so there is no benchmark; the sender allocates only a few small arrays and one list per call.

## Not included (by decision)

`Notifiable` model traits, `via()` channel inference, `routeNotificationFor*` conventions found by reflection, notification discovery, a database channel, a broadcast channel (Broadcasting is its own stage), SMS or chat providers, per-user channel preferences, notification localization and queueing by interface. Applications add what they need as channels.

## Laravel mapping

| Laravel | Naravel |
|---|---|
| `Notification::send($user, new InvoicePaid($inv))` | `notifications.SendAsync(user, new InvoicePaid(inv), ["mail"], ct)` |
| `via()` returns the channels | the caller passes the channel names |
| `toMail()` + `routeNotificationForMail()` | `AddMailNotificationChannel(address, mailable)` |
| `ShouldQueue` notification | `AddQueuedNotification(alias, handler)` + `INotificationQueue.QueueAsync` |
| custom channel class with `send()` | `INotificationChannel<TNotification>` |
| `Notification::fake()` + `assertSentTo` | `NotificationFake` + `AssertSent<T>()` |
