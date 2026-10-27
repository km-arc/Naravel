using Naravel.Cache.Abstractions;
using Naravel.Cache.RateLimiting;
using Naravel.Encryption;
using Naravel.Mail;
using Naravel.Notifications;

namespace Naravel.Sample.App.Demos;

/// <summary>An in-memory log the demos write to, so a page or <c>curl</c> can show what ran. Not for production.</summary>
public sealed class DemoTrace
{
    private readonly Queue<string> _lines = new();

    public void Add(string line)
    {
        lock (_lines)
        {
            _lines.Enqueue($"{DateTimeOffset.UtcNow:HH:mm:ss} {line}");
            while (_lines.Count > 50) _lines.Dequeue();
        }
    }

    public string[] Snapshot() { lock (_lines) return _lines.ToArray(); }
}

public sealed record InvoiceReady(int InvoiceId);

public sealed record CardInfo(string Holder, int Last4);

public sealed class WelcomeMail(string name) : Mailable
{
    public override MailMessage Build(MailBuilder mail) =>
        mail.Subject("Welcome").Text($"Hi {name}").Html($"<p>Hi {name}</p>").Build();
}

/// <summary>Plain minimal-API endpoints for the module demos. Each group is independent of the routing demo.</summary>
public static class ModuleDemoEndpoints
{
    private static int _loads;

    public static void Map(WebApplication app)
    {
        // Cache: RememberAsync loads once, then serves from the store until the ttl ends.
        var cache = app.MapGroup("/cache");
        cache.MapGet("/settings", async (ICacheStore store, CancellationToken ct) =>
        {
            var value = await store.RememberAsync("site-settings", TimeSpan.FromSeconds(30),
                () => Task.FromResult($"loaded #{Interlocked.Increment(ref _loads)} at {DateTimeOffset.UtcNow:HH:mm:ss}"), ct);
            return Results.Ok(new { value, hint = "call again within 30 seconds: the number does not change" });
        });
        cache.MapDelete("/settings", async (ICacheStore store, CancellationToken ct) =>
        {
            await store.RemoveAsync("site-settings", ct);
            return Results.NoContent();
        });

        // Rate limiter: 3 attempts per 30 seconds per subject. The 4th answers 429 with Retry-After.
        cache.MapPost("/attempt", async (string? subject, HttpContext http, IRateLimiter limiter, CancellationToken ct) =>
        {
            var decision = await limiter.AttemptAsync("sample-attempt", subject ?? "anonymous", 3, TimeSpan.FromSeconds(30), ct);
            if (!decision.Allowed)
            {
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString();
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            return Results.Ok(new { decision.Remaining });
        });
        cache.MapDelete("/attempt", async (string? subject, IRateLimiter limiter, CancellationToken ct) =>
        {
            await limiter.ClearAsync("sample-attempt", subject ?? "anonymous", ct);
            return Results.NoContent();
        });

        // Encryption: text, typed values and purposes. A tampered token never throws from TryDecrypt.
        var crypto = app.MapGroup("/encryption");
        crypto.MapGet("/text", (string? value, IEncrypter encrypter) =>
        {
            var token = encrypter.Encrypt(value ?? "secret");
            return Results.Ok(new { token, roundTrip = encrypter.Decrypt(token) });
        });
        crypto.MapGet("/typed", (IEncrypter encrypter) =>
        {
            var token = encrypter.Encrypt(new CardInfo("Ada", 4242));
            return Results.Ok(new { token, back = encrypter.Decrypt<CardInfo>(token) });
        });
        crypto.MapGet("/purpose", (IEncrypter encrypter) =>
        {
            var token = encrypter.ForPurpose("billing").Encrypt("only billing can read this");
            var other = encrypter.TryDecrypt(token, out _);                 // default purpose cannot read it
            var same = encrypter.ForPurpose("billing").TryDecrypt(token, out var text);
            return Results.Ok(new { defaultPurposeCanRead = other, billingCanRead = same, text });
        });

        // Mail: the log transport only writes to the log. Look at the console of `dotnet run`.
        app.MapPost("/mail/welcome", async (string? name, IMailer mailer, DemoTrace trace, CancellationToken ct) =>
        {
            await mailer.To("ali@example.com").SendAsync(new WelcomeMail(name ?? "Ali"), ct);
            trace.Add("mail: welcome sent to ali@example.com through the log transport");
            return Results.Accepted();
        });

        // Notifications: one typed notification, two named channels, in the order given.
        app.MapPost("/notifications/invoice", async (int? id, INotificationSender sender, CancellationToken ct) =>
        {
            await sender.SendAsync("ali@example.com", new InvoiceReady(id ?? 7), ["log", "trace"], ct);
            return Results.Accepted();
        });

        // Trace of everything the demos did, including the scheduler heartbeat (it runs about once a minute).
        app.MapGet("/demos/trace", (DemoTrace trace) => Results.Ok(trace.Snapshot()));
    }
}
