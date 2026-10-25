namespace Naravel.Broadcasting;

/// <summary>Sends an <see cref="IBroadcastable"/> event to its channels.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Contracts\Broadcasting\Broadcaster</c> / <c>broadcast($event)</c>.</para>
/// <para><b>Why it exists:</b> one injectable entry point whose transport is chosen by configuration and replaced by <c>BroadcastFake</c> in tests.</para>
/// <para><b>Not ported on purpose:</b> queueing by default. Delivery is inline and at most once (PDR-018).</para>
/// </remarks>
public interface IBroadcaster
{
    /// <summary>Sends <paramref name="evt"/> to every channel it returns. A transport failure is thrown to the caller; a channel nobody listens to is not an error.</summary>
    /// <param name="evt">The event.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task BroadcastAsync(IBroadcastable evt, CancellationToken cancellationToken = default);
}
