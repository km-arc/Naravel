using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Events.Testing;
using Naravel.Queue.Extensions;
using Naravel.Queue.Memory;

namespace Naravel.Events.Tests.DocsExample;

// Mirrors "A complete example" in docs/en/events.md and docs/fa/events.md. If this test needs a change,
// the guide needs the same change.
public sealed record OrderPaid(string OrderId, decimal Total);

public sealed class ReadModel
{
    public List<string> Paid { get; } = [];
}

public sealed class Mailer
{
    public TaskCompletionSource<string> Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class UpdateOrderReadModel(ReadModel readModel) : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        lock (readModel.Paid) readModel.Paid.Add(evt.OrderId);
        return Task.CompletedTask;
    }
}

public sealed class SendReceipt(Mailer mailer) : IQueuedEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        mailer.Sent.TrySetResult(evt.OrderId);
        return Task.CompletedTask;
    }
}

public sealed class RejectFraudulentOrders : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        if (evt.Total > 100_000m) context.Stop();
        return Task.CompletedTask;
    }
}

public sealed class CheckoutService(IEventDispatcher events)
{
    public Task PayAsync(string id, decimal total, CancellationToken ct = default) =>
        events.DispatchAsync(new OrderPaid(id, total), ct);
}

public sealed class DocsExampleTests
{
    [Fact]
    public async Task Inline_listener_runs_before_dispatch_returns_and_queued_listener_runs_on_the_worker()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "events",
            ["NaravelQueue:Stores:events:Driver"] = "memory",
        }).Build();
        var readModel = new ReadModel();
        var mailer = new Mailer();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(readModel);
        services.AddSingleton(mailer);
        services.AddNaravelEvents();
        services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();
        services.AddQueue(configuration).AddMemoryDriver(configuration);
        services.AddNaravelEventsQueue();
        services.AddQueuedEventListener<OrderPaid, SendReceipt>("order-paid.receipt");
        services.AddQueueWorker();

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted) await service.StartAsync(CancellationToken.None);
        try
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().DispatchAsync(new OrderPaid("order-1", 42m));

            readModel.Paid.Should().Contain("order-1");
            var sent = await mailer.Sent.Task.WaitAsync(TimeSpan.FromSeconds(10));
            sent.Should().Be("order-1");
        }
        finally
        {
            foreach (var service in hosted) await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Stop_skips_the_listeners_registered_after_it()
    {
        var readModel = new ReadModel();
        var services = new ServiceCollection();
        services.AddSingleton(readModel);
        services.AddNaravelEvents();
        services.AddScoped<IEventListener<OrderPaid>, RejectFraudulentOrders>();
        services.AddScoped<IEventListener<OrderPaid>, UpdateOrderReadModel>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();

        await events.DispatchAsync(new OrderPaid("big", 200_000m));
        await events.DispatchAsync(new OrderPaid("small", 5m));

        readModel.Paid.Should().Equal("small");
    }

    [Fact]
    public async Task EventFake_records_the_dispatched_event()
    {
        var events = new EventFake();

        await new CheckoutService(events).PayAsync("order-7", 42m);

        events.AssertDispatched<OrderPaid>().OrderId.Should().Be("order-7");
        events.AssertDispatched<OrderPaid>(1);
    }
}
