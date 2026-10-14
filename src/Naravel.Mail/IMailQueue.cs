namespace Naravel.Mail;

/// <summary>
/// Implemented by <c>Naravel.Mail.Queue</c> to accept a built message for later delivery. Core <c>Naravel.Mail</c> does not
/// reference the queue; it only calls this seam.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>Mail::queue()</c>. <b>Why it exists:</b> keeps the dependency pointing one way
/// (PDR-013a). <b>Not ported:</b> queueing the <see cref="Mailable"/> object itself.
/// </remarks>
public interface IMailQueue
{
    /// <summary>Enqueues a message built from a mailable.</summary>
    /// <param name="message">The built message (data only).</param>
    /// <param name="transport">The transport name the worker must use, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">Cancels the enqueue.</param>
    Task EnqueueAsync(MailMessage message, string? transport, CancellationToken cancellationToken = default);
}
