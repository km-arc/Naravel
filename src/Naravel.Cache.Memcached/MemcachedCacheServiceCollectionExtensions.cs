using Enyim.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Extensions;

namespace Naravel.Cache.Memcached;

/// <summary>Registration of Memcached cache and lock providers.</summary>
public static class MemcachedCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers configured Memcached stores over one shared server pool.
    /// All Memcached stores must declare the same server list; restart the host to change that list.
    /// </summary>
    public static IServiceCollection AddNaravelMemcachedCache(this IServiceCollection services, IConfiguration configuration, string sectionName = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var memcachedStores = configuration.GetSection($"{sectionName}:Stores").GetChildren()
            .Where(store => string.Equals(store["Driver"], "memcached", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (memcachedStores.Length == 0) return services;

        var serverGroups = memcachedStores
            .Select(store => (Store: store.Key, Servers: ReadServers(store)
                .Distinct()
                .OrderBy(server => server.Address, StringComparer.Ordinal)
                .ThenBy(server => server.Port)
                .ToArray()))
            .ToArray();

        foreach (var group in serverGroups)
        {
            if (group.Servers.Length == 0)
            {
                throw new InvalidOperationException($"Memcached store '{group.Store}' must configure at least one server.");
            }
        }

        var servers = serverGroups[0].Servers;
        foreach (var group in serverGroups.Skip(1))
        {
            if (!group.Servers.SequenceEqual(servers))
            {
                throw new InvalidOperationException(
                    $"Memcached stores '{serverGroups[0].Store}' and '{group.Store}' must use the same server list because Naravel registers one shared Memcached client. Restart the host after changing the server list.");
            }
        }

        services.AddEnyimMemcached(options =>
        {
            foreach (var server in servers) options.AddServer(server.Address, server.Port);
        });

        return services.AddCacheDriver(configuration, "memcached",
            (provider, store) => new MemcachedCacheStore(store.Key, provider.GetRequiredService<IMemcachedClient>(), store["Prefix"] ?? store.Key),
            (provider, store) => new MemcachedLock(provider.GetRequiredService<IMemcachedClient>(), store["LockPrefix"] ?? store["Prefix"] ?? store.Key),
            sectionName);
    }

    private static IEnumerable<(string Address, int Port)> ReadServers(IConfigurationSection store)
    {
        var children = store.GetSection("Servers").GetChildren().ToArray();
        if (children.Length > 0)
        {
            foreach (var server in children)
            {
                var serverAddress = string.IsNullOrWhiteSpace(server["Address"])
                    ? "127.0.0.1"
                    : server["Address"]!.Trim().ToLowerInvariant();
                var port = ParsePort(server["Port"], store.Key);
                yield return (serverAddress, port);
            }

            yield break;
        }

        var endpoint = store["Servers"];
        if (string.IsNullOrWhiteSpace(endpoint)) yield break;
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0)
        {
            throw new InvalidOperationException($"Memcached server '{endpoint}' must use the host:port format.");
        }

        var address = endpoint[..separator].Trim().ToLowerInvariant();
        var endpointPort = endpoint[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(endpointPort))
        {
            throw new InvalidOperationException($"Memcached server '{endpoint}' must use the host:port format.");
        }

        yield return (address, ParsePort(endpointPort, store.Key));
    }

    private static int ParsePort(string? value, string storeName)
    {
        if (string.IsNullOrWhiteSpace(value)) return 11211;
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Memcached store '{storeName}' has an invalid server port '{value}'.");
        }

        return port;
    }
}
