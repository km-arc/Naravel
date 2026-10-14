using Naravel.Cache.Abstractions;
using Naravel.Queue.Dispatch;

namespace Naravel.Queue.Coordination;

/// <summary>
/// <see cref="IUniqueJobGate"/> on top of the Cache lock (PDR-012b). The lock decides who owns a key; a second entry records
/// which message holds it, because the worker that releases the key knows the message id but not the lock token.
/// </summary>
/// <remarks>
/// Acquisition is exactly as atomic as the configured <see cref="ICacheLock"/> (cross-process with Redis, single-process with
/// Memory). Recording the owner is a separate write: if the process dies between the two, the key stays locked until its TTL.
/// Release re-checks the owner, so a worker that outlived its TTL cannot free a key now held by a newer dispatch.
/// </remarks>
internal sealed class CacheUniqueJobGate(ICacheLock locks, ICacheStore store) : IUniqueJobGate
{
    private const char Separator = '|';

    public async Task<bool> TryAcquireAsync(string key, string owner, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(owner);
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl), "The unique lock lifetime must be positive.");

        var token = await locks.AcquireAsync(CoordinationKeys.Unique(key), ttl, cancellationToken).ConfigureAwait(false);
        if (token is null) return false;

        try
        {
            await store.SetAsync(CoordinationKeys.UniqueOwner(key), owner + Separator + token, ttl, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Without the owner record nobody could release this key, so give it back right away.
            await locks.ReleaseAsync(CoordinationKeys.Unique(key), token, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return true;
    }

    public async Task ReleaseAsync(string key, string owner, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(owner);

        var ownerKey = CoordinationKeys.UniqueOwner(key);
        var (found, value) = await store.TryGetAsync<string>(ownerKey, cancellationToken).ConfigureAwait(false);
        if (!found || value is null) return;

        var separator = value.IndexOf(Separator);
        if (separator <= 0 || !string.Equals(value[..separator], owner, StringComparison.Ordinal)) return;

        await locks.ReleaseAsync(CoordinationKeys.Unique(key), value[(separator + 1)..], cancellationToken).ConfigureAwait(false);
        await store.RemoveAsync(ownerKey, cancellationToken).ConfigureAwait(false);
    }
}
