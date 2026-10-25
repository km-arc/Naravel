using System.Collections.Concurrent;

namespace Naravel.Broadcasting;

/// <summary>What this instance knows about its own connections, so it can refresh and clean up their presence entries.</summary>
internal sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PresenceMember>> _connections = new(StringComparer.Ordinal);

    public void Add(string connectionId, string channel, PresenceMember member) =>
        _connections.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, PresenceMember>(StringComparer.Ordinal))[channel] = member;

    public void Remove(string connectionId, string channel)
    {
        if (_connections.TryGetValue(connectionId, out var channels)) channels.TryRemove(channel, out _);
    }

    public IReadOnlyList<string> RemoveAll(string connectionId) =>
        _connections.TryRemove(connectionId, out var channels) ? channels.Keys.ToArray() : [];

    public IReadOnlyList<(string ConnectionId, string Channel, PresenceMember Member)> Snapshot() =>
        _connections.SelectMany(c => c.Value.Select(m => (c.Key, m.Key, m.Value))).ToArray();
}

internal sealed class PresenceService(PresenceStore store, PresenceTracker tracker) : IPresence
{
    public Task<IReadOnlyList<PresenceMember>> MembersAsync(string channel, CancellationToken cancellationToken = default)
    {
        if (!ChannelNames.IsValid(channel)) throw new ArgumentException("The channel name is not valid.", nameof(channel));
        return store.MembersAsync(channel, cancellationToken);
    }

    public async Task<(bool IsNewMember, IReadOnlyList<PresenceMember> Members)> JoinAsync(
        string channel, string connectionId, PresenceMember member, CancellationToken cancellationToken)
    {
        var result = await store.JoinAsync(channel, connectionId, member, cancellationToken).ConfigureAwait(false);
        tracker.Add(connectionId, channel, member);
        return result;
    }

    public Task<PresenceMember?> LeaveAsync(string channel, string connectionId, CancellationToken cancellationToken)
    {
        tracker.Remove(connectionId, channel);
        return store.LeaveAsync(channel, connectionId, cancellationToken);
    }

    public async Task<IReadOnlyList<(string Channel, PresenceMember? Left)>> LeaveAllAsync(string connectionId, CancellationToken cancellationToken)
    {
        var results = new List<(string, PresenceMember?)>();
        foreach (var channel in tracker.RemoveAll(connectionId))
        {
            results.Add((channel, await store.LeaveAsync(channel, connectionId, cancellationToken).ConfigureAwait(false)));
        }

        return results;
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        foreach (var (connectionId, channel, member) in tracker.Snapshot())
        {
            await store.JoinAsync(channel, connectionId, member, cancellationToken).ConfigureAwait(false);
        }
    }
}
