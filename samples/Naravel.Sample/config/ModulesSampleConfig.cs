using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Sample.App.Demos;

namespace Naravel.Sample.Config;

/// <summary>
/// Registers the small module demos: Cache + rate limiter, Encryption, Mail, Notifications and Scheduling.
/// Everything here is dependency-free: memory cache, the <c>log</c> mail transport, log and delegate notification
/// channels, and Data Protection with its default key ring, so the sample still starts with no server installed.
/// </summary>
public static class ModulesSampleConfig
{
    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        var trace = new DemoTrace();                           // one instance shared by the singleton channel and the endpoints
        services.AddSingleton(trace);

        // Cache section of config/appsettings.json: Default = memory. Register the core package before any provider.
        services.AddNaravelCache(configuration);
        services.AddNaravelRateLimiter("memory");              // fixed window on the named store; use "redis" with several instances

        services.AddNaravelEncryption();                       // thin facade over ASP.NET Core Data Protection

        services.AddNaravelMail(configuration);                // Mail:Default = log, so nothing is delivered

        services.AddNaravelNotifications();
        services.AddLogNotificationChannel<InvoiceReady>("log");
        services.AddNotificationChannel<InvoiceReady>("trace", (context, invoice, _) =>
        {
            trace.Add($"notification: invoice {invoice.InvoiceId} for {context.Recipient} on channel {context.Channel}");
            return Task.CompletedTask;
        });

        services.AddNaravelScheduling(schedule =>
            schedule.Call("sample-heartbeat", (sp, _) =>
            {
                sp.GetRequiredService<DemoTrace>().Add("scheduler: sample-heartbeat ran");
                return Task.CompletedTask;
            }).EveryMinute().WithoutOverlapping());
    }
}
