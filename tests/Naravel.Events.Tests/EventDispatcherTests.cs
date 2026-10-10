using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Events;
using Naravel.Events.Testing;

namespace Naravel.Events.Tests;

public sealed record OrderPaid(string OrderId);

public sealed class OrderPaidListenerA : IEventListener<OrderPaid>
{
    public static ConcurrentQueue<string> Calls { get; } = new();

    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue($"A:{evt.OrderId}");
        return Task.CompletedTask;
    }
}

public sealed class OrderPaidListenerB : IEventListener<OrderPaid>
{
    public static ConcurrentQueue<string> Calls { get; } = new();

    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue($"B:{evt.OrderId}");
        context.Stop();
        return Task.CompletedTask;
    }
}

public sealed class OrderPaidListenerC : IEventListener<OrderPaid>
{
    public static ConcurrentQueue<string> Calls { get; } = new();

    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue($"C:{evt.OrderId}");
        return Task.CompletedTask;
    }
}

public sealed class ScopedListener : IEventListener<OrderPaid>
{
    public Guid Id { get; } = Guid.NewGuid();
    public static ConcurrentQueue<Guid> Observed { get; } = new();

    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        Observed.Enqueue(Id);
        return Task.CompletedTask;
    }
}

public sealed class FailingListener : IEventListener<OrderPaid>
{
    public Task HandleAsync(OrderPaid evt, EventDispatchContext context, CancellationToken cancellationToken = default)
        => Task.FromException(new InvalidOperationException("listener failed"));
}

[Collection("Event telemetry")]
public class EventDispatcherTests
{
    [Fact]
    public async Task Dispatch_runs_DI_listeners_in_registration_order_and_stops_when_requested()
    {
        Drain(OrderPaidListenerA.Calls);
        Drain(OrderPaidListenerB.Calls);
        Drain(OrderPaidListenerC.Calls);

        var services = new ServiceCollection();
        services.AddNaravelEvents();
        services.AddSingleton<IEventListener<OrderPaid>, OrderPaidListenerA>();
        services.AddSingleton<IEventListener<OrderPaid>, OrderPaidListenerB>();
        services.AddSingleton<IEventListener<OrderPaid>, OrderPaidListenerC>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
                .DispatchAsync(new OrderPaid("123"));
        }

        OrderPaidListenerA.Calls.Should().Equal("A:123");
        OrderPaidListenerB.Calls.Should().Equal("B:123");
        OrderPaidListenerC.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Listen_callbacks_run_in_order_and_dispose_unsubscribes()
    {
        var services = new ServiceCollection();
        services.AddNaravelEvents();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        var calls = new List<string>();

        dispatcher.Listen<OrderPaid>((evt, _, _) =>
        {
            calls.Add($"first:{evt.OrderId}");
            return Task.CompletedTask;
        });
        var subscription = dispatcher.Listen<OrderPaid>((evt, _, _) =>
        {
            calls.Add($"second:{evt.OrderId}");
            return Task.CompletedTask;
        });

        await dispatcher.DispatchAsync(new OrderPaid("1"));
        subscription.Dispose();
        await dispatcher.DispatchAsync(new OrderPaid("2"));

        calls.Should().Equal("first:1", "second:1", "first:2");
    }

    [Fact]
    public async Task Cached_listener_plan_preserves_scoped_DI_lifetimes()
    {
        Drain(ScopedListener.Observed);
        var services = new ServiceCollection();
        services.AddNaravelEvents();
        services.AddScoped<IEventListener<OrderPaid>, ScopedListener>();
        await using var provider = services.BuildServiceProvider();

        Guid firstScopeListener;
        await using (var firstScope = provider.CreateAsyncScope())
        {
            var dispatcher = firstScope.ServiceProvider.GetRequiredService<IEventDispatcher>();
            await dispatcher.DispatchAsync(new OrderPaid("1"));
            await dispatcher.DispatchAsync(new OrderPaid("2"));
            firstScopeListener = ScopedListener.Observed.TryDequeue(out var first) ? first : Guid.Empty;
            ScopedListener.Observed.TryDequeue(out var secondInScope).Should().BeTrue();
            secondInScope.Should().Be(firstScopeListener);
        }

        await using (var secondScope = provider.CreateAsyncScope())
        {
            await secondScope.ServiceProvider.GetRequiredService<IEventDispatcher>()
                .DispatchAsync(new OrderPaid("3"));
        }

        var secondScopeListener = ScopedListener.Observed.TryDequeue(out var second) ? second : Guid.Empty;
        firstScopeListener.Should().NotBe(Guid.Empty);
        secondScopeListener.Should().NotBe(firstScopeListener);
    }

    [Fact]
    public async Task Listener_exception_is_propagated()
    {
        var services = new ServiceCollection();
        services.AddNaravelEvents();
        services.AddSingleton<IEventListener<OrderPaid>, FailingListener>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
            .DispatchAsync(new OrderPaid("failure"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("listener failed");
    }

    [Fact]
    public async Task Pre_cancelled_dispatch_does_not_invoke_listeners()
    {
        Drain(OrderPaidListenerA.Calls);
        var services = new ServiceCollection();
        services.AddNaravelEvents();
        services.AddSingleton<IEventListener<OrderPaid>, OrderPaidListenerA>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
            .DispatchAsync(new OrderPaid("cancelled"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        OrderPaidListenerA.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Event_fake_records_count_order_and_reset()
    {
        var fake = new EventFake();
        var first = new OrderPaid("first");
        var second = new OrderPaid("second");

        await fake.DispatchAsync(first);
        await fake.DispatchAsync(second);

        fake.AssertDispatched<OrderPaid>().Should().Be(first);
        fake.AssertDispatched<OrderPaid>(2).Should().Equal(first, second);
        fake.All.Should().Equal(first, second);
        fake.Reset();
        fake.All.Should().BeEmpty();
    }

    [Fact]
    public void AddNaravelEvents_respects_an_explicit_dispatcher_override()
    {
        var fake = new EventFake();
        var services = new ServiceCollection();
        services.AddSingleton<IEventDispatcher>(fake);

        services.AddNaravelEvents();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventDispatcher>().Should().BeSameAs(fake);
    }

    private static void Drain<T>(ConcurrentQueue<T> queue)
    {
        while (queue.TryDequeue(out _))
        {
        }
    }
}
