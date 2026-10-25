using StackExchange.Redis;

namespace Naravel.Redis;

/// <summary>A named Redis connection: the real StackExchange.Redis multiplexer and the name it was configured under.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Redis\Connections\Connection</c>, returned by <c>Redis::connection('cache')</c>.</para>
/// <para><b>Why it exists:</b> the manager hands out the real <see cref="IConnectionMultiplexer"/> and never wraps commands, so every client capability stays available (PDR-010).</para>
/// <para><b>Ownership:</b> the manager owns the multiplexer and disposes it when the manager is disposed. Whoever borrows <see cref="Multiplexer"/> must <b>not</b> dispose it.</para>
/// <para><b>Not ported on purpose:</b> Laravel's command forwarding (<c>Redis::get()</c>).</para>
/// </remarks>
public interface IRedisConnection
{
    /// <summary>The connection name from configuration, or a generated <c>implicit-...</c> name for a connection made from a connection string.</summary>
    string Name { get; }

    /// <summary>The shared multiplexer. Never dispose it.</summary>
    IConnectionMultiplexer Multiplexer { get; }
}
