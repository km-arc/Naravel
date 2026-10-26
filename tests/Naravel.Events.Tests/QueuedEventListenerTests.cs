using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Naravel.Events;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Queue.Memory;

namespace Naravel.Events.Tests;

public sealed record InvoiceReady(string InvoiceId);

public sealed class QueuedInvoiceListener : IQueuedEventListener<InvoiceReady>
{
    public static ConcurrentQueue<string> Processed { get; } = new();

    public Task HandleAsync(InvoiceReady evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Processed.Enqueue(evt.InvoiceId);
        return Task.CompletedTask;
    }
}

public sealed record ShipmentReady(string ShipmentId);

public sealed class QueuedShipmentListener : IQueuedEventListener<ShipmentReady>
{
    public static ConcurrentQueue<string> Processed { get; } = new();

    public Task HandleAsync(ShipmentReady evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Processed.Enqueue(evt.ShipmentId);
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(ShipmentReady))]
public sealed partial class EventsTestJsonContext : JsonSerializerContext;

[Collection("Event telemetry")]
public sealed class QueuedEventListenerTests
{
    [Fact]
    public async Task Queue_adapter_runs_marked_listener_in_memory_worker()
    {
        Drain(QueuedInvoiceListener.Processed);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NaravelQueue:Default"] = "events",
                ["NaravelQueue:Stores:events:Driver"] = "memory"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddNaravelEventsQueue();
        services.AddQueuedEventListener<InvoiceReady, QueuedInvoiceListener>("invoice-ready");
        services.AddQueueWorker(options =>
        {
            options.Connection = "events";
            options.Queues = ["default"];
            options.Rest = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var workers = provider.GetServices<IHostedService>().ToArray();
        foreach (var worker in workers)
        {
            await worker.StartAsync(CancellationToken.None);
        }

        try
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
                .DispatchAsync(new InvoiceReady("invoice-42"));

            (await WaitUntilAsync(() => QueuedInvoiceListener.Processed.Contains("invoice-42")))
                .Should().BeTrue();
        }
        finally
        {
            foreach (var worker in workers)
            {
                await worker.StopAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Per_listener_dispatch_options_route_to_their_own_queue()
    {
        Drain(QueuedShipmentListener.Processed);
        await RunOnQueueAsync("shipments", services =>
            services.AddQueuedEventListener<ShipmentReady, QueuedShipmentListener>("shipment-ready", options => options.OnQueue("shipments")));
    }

    [Fact]
    public async Task Source_generated_metadata_overload_serializes_and_routes()
    {
        Drain(QueuedShipmentListener.Processed);
        await RunOnQueueAsync("shipments", services =>
            services.AddQueuedEventListener<ShipmentReady, QueuedShipmentListener>("shipment-ready", EventsTestJsonContext.Default.ShipmentReady, options => options.OnQueue("shipments")));
    }

    private static async Task RunOnQueueAsync(string queue, Action<IServiceCollection> register)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NaravelQueue:Default"] = "events",
                ["NaravelQueue:Stores:events:Driver"] = "memory"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddNaravelEventsQueue();
        register(services);
        // The worker only reads the listener's own queue, so the job can run only if the dispatch options were applied.
        services.AddQueueWorker(options =>
        {
            options.Connection = "events";
            options.Queues = [queue];
            options.Rest = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var workers = provider.GetServices<IHostedService>().ToArray();
        foreach (var worker in workers) await worker.StartAsync(CancellationToken.None);

        try
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().DispatchAsync(new ShipmentReady("shipment-7"));

            (await WaitUntilAsync(() => QueuedShipmentListener.Processed.Contains("shipment-7"))).Should().BeTrue();
        }
        finally
        {
            foreach (var worker in workers) await worker.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    private static void Drain<T>(ConcurrentQueue<T> queue)
    {
        while (queue.TryDequeue(out _))
        {
        }
    }
}
