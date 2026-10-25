using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Foundation;
using StackExchange.Redis;

namespace Naravel.Redis;

/// <summary>Resolves named Redis connections and owns their multiplexers.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Redis\RedisManager</c> (<c>connection($name)</c>, <c>extend()</c>).</para>
/// <para><b>Why it exists:</b> Cache and Queue each opened their own multiplexers to the same server and one never closed. One multiplexer per name, created on first use and
/// disposed with the manager, removes the duplicate pools and the leak (PDR-010). Runtime <c>Extend</c> and configuration reload come from the base class.</para>
/// <para><b>Ownership:</b> consumers borrow <see cref="IRedisConnection.Multiplexer"/> and never dispose it.</para>
/// <para><b>Secrets:</b> connection strings and passwords are never logged, named or put in exception messages by Naravel; an implicit connection is named by a hash.</para>
/// <para><b>Not ported on purpose:</b> command forwarding through the manager.</para>
/// </remarks>
public sealed class RedisManager : Manager<IRedisConnection, RedisOptions>
{
    internal const string ImplicitPrefix = "implicit-";

    private readonly IDriverRegistry<IRedisConnection> _registry;
    private readonly object _implicitGate = new();

    /// <summary>Creates the manager. Normally resolved from DI after <c>AddNaravelRedis</c>.</summary>
    /// <param name="provider">Service provider handed to connection factories.</param>
    /// <param name="registry">The connection registry.</param>
    /// <param name="options">Live Redis options.</param>
    public RedisManager(IServiceProvider provider, IDriverRegistry<IRedisConnection> registry, IOptionsMonitor<RedisOptions> options)
        : base(provider, registry, options)
    {
        _registry = registry;
    }

    /// <summary>Gets a connection by name, or the default one when <paramref name="name"/> is <see langword="null"/>. Alias for <c>Driver</c>.</summary>
    /// <param name="name">The connection name, or <see langword="null"/> for the default.</param>
    public IRedisConnection Connection(string? name = null) => Driver(name);

    /// <summary>
    /// Gets the connection for a StackExchange.Redis connection string, sharing one multiplexer among every caller that passes the same string
    /// (compared by a hash; the string is kept only inside the factory that opens the connection). This is how the old <c>ConnectionString</c> keys of Cache and Queue
    /// stop opening several connections to one server.
    /// </summary>
    /// <param name="connectionString">The StackExchange.Redis connection string.</param>
    public IRedisConnection FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var name = ImplicitPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)))[..16].ToLowerInvariant();
        lock (_implicitGate)
        {
            if (!_registry.IsRegistered(name))
            {
                _registry.Register(name, provider => Open(provider, name, connectionString));
            }
        }

        return Driver(name);
    }

    internal static IRedisConnection Open(IServiceProvider provider, string name, string connectionString)
    {
        var connect = provider.GetRequiredService<Func<string, IConnectionMultiplexer>>();
        return new RedisConnection(name, connect(connectionString));
    }
}
