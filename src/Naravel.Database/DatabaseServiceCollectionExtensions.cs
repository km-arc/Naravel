using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Database;
using Naravel.Foundation;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the database module and lets driver packages add named connections.</summary>
public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DatabaseManager"/>, the default <see cref="IDatabaseConnection"/> and the pooled <see cref="IConnectionScope"/>.
    /// Configure <c>Database:Default</c> and <c>Database:Connections:&lt;name&gt;:Driver</c>, then add a driver package such as
    /// <c>AddNaravelSqliteDatabase</c>.
    /// </summary>
    /// <remarks><b>Laravel equivalent:</b> <c>DatabaseServiceProvider</c>.</remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Database</c> by default.</param>
    public static IServiceCollection AddNaravelDatabase(this IServiceCollection services, IConfiguration configuration, string sectionName = "Database")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.AddNaravelManager<DatabaseManager, IDatabaseConnection, DatabaseOptions>(configuration.GetSection(sectionName));
        services.TryAddSingleton<IConnectionScope, PooledConnectionScope>();
        return services;
    }

    /// <summary>
    /// For driver packages: registers one factory for every <c>Database:Connections:&lt;name&gt;</c> whose <c>Driver</c> equals
    /// <paramref name="driverName"/>, so each configured name gets its own connection definition.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="driverName">The <c>Driver</c> value the factory handles, for example <c>sqlite</c>.</param>
    /// <param name="factory">Creates the connection from the provider, the connection name and its configuration section.</param>
    /// <param name="sectionName">The configuration section, <c>Database</c> by default.</param>
    public static IServiceCollection AddDatabaseConnection(
        this IServiceCollection services,
        IConfiguration configuration,
        string driverName,
        Func<IServiceProvider, string, IConfigurationSection, IDatabaseConnection> factory,
        string sectionName = "Database")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        ArgumentNullException.ThrowIfNull(factory);

        foreach (var connection in configuration.GetSection($"{sectionName}:Connections").GetChildren())
        {
            if (!string.Equals(connection["Driver"], driverName, StringComparison.OrdinalIgnoreCase)) continue;
            var section = connection;
            services.AddNaravelDriver<IDatabaseConnection>(connection.Key, provider => factory(provider, section.Key, section));
        }

        return services;
    }
}
