namespace Naravel.Events;

/// <summary>
/// Dispatches an event instance through the registered listeners.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Contracts\Events\Dispatcher</c>.</para>
/// <para><b>Why it exists:</b> .NET already has delegates, but a type-based listener registry keeps listener
/// lifetimes and registrations explicit without forcing global/static event hooks.</para>
/// <para><b>Not ported on purpose:</b> facade access, PHP discovery, and magic subscriber conventions.</para>
/// </remarks>
public interface IEventDispatcher
{
    /// <summary>
    /// Registers an asynchronous callback for events of <typeparamref name="TEvent"/> in the current DI scope.
    /// </summary>
    /// <param name="listener">The callback, invoked in registration order after DI-registered listeners.</param>
    /// <returns>A subscription that removes the callback when disposed.</returns>
    IDisposable Listen<TEvent>(Func<TEvent, EventDispatchContext, CancellationToken, Task> listener);

    /// <summary>
    /// Dispatches a single event instance to all matching listeners.
    /// </summary>
    Task DispatchAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default);
}
