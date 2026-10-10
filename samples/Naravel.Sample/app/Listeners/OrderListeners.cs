using Naravel.Events;
using Naravel.Sample.App.Events;

namespace Naravel.Sample.App.Listeners;

/// <summary>
/// Runs first (it is registered first). Orders above the limit are held for review and it stops propagation, so
/// neither the read-model listener, the queued receipt, nor any <c>Listen</c> callback runs for them.
/// </summary>
public sealed class FraudScreenListener(EventTrace trace) : IEventListener<OrderPlaced>
{
    public const decimal ReviewLimit = 10_000m;

    public Task HandleAsync(OrderPlaced evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        if (evt.Total > ReviewLimit)
        {
            trace.Add($"fraud-screen: {evt.OrderId} total {evt.Total} is above {ReviewLimit}; held for review, propagation stopped");
            context.Stop();
        }
        else
        {
            trace.Add($"fraud-screen: {evt.OrderId} passed");
        }

        return Task.CompletedTask;
    }
}

/// <summary>A normal DI listener. It runs synchronously inside the dispatch, in registration order.</summary>
public sealed class RecordOrderListener(EventTrace trace) : IEventListener<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        trace.Add($"read-model: recorded {evt.OrderId} ({evt.Total})");
        return Task.CompletedTask;
    }
}

/// <summary>
/// A queued listener. Implementing <see cref="IQueuedEventListener{TEvent}"/> and registering it with
/// <c>AddQueuedEventListener</c> means dispatch only enqueues a job; the worker calls this method later.
/// </summary>
public sealed class SendReceiptListener(EventTrace trace) : IQueuedEventListener<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        trace.Add($"receipt (queue worker): emailed {evt.Email} for {evt.OrderId}");
        Console.WriteLine($"Receipt for {evt.OrderId} sent to {evt.Email}.");
        return Task.CompletedTask;
    }
}
