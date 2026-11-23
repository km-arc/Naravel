namespace Naravel.Cache.Abstractions;

/// <summary>An optional capability of a cache lock: extend the lease of a lock that a token still owns.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Lock::refresh()</c>.</para>
/// <para><b>Why it exists:</b> the lifetime given to <see cref="ICacheLock.AcquireAsync"/> is a lease. Work that runs longer than the lease would otherwise lose the lock while it is still running,
/// and another process could enter. A holder that knows it needs more time extends the lease before it ends. A lock that cannot do this atomically does not implement this interface; code that needs it
/// then sees <c>lock is IRefreshableCacheLock</c> fail instead of getting an unsafe extension. The Memory and Redis locks implement it; the Memcached lock does not, because its <c>touch</c> command does not check an owner.</para>
/// <para><b>Contract:</b> the lease is set to <c>ttl</c> from now, it is not lengthened by <c>ttl</c>. The call succeeds only while the token owns a lease that has not ended. A lease that has already ended is not revived, even when nobody
/// has taken the lock since. Refreshing never creates a lock and never changes the token.</para>
/// <para><b>Intentionally not ported:</b> automatic renewal. A renewal that nobody has to ask for would turn a holder that hangs into a lock that never ends, so the caller decides when and for how long to extend.</para>
/// <para><b>Decorators:</b> a type that wraps an <see cref="ICacheLock"/> and should keep this capability has to implement the interface and forward to the lock it wraps.</para>
/// </remarks>
public interface IRefreshableCacheLock
{
    /// <summary>Sets the lease of the lock <paramref name="name"/> to <paramref name="ttl"/> from now, when <paramref name="token"/> still owns it.</summary>
    /// <param name="name">The lock name that was passed to <see cref="ICacheLock.AcquireAsync"/>.</param>
    /// <param name="token">The ownership token that <see cref="ICacheLock.AcquireAsync"/> returned.</param>
    /// <param name="ttl">The new lifetime, counted from now. It must be greater than zero.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see langword="true"/> when the lease was extended; <see langword="false"/> when the token does not own the lock any more (the lease ended, or another holder took the lock), in which case nothing changed.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="token"/> is <see langword="null"/>, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is zero or less.</exception>
    Task<bool> RefreshAsync(string name, string token, TimeSpan ttl, CancellationToken cancellationToken = default);
}
