namespace Naravel.Queue.Encryption;

/// <summary>Options for encrypted queue jobs.</summary>
/// <remarks><b>Laravel equivalent:</b> none; this is the migration switch PDR-021 adds so a job class can be marked while old messages are still queued.</remarks>
public sealed class QueueEncryptionOptions
{
    /// <summary>
    /// When <see langword="false"/> (the default), a job of a marked class whose stored payload is plaintext is refused with a clear message, so protection is
    /// never dropped silently. Switch it on for a deploy window so messages queued before the class was marked can finish, then switch it off again.
    /// </summary>
    public bool AcceptPlaintextForMarkedJobs { get; set; }
}
