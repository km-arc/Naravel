using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Foundation;
using Naravel.Redis;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration for <c>Naravel.Redis</c>.</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisManager"/> and one named connection for every <c>Redis:Stores:&lt;name&gt;</c>. The default connection is also injectable as <see cref="IRedisConnection"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> the Redis service provider and <c>redis.*</c> connections.</para>
    /// <para><b>Why it exists:</b> named connections shared by Cache, Queue and your own code, with one owner for disposal (PDR-010). The multiplexer is opened with
    /// <c>ConnectionMultiplexer.Connect</c> on first use of a name; tests replace the opener by registering a <c>Func&lt;string, IConnectionMultiplexer&gt;</c>.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Redis</c> by default.</param>
    public static IServiceCollection AddNaravelRedis(this IServiceCollection services, IConfiguration configuration, string sectionName = "Redis")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName);
        services.AddNaravelManager<RedisManager, IRedisConnection, RedisOptions>(section);
        services.TryAddSingleton<Func<string, IConnectionMultiplexer>>(_ => connectionString => ConnectionMultiplexer.Connect(connectionString));

        foreach (var store in section.GetSection("Stores").GetChildren())
        {
            var storeSection = store;
            services.AddNaravelDriver<IRedisConnection>(store.Key, provider => RedisManager.Open(
                provider,
                storeSection.Key,
                storeSection["ConnectionString"] ?? throw new InvalidOperationException($"Redis connection '{storeSection.Key}' requires ConnectionString.")));
        }

        return services;
    }
}
