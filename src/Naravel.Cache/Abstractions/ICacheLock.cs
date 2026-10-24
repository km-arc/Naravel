namespace Naravel.Cache.Abstractions;

/// <summary>A token-owned lock provided by a named cache backend.</summary>
/// <remarks><b>Laravel equivalent:</b> the lock contract behind <c>Cache::lock()</c>.</remarks>
public interface ICacheLock
{
    /// <summary>Attempts to acquire a lock and returns its ownership token, or null when already held.</summary>
    Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Releases a lock only when the supplied token still owns it.</summary>
    Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the lock is visible to every process that uses the same backend (Redis, Memcached) or only to this process (the memory driver).
    /// Code that must hold across several instances, such as <c>OnOneServer</c> in <c>Naravel.Scheduling</c>, refuses a driver that is not shared. The default is <c>true</c>.
    /// </summary>
    bool IsShared => true;
}
