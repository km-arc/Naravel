namespace Naravel.Queue.Dispatch;

/// <summary>
/// Storage seam for <see cref="Naravel.Queue.Jobs.IUniqueJob"/>. <c>Naravel.Queue</c> does not reference Cache, so the
/// lock itself lives in the optional <c>Naravel.Queue.Coordination</c> package (PDR-012b).
/// </summary>
public interface IUniqueJobGate
{
    /// <summary>Tries to claim <paramref name="key"/> for the message <paramref name="owner"/>. Returns false when it is already claimed.</summary>
    Task<bool> TryAcquireAsync(string key, string owner, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Releases <paramref name="key"/> only if <paramref name="owner"/> still holds it; a stale owner is a no-op.</summary>
    Task ReleaseAsync(string key, string owner, CancellationToken cancellationToken);
}
