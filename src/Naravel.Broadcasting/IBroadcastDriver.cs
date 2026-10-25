namespace Naravel.Broadcasting;

/// <summary>A broadcasting transport: <c>signalr</c>, <c>log</c>, <c>null</c> or one you register.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a broadcast driver (<c>pusher</c>, <c>log</c>, <c>null</c>).</para>
/// <para><b>Why it exists:</b> the named-driver seam of <see cref="BroadcastManager"/>.</para>
/// </remarks>
public interface IBroadcastDriver
{
    /// <summary>Delivers one message to every channel in it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendAsync(BroadcastMessage message, CancellationToken cancellationToken = default);
}
