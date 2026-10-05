using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Foundation;
using Naravel.Queue.Drivers;

namespace Naravel.Queue.Extensions;

/// <summary>
/// Shared registration logic used by every <c>AddXxxDriver()</c> extension method
/// (<c>Naravel.Queue.Memory</c>, <c>.File</c>, <c>.Redis</c>, <c>.Database</c>, <c>.RabbitMQ</c>, <c>.Kafka</c>),
/// so each driver package only supplies "how do I build one instance", not "how do I find which stores use me".
/// </summary>
/// <remarks>
/// <b>Why store names are scanned eagerly, at startup:</b> <see cref="IDriverRegistry{TDriver}"/> factories
/// are registered per fixed name (Laravel's <c>Manager::extend($name, $factory)</c> shape). A store's
/// <c>"Driver"</c> key can only be read once, when <c>AddXxxDriver(configuration)</c> runs, to decide which
/// store names to register a factory for - a store added to configuration <i>after</i> the host starts (not
/// just a changed value inside an existing store) is not picked up automatically. This is a known,
/// documented limit (see docs/en/queue.md "Limitations"), not an oversight: Laravel itself does not let you
/// add a brand new named connection without a code change either.
/// </remarks>
public static class QueueDriverRegistrationExtensions
{
    /// <summary>
    /// Finds every store configured under <c>{sectionName}:Stores</c> whose own <c>"Driver"</c> value equals
    /// <paramref name="driverName"/> (case-insensitive), and registers <paramref name="factory"/> for each
    /// one under that store's own name. Two stores may use the same driver type with different settings
    /// (e.g. two independent Redis connections); a factory only has to build one instance per store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration (the whole root, not a pre-narrowed section).</param>
    /// <param name="driverName">The literal driver-type name this package handles, e.g. <c>"redis"</c>.</param>
    /// <param name="factory">Builds one driver instance from the service provider and the matching store's own section.</param>
    /// <param name="sectionName">The module's top-level configuration section. Defaults to <c>"NaravelQueue"</c>.</param>
    public static IServiceCollection AddQueueDriver(
        this IServiceCollection services,
        IConfiguration configuration,
        string driverName,
        Func<IServiceProvider, IConfigurationSection, IQueueDriver> factory,
        string sectionName = "NaravelQueue")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        ArgumentNullException.ThrowIfNull(factory);

        foreach (var store in configuration.GetSection($"{sectionName}:Stores").GetChildren())
        {
            if (!string.Equals(store["Driver"], driverName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Capture the store's own IConfigurationSection (a live view) so a config reload that changes
            // e.g. a connection string is picked up the next time Manager rebuilds this driver.
            var section = store;
            services.AddNaravelDriver<IQueueDriver>(store.Key, sp => factory(sp, section));
        }

        return services;
    }
}
