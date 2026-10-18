using Microsoft.Extensions.Configuration;
using Naravel.Database.MySql;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the MySql driver for Naravel.Database.</summary>
public static class MySqlServiceCollectionExtensions
{
    /// <summary>
    /// Adds a MySql connection for every <c>Database:Connections:&lt;name&gt;</c> whose <c>Driver</c> is <c>mysql</c>; each needs a
    /// <c>ConnectionString</c>. Call it after <c>AddNaravelDatabase</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Database</c> by default.</param>
    public static IServiceCollection AddNaravelMySqlDatabase(this IServiceCollection services, IConfiguration configuration, string sectionName = "Database")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        return services.AddDatabaseConnection(
            configuration,
            "mysql",
            (_, name, section) => new MySqlDatabaseConnection(name, section["ConnectionString"]
                ?? throw new InvalidOperationException($"The mysql database connection '{name}' needs 'ConnectionString' in its configuration section.")),
            sectionName);
    }
}
