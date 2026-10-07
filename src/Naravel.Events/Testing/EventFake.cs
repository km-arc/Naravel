using System.Collections.Concurrent;

namespace Naravel.Events.Testing;

/// <summary>
/// In-memory test double that records dispatched events without running listeners.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the event fake used in application tests.</para>
/// <para><b>Why it exists:</b> it asserts event dispatch without invoking side effects.</para>
/// <para><b>Not ported on purpose:</b> global test state or execution of real listeners.</para>
/// </remarks>
public sealed class EventFake : IEventDispatcher
{
    private readonly ConcurrentQueue<object> _events = new();

    /// <inheritdoc />
    public IDisposable Listen<TEvent>(Func<TEvent, EventDispatchContext, CancellationToken, Task> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return EmptySubscription.Instance;
    }

    /// <inheritdoc />
    public Task DispatchAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(evt);
        _events.Enqueue(evt!);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts and returns the first recorded event of the specified type.
    /// </summary>
    public TEvent AssertDispatched<TEvent>()
    {
        var evt = _events.OfType<TEvent>().FirstOrDefault();
        return evt is null
            ? throw new InvalidOperationException($"No {typeof(TEvent).Name} event was dispatched.")
            : evt;
    }

    /// <summary>
    /// Asserts and returns all recorded events of the specified type.
    /// </summary>
    public IReadOnlyList<TEvent> AssertDispatched<TEvent>(int expectedCount)
    {
        var events = _events.OfType<TEvent>().ToArray();
        if (events.Length != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} {typeof(TEvent).Name} events to be dispatched, but found {events.Length}.");
        }

        return events;
    }

    /// <summary>
    /// Gets all recorded events in dispatch order.
    /// </summary>
    public IReadOnlyList<object> All => _events.ToArray();

    /// <summary>
    /// Removes every recorded event.
    /// </summary>
    public void Reset() => _events.Clear();

    private sealed class EmptySubscription : IDisposable
    {
        public static EmptySubscription Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
