namespace Naravel.Events;

/// <summary>
/// Handles a typed event during dispatch.
/// </summary>
/// <typeparam name="TEvent">The event type handled by this listener.</typeparam>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a typed Laravel event listener.</para>
/// <para><b>Why it exists:</b> DI-resolved handlers make dependencies and lifetimes explicit.</para>
/// <para><b>Not ported on purpose:</b> name-based discovery and implicit listener construction.</para>
/// </remarks>
public interface IEventListener<in TEvent>
{
    /// <summary>
    /// Invoked when the event is dispatched.
    /// </summary>
    /// <param name="evt">The event instance.</param>
    /// <param name="context">Dispatch context that can stop propagation.</param>
    /// <param name="cancellationToken">A token that can cancel the listener execution.</param>
    Task HandleAsync(TEvent evt, EventDispatchContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marks a typed event listener for asynchronous execution through the optional Queue adapter.
/// </summary>
/// <typeparam name="TEvent">The event type handled by this listener.</typeparam>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a queued Laravel event listener.</para>
/// <para><b>Why it exists:</b> it opts an explicitly registered listener into queue execution without coupling
/// <c>Naravel.Events</c> to <c>Naravel.Queue</c>.</para>
/// <para><b>Not ported on purpose:</b> automatic listener discovery and arbitrary runtime type resolution.</para>
/// </remarks>
public interface IQueuedEventListener<in TEvent> : IEventListener<TEvent>;

/// <summary>
/// Invokes an event listener; an optional adapter can replace the direct strategy.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the event dispatcher listener invocation pipeline.</para>
/// <para><b>Why it exists:</b> it allows optional execution adapters, such as Queue, while keeping the core
/// independent of transport packages.</para>
/// <para><b>Not ported on purpose:</b> a generic middleware pipeline or reflection-based listener resolution.</para>
/// </remarks>
public interface IEventListenerInvoker
{
    /// <summary>
    /// Invokes one listener with the event dispatch context.
    /// </summary>
    Task InvokeAsync<TEvent>(
        IEventListener<TEvent> listener,
        TEvent evt,
        EventDispatchContext context,
        CancellationToken cancellationToken);
}
