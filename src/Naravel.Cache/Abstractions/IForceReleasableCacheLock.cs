namespace Naravel.Cache.Abstractions;

/// <summary>An optional capability of a cache lock: delete a lock whoever owns it.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Lock::forceRelease()</c>.</para>
/// <para><b>Why it exists:</b> a lock whose holder crashed, or whose lease is far longer than the work, blocks everyone until the lease ends. An operator needs a way to clear it without waiting. The Memory and Redis locks implement
/// this interface; a provider without owner-checked deletion must not implement it.</para>
/// <para><b>Contract:</b> the lock is removed whoever owns it, and the token of the previous owner no longer releases or refreshes anything. This is an operator tool. Calling it while a holder is still working lets
/// another process enter at once, so it must not be part of normal release logic: use <see cref="ICacheLock.ReleaseAsync"/> for that.</para>
/// <para><b>Decorators:</b> a type that wraps an <see cref="ICacheLock"/> and should keep this capability has to implement the interface and forward to the lock it wraps.</para>
/// </remarks>
public interface IForceReleasableCacheLock
{
    /// <summary>Deletes the lock <paramref name="name"/> whoever owns it.</summary>
    /// <param name="name">The lock name that was passed to <see cref="ICacheLock.AcquireAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see langword="true"/> when a lock that had not expired was deleted; <see langword="false"/> when there was none.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty or white space.</exception>
    Task<bool> ForceReleaseAsync(string name, CancellationToken cancellationToken = default);
}
