using Microsoft.Extensions.Configuration;
using Naravel.Database.Postgres;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Postgres driver for Naravel.Database.</summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Adds a Postgres connection for every <c>Database:Connections:&lt;name&gt;</c> whose <c>Driver</c> is <c>pgsql</c> (Laravel's name); each needs a
    /// <c>ConnectionString</c>. Call it after <c>AddNaravelDatabase</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Database</c> by default.</param>
    public static IServiceCollection AddNaravelPostgresDatabase(this IServiceCollection services, IConfiguration configuration, string sectionName = "Database")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        return services.AddDatabaseConnection(
            configuration,
            "pgsql",
            (_, name, section) => new PostgresDatabaseConnection(name, section["ConnectionString"]
                ?? throw new InvalidOperationException($"The pgsql database connection '{name}' needs 'ConnectionString' in its configuration section.")),
            sectionName);
    }
}
