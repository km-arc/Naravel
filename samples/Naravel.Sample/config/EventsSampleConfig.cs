using Microsoft.Extensions.DependencyInjection;
using Naravel.Events;
using Naravel.Sample.App.Events;
using Naravel.Sample.App.Listeners;

namespace Naravel.Sample.Config;

public static class EventsSampleConfig
{
    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<EventTrace>();

        // Registers the dispatcher and swaps in the queue-aware listener invoker. Queue itself is configured in
        // QueueSampleConfig; the adapter dispatches through IJobDispatcher on the default connection and queue.
        services.AddNaravelEventsQueue();

        // DI listeners run in registration order, so the fraud screen can stop propagation before the others.
        services.AddScoped<IEventListener<OrderPlaced>, FraudScreenListener>();
        services.AddScoped<IEventListener<OrderPlaced>, RecordOrderListener>();

        // The alias is what is stored in the queue; it is an allow-list entry, never a CLR type name.
        services.AddQueuedEventListener<OrderPlaced, SendReceiptListener>("order-placed.receipt");
    }
}
