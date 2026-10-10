namespace Naravel.Events;

/// <summary>
/// Mutable request-scoped state for one dispatch.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> stopping propagation in the event dispatcher.</para>
/// <para><b>Why it exists:</b> it provides an explicit typed signal to stop later listeners.</para>
/// <para><b>Not ported on purpose:</b> boolean return-value conventions and mutable global event state.</para>
/// </remarks>
public sealed class EventDispatchContext
{
    /// <summary>
    /// Gets whether propagation should stop before the next listener runs.
    /// </summary>
    public bool StopPropagation { get; private set; }

    /// <summary>
    /// Stops future listeners from running for the current dispatch.
    /// </summary>
    public void Stop() => StopPropagation = true;
}
