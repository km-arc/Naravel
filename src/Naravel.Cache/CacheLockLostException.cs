namespace Naravel.Cache;

/// <summary>The lease of a lock was lost while the work that it protected was running, so another process may have entered the same critical section.</summary>
/// <remarks>
/// <para>Thrown by the <c>BlockAsync</c> and <c>TryRunAsync</c> overloads of <see cref="CacheLockExtensions"/> that take a <see cref="CacheLockLeaseOptions"/>, after the callback has ended, and only in two cases:
/// the callback returned normally although the lease had been lost, or the callback threw an <see cref="OperationCanceledException"/> because the lease was lost (that exception is then the <see cref="Exception.InnerException"/>).
/// An exception that the callback threw for any other reason is never replaced by this one.</para>
/// <para>The work is not undone. The exception says only that the lock did not protect it for the whole run, so the caller decides whether to repeat, compensate or report.</para>
/// </remarks>
public sealed class CacheLockLostException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="lockName">The name of the lock whose lease was lost.</param>
    /// <param name="message">What happened, without any secret.</param>
    public CacheLockLostException(string lockName, string message)
        : base(message)
    {
        LockName = lockName;
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="lockName">The name of the lock whose lease was lost.</param>
    /// <param name="message">What happened, without any secret.</param>
    /// <param name="innerException">The exception that came with the loss: a failed renewal, or the cancellation that the callback threw.</param>
    public CacheLockLostException(string lockName, string message, Exception? innerException)
        : base(message, innerException)
    {
        LockName = lockName;
    }

    /// <summary>The name of the lock whose lease was lost.</summary>
    public string LockName { get; }
}
