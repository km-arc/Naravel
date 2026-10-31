// Native AOT smoke test: published with ILC, then run. Each check uses the trim/AOT-safe path of its package
// (compile-time configuration binding, source-generated JSON metadata) and the process exits non-zero when one fails.
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Cache.Abstractions;
using Naravel.Events;
using Naravel.Mail;
using Naravel.Mail.Transports;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;
using Naravel.Queue.Serialization;

var failures = 0;

async Task Check(string name, Func<Task> body)
{
    try
    {
        await body();
        Console.WriteLine($"ok   {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Cache:Default"] = "memory",
    ["Mail:Default"] = "array",
    ["NaravelQueue:Default"] = "smoke",
    ["NaravelQueue:Stores:smoke:Driver"] = "memory",
}).Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddNaravelCache(config);
services.AddNaravelEvents();
services.AddNaravelMail(config);
services.AddQueue(config).AddMemoryDriver(config);
services.AddJob(SmokeJson.Default.SmokeJob);
services.AddQueueWorker(o =>
{
    o.Connection = "smoke";
    o.Queues = new[] { "q" };
    o.SleepWhenEmpty = TimeSpan.FromMilliseconds(15);
});

await using var provider = services.BuildServiceProvider();
var hosted = provider.GetServices<IHostedService>().ToList();
foreach (var service in hosted) await service.StartAsync(CancellationToken.None);

await Check("cache: memory store round trip", async () =>
{
    var cache = provider.GetRequiredService<ICacheStore>();
    await cache.SetAsync("greeting", "hello", TimeSpan.FromMinutes(1));
    var (found, value) = await cache.TryGetAsync<string>("greeting");
    if (!found || value != "hello") throw new InvalidOperationException($"found={found} value={value}");
});

await Check("events: dispatch reaches a listener", async () =>
{
    var dispatcher = provider.GetRequiredService<IEventDispatcher>();
    var seen = 0;
    using var subscription = dispatcher.Listen<SmokeEvent>((evt, _, _) =>
    {
        seen += evt.Amount;
        return Task.CompletedTask;
    });
    await dispatcher.DispatchAsync(new SmokeEvent(3));
    if (seen != 3) throw new InvalidOperationException($"seen={seen}");
});

await Check("mail: array transport receives the message", async () =>
{
    var mailer = provider.GetRequiredService<IMailer>();
    await mailer.SendAsync(new MailMessage("app@example.com", "user@example.com", "Hello", "Body"));
    var transport = (ArrayMailTransport)provider.GetRequiredService<MailManager>().Driver("array");
    if (transport.Sent.Count != 1) throw new InvalidOperationException($"sent={transport.Sent.Count}");
});

await Check("queue: envelope JSON round trip", () =>
{
    var message = new QueuedMessage { Queue = "q", JobType = "x", Payload = "{}" };
    var back = QueueEnvelopeJson.Deserialize(QueueEnvelopeJson.Serialize(message));
    if (back is null || back.Id != message.Id || back.Queue != "q") throw new InvalidOperationException("envelope changed");
    return Task.CompletedTask;
});

await Check("queue: job dispatched, serialised with metadata and run by the worker", async () =>
{
    SmokeJob.Ran = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    await provider.GetRequiredService<IJobDispatcher>().DispatchAsync(new SmokeJob { Text = "from-the-queue" }, o => o.OnQueue("q"));
    var winner = await Task.WhenAny(SmokeJob.Ran.Task, Task.Delay(TimeSpan.FromSeconds(10)));
    if (winner != SmokeJob.Ran.Task) throw new TimeoutException("the job did not run within 10 seconds");
    if (SmokeJob.Ran.Task.Result != "from-the-queue") throw new InvalidOperationException(SmokeJob.Ran.Task.Result);
});

foreach (var service in hosted) await service.StopAsync(CancellationToken.None);

Console.WriteLine(failures == 0 ? "AOT smoke: all checks passed" : $"AOT smoke: {failures} check(s) failed");
return failures == 0 ? 0 : 1;

internal sealed record SmokeEvent(int Amount);

internal sealed class SmokeJob : Job
{
    public static TaskCompletionSource<string> Ran { get; set; } = new();

    public string Text { get; set; } = string.Empty;

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        Ran.TrySetResult(Text);
        return Task.CompletedTask;
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SmokeJob))]
internal sealed partial class SmokeJson : JsonSerializerContext;
