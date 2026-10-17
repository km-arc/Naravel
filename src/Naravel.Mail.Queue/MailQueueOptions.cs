namespace Naravel.Mail.Queue;

/// <summary>Where queued mail jobs are placed.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the <c>onConnection</c> / <c>onQueue</c> of a queued mailable. Leave both
/// <see langword="null"/> to use the queue module's default connection and queue.
/// </remarks>
public sealed class MailQueueOptions
{
    /// <summary>Queue connection name, or <see langword="null"/> for the default connection.</summary>
    public string? Connection { get; set; }

    /// <summary>Queue name within the connection, or <see langword="null"/> for <c>default</c>.</summary>
    public string? Queue { get; set; }
}
