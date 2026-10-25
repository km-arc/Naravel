using System.Collections.Concurrent;

namespace Naravel.Broadcasting.Testing;

/// <summary>
/// In-memory <see cref="IBroadcaster"/> that records broadcasts and asserts on them.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Event::fake()</c> / <c>Broadcast::fake()</c> assertions.</para>
/// <para><b>Why it exists:</b> inject it where <see cref="IBroadcaster"/> is expected and assert on the data, with no SignalR server or client.</para>
/// <para><b>Not ported on purpose:</b> global fake state; the fake is an ordinary object you pass in.</para>
/// </remarks>
public sealed class BroadcastFake : IBroadcaster
{
    private readonly ConcurrentQueue<BroadcastMessage> _broadcasts = new();

    /// <summary>Messages recorded by <see cref="BroadcastAsync"/>, oldest first.</summary>
    public IReadOnlyList<BroadcastMessage> Broadcasts => _broadcasts.ToArray();

    /// <inheritdoc />
    public Task BroadcastAsync(IBroadcastable evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        cancellationToken.ThrowIfCancellationRequested();
        _broadcasts.Enqueue(BroadcastMessage.From(evt));
        return Task.CompletedTask;
    }

    /// <summary>Asserts that a broadcast (matching <paramref name="predicate"/> when given) happened, and returns the first match.</summary>
    /// <param name="predicate">Optional filter.</param>
    /// <exception cref="InvalidOperationException">No such broadcast happened.</exception>
    public BroadcastMessage AssertBroadcast(Func<BroadcastMessage, bool>? predicate = null) =>
        _broadcasts.FirstOrDefault(m => predicate is null || predicate(m))
        ?? throw new InvalidOperationException(
            predicate is not null
                ? $"No matching broadcast was recorded ({_broadcasts.Count} in total)."
                : "No broadcast was recorded.");

    /// <summary>Asserts that exactly <paramref name="expectedCount"/> broadcasts happened.</summary>
    /// <param name="expectedCount">The expected number.</param>
    /// <exception cref="InvalidOperationException">The count differs.</exception>
    public IReadOnlyList<BroadcastMessage> AssertBroadcastCount(int expectedCount)
    {
        var all = _broadcasts.ToArray();
        return all.Length == expectedCount
            ? all
            : throw new InvalidOperationException($"Expected {expectedCount} broadcast(s), but found {all.Length}.");
    }

    /// <summary>Asserts that nothing was broadcast.</summary>
    /// <exception cref="InvalidOperationException">At least one broadcast was recorded.</exception>
    public void AssertNothingBroadcast()
    {
        if (!_broadcasts.IsEmpty)
        {
            throw new InvalidOperationException($"Expected no broadcast, but {_broadcasts.Count} were recorded.");
        }
    }

    /// <summary>Forgets every recorded broadcast.</summary>
    public void Reset() => _broadcasts.Clear();
}
