using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Naravel.Cache.Abstractions;

namespace Naravel.Broadcasting;

/// <summary>
/// The member list of each presence channel, kept in the shared cache so every instance sees the same members.
/// One cache entry per channel holds one record per connection; each change is a read-modify-write under a per-channel cache lock, so two instances cannot overwrite each other.
/// </summary>
internal sealed class PresenceStore(
    ICacheStore cache,
    ICacheLock locks,
    TimeProvider time,
    IOptionsMonitor<BroadcastingOptions> options,
    ILogger logger)
{
    private const string Prefix = "broadcasting:presence:";
    private int _warned;

    internal sealed class Entry
    {
        public string ConnectionId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string? Info { get; set; }
        public long ExpiresAt { get; set; }
    }

    /// <summary>Adds or refreshes one connection. <c>IsNewMember</c> is true when the user had no other live connection.</summary>
    public Task<(bool IsNewMember, IReadOnlyList<PresenceMember> Members)> JoinAsync(
        string channel, string connectionId, PresenceMember member, CancellationToken cancellationToken)
    {
        var ttl = Ttl();
        return WithLockAsync(channel, async () =>
        {
            var now = Now();
            var entries = await LoadAsync(channel, now, cancellationToken).ConfigureAwait(false);
            entries.RemoveAll(e => e.ConnectionId == connectionId);
            var isNew = !entries.Exists(e => e.UserId == member.UserId);
            entries.Add(new Entry
            {
                ConnectionId = connectionId,
                UserId = member.UserId,
                Info = member.Info?.GetRawText(),
                ExpiresAt = now + (long)ttl.TotalSeconds,
            });
            await SaveAsync(channel, entries, ttl, cancellationToken).ConfigureAwait(false);
            return (isNew, ToMembers(entries));
        }, cancellationToken);
    }

    /// <summary>Removes one connection. Returns the member when that was the user's last live connection, otherwise <see langword="null"/>.</summary>
    public Task<PresenceMember?> LeaveAsync(string channel, string connectionId, CancellationToken cancellationToken)
    {
        var ttl = Ttl();
        return WithLockAsync<PresenceMember?>(channel, async () =>
        {
            var entries = await LoadAsync(channel, Now(), cancellationToken).ConfigureAwait(false);
            var leaving = entries.Find(e => e.ConnectionId == connectionId);
            if (leaving is null) return null;

            entries.Remove(leaving);
            await SaveAsync(channel, entries, ttl, cancellationToken).ConfigureAwait(false);
            return entries.Exists(e => e.UserId == leaving.UserId) ? null : PresenceMember.FromStored(leaving.UserId, leaving.Info);
        }, cancellationToken);
    }

    /// <summary>Reads the members without taking the lock; a read may be a moment old.</summary>
    public async Task<IReadOnlyList<PresenceMember>> MembersAsync(string channel, CancellationToken cancellationToken)
    {
        var entries = await LoadAsync(channel, Now(), cancellationToken).ConfigureAwait(false);
        return ToMembers(entries);
    }

    private TimeSpan Ttl()
    {
        var ttl = options.CurrentValue.PresenceEntryTtl;
        if (ttl < TimeSpan.FromSeconds(3))
        {
            throw new InvalidOperationException("BroadcastingOptions.PresenceEntryTtl must be at least 3 seconds.");
        }

        return ttl;
    }

    private long Now() => time.GetUtcNow().ToUnixTimeSeconds();

    private static string Key(string channel) => Prefix + channel;

    private async Task<List<Entry>> LoadAsync(string channel, long now, CancellationToken cancellationToken)
    {
        var (found, json) = await cache.TryGetAsync<string>(Key(channel), cancellationToken).ConfigureAwait(false);
        if (!found || string.IsNullOrEmpty(json)) return [];
        var entries = JsonSerializer.Deserialize(json, PresenceJsonContext.Default.ListEntry) ?? [];
        entries.RemoveAll(e => e.ExpiresAt <= now);
        return entries;
    }

    private async Task SaveAsync(string channel, List<Entry> entries, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            await cache.RemoveAsync(Key(channel), cancellationToken).ConfigureAwait(false);
            return;
        }

        // The cache entry outlives its records, so a record is judged by its own ExpiresAt.
        await cache.SetAsync(Key(channel), JsonSerializer.Serialize(entries, PresenceJsonContext.Default.ListEntry), ttl * 2, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<PresenceMember> ToMembers(List<Entry> entries)
    {
        var members = new List<PresenceMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (seen.Add(entry.UserId)) members.Add(PresenceMember.FromStored(entry.UserId, entry.Info));
        }

        return members;
    }

    private async Task<T> WithLockAsync<T>(string channel, Func<Task<T>> body, CancellationToken cancellationToken)
    {
        if (!locks.IsShared && Interlocked.Exchange(ref _warned, 1) == 0)
        {
            logger.LogWarning(
                "Presence member lists are kept per process because the cache lock is not shared; with several instances each one sees only its own members. Register a Redis or Memcached cache.");
        }

        var name = Prefix + "lock:" + channel;
        string? token = null;
        for (var attempt = 0; attempt < 100 && token is null; attempt++)
        {
            token = await locks.AcquireAsync(name, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            if (token is null) await Task.Delay(TimeSpan.FromMilliseconds(20), time, cancellationToken).ConfigureAwait(false);
        }

        if (token is null) throw new TimeoutException("The presence list of the channel is busy; try again.");

        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            await locks.ReleaseAsync(name, token, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
