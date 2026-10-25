using Naravel.Events;

namespace Naravel.Broadcasting;

/// <summary>Bridges one event type from <c>Naravel.Events</c> to <see cref="IBroadcaster"/>.</summary>
internal sealed class BroadcastListener<TEvent>(IBroadcaster broadcaster) : IEventListener<TEvent>
    where TEvent : class, IBroadcastable
{
    public Task HandleAsync(TEvent evt, EventDispatchContext context, CancellationToken cancellationToken = default) =>
        broadcaster.BroadcastAsync(evt, cancellationToken);
}
