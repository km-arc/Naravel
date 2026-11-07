# Notifications

`Naravel.Notifications` sends **one typed notification to a recipient through channels you name**, in the order you name them, and ships a fake that makes it testable. The core package has no mail, queue or network dependency: Mail and Queue are separate opt-in packages, and any other channel is an ordinary class or delegate you register.

Decisions: [PDR-014](../pdr/en/PDR-014-notifications.md)..

## Quickstart

```sh
dotnet add package Naravel.Notifications
```

```csharp
using Naravel.Notifications;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelNotifications();
builder.Services.AddLogNotificationChannel<InvoiceReady>();   // a channel named "log"

var app = builder.Build();

app.MapPost("/invoices/{id:int}/notify", async (int id, INotificationSender notifications, CancellationToken ct) =>
{
    var recipient = new User("Ada");                                            // any object: it is passed to the channels as is
    await notifications.SendAsync(recipient, new InvoiceReady(id), ["log"], ct);
    return Results.Accepted();
});

app.Run();

public sealed record User(string Name);
public sealed record InvoiceReady(int InvoiceId);
```

`POST /invoices/7/notify` answers `202 Accepted`, and the log channel writes one line naming the types `InvoiceReady` and `User` (never their contents). Add the [mail channel](#mail-channel-naravelnotificationsmail) or your own channel class to deliver for real.

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

## Errors

**Arguments.** These are raised before any channel runs.

| When | Exception and message |
|---|---|
| A `null` recipient, notification or channel list | `ArgumentNullException` |
| `channels` is empty | `ArgumentException`: `At least one channel name is required.` |
| A channel name is blank | `ArgumentException`: `A channel name must not be blank.` |
| A name is listed twice (compared ignoring case) | `ArgumentException`: `The channel 'log' is listed more than once.` |

**Channels.** Every name is resolved before the first delivery, so none of these has sent anything.

| When | Exception and message |
|---|---|
| A name has no registered channel for this notification type | `InvalidOperationException`: `No notification channel 'sms' is registered for InvoiceReady. Registered: log.` (`Registered: none.` when there are none) |
| The same name is registered twice for one type | `InvalidOperationException`: `The channel 'sms' is registered more than once for InvoiceReady; channel names must be unique per notification type.` |

**Delivery.** Anything that throws **inside a channel** stops the dispatch, and it does not reach the caller as itself: `SendAsync` throws a `NotificationDeliveryException` and the original exception is its `InnerException`.

| Channels that had delivered | `Message` |
|---|---|
| none | `Notification channel 'boom' failed; no channel had delivered yet.` |
| `log` | `Notification channel 'boom' failed after delivering to: log.` |

`Channel` is `boom`, and `DeliveredChannels` lists the earlier channels in order. `OperationCanceledException` is never wrapped.

What arrives as the `InnerException`:

| Thrown inside the channel | `InnerException` |
|---|---|
| Your own channel code | Whatever it threw. |
| `context.GetRecipient<User>()` on a recipient of another type | `InvalidOperationException`: `Channel 'sms' expects a recipient of type User but got String.` |
| The mail channel, when your `mailable` delegate returns `null` | `InvalidOperationException`: `The mailable factory for InvoiceReady returned null.` |
| The mail channel, with an invalid address | Naravel.Mail's own `ArgumentException`, for example `'not-an-address' is not a valid e-mail address.` (see the [Mail guide](mail.md#errors)) |
| The mail channel, when the mail transport fails | The transport's own exception. |

Calling `GetRecipient` or a channel's `SendAsync` directly, as a unit test of the channel does, throws these exceptions unwrapped.

**Queue adapter.**

| When | Exception and message |
|---|---|
| `QueueAsync` with an alias that is not registered | `InvalidOperationException`: `No queued notification is registered under the alias 'invoice.ready'. Register it with AddQueuedNotification.` |
| `QueueAsync` with a payload of another type | `ArgumentException`: `The alias 'invoice.ready' was registered for payload type InvoicePayload, not String.` |
| One alias registered twice | `InvalidOperationException`: `The queued notification alias 'invoice.ready' is registered more than once.` |
| The worker reads an empty payload | `InvalidOperationException`: `The stored payload for 'invoice.ready' is empty.` |

`NotificationFake` assertions throw `InvalidOperationException`:

| Assertion | Message |
|---|---|
| `AssertSent<InvoiceReady>()` when none was sent | `No InvoiceReady notification was sent.` |
| `AssertSent<InvoiceReady>(2)` when one was sent | `Expected 2 InvoiceReady notifications to be sent, but found 1.` |
| `AssertNotSent<InvoiceReady>()` when one was sent | `Expected no InvoiceReady notification, but 1 were sent.` |
| `AssertNothingSent()` when one was sent | `Expected no notifications, but 1 were sent.` |

`tests/Naravel.Notifications.Tests/DocsExampleTests.cs` runs the quickstart and the argument, channel, delivery and `NotificationFake` messages above. The mail channel's `returned null` message is run by `tests/Naravel.Notifications.Mail.Tests`; the queue adapter messages come from the source and are not run. Change one, change the other.

## Complete example

Three channels (mail, SMS, log) for one notification, an endpoint that reports which channel failed, and a test with `NotificationFake`. `User`, `IUserRepository` and `ISmsClient` are your own types.

```csharp
public sealed record User(int Id, string Name, string Email, string Phone);
public sealed record InvoiceReady(int InvoiceId, decimal Amount);

// Mail: the Mailable is built from the notification. Build must be pure.
public sealed class InvoiceMail(InvoiceReady n) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject($"Invoice {n.InvoiceId}").Text($"Amount due: {n.Amount}").Build();
}

// SMS: a channel class. ISmsClient is your own provider wrapper.
public sealed class SmsChannel(ISmsClient sms) : INotificationChannel<InvoiceReady>
{
    public string Name => "sms";

    public Task SendAsync(NotificationContext context, InvoiceReady n, CancellationToken cancellationToken) =>
        sms.SendAsync(context.GetRecipient<User>().Phone, $"Invoice {n.InvoiceId} is ready", cancellationToken);
}

// Program.cs
builder.Services.AddNaravelMail(builder.Configuration);
builder.Services.AddNaravelNotifications();
builder.Services.AddMailNotificationChannel<InvoiceReady>(
    address: c => c.GetRecipient<User>().Email,
    mailable: n => new InvoiceMail(n));
builder.Services.AddNotificationChannel<InvoiceReady, SmsChannel>();
builder.Services.AddLogNotificationChannel<InvoiceReady>("log");

app.MapPost("/invoices/{id:int}/notify", async (int id, IUserRepository users, INotificationSender notifications, CancellationToken ct) =>
{
    var user = await users.CurrentAsync(ct);
    try
    {
        await notifications.SendAsync(user, new InvoiceReady(id, 120m), ["mail", "sms", "log"], ct);   // in this order
        return Results.Accepted();
    }
    catch (NotificationDeliveryException ex)                  // a channel threw: later channels were not tried
    {
        return Results.Problem($"{ex.Channel} failed after: {string.Join(", ", ex.DeliveredChannels)}", statusCode: 502);
    }
});

// Test
[Fact]
public async Task Notify_sends_through_three_channels()
{
    var fake = new NotificationFake();
    var user = new User(1, "Ada", "ada@example.com", "+98900000000");

    await fake.SendAsync(user, new InvoiceReady(7, 120m), ["mail", "sms", "log"]);

    var sent = fake.AssertSent<InvoiceReady>();
    sent.Recipient.Should().BeSameAs(user);
    sent.Channels.Should().Equal("mail", "sms", "log");
}
```

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
