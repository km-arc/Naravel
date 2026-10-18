using Microsoft.Extensions.Configuration;
using Naravel.Database.SqlServer;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the SqlServer driver for Naravel.Database.</summary>
public static class SqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds a SqlServer connection for every <c>Database:Connections:&lt;name&gt;</c> whose <c>Driver</c> is <c>sqlserver</c>; each needs a
    /// <c>ConnectionString</c>. Call it after <c>AddNaravelDatabase</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Database</c> by default.</param>
    public static IServiceCollection AddNaravelSqlServerDatabase(this IServiceCollection services, IConfiguration configuration, string sectionName = "Database")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        return services.AddDatabaseConnection(
            configuration,
            "sqlserver",
            (_, name, section) => new SqlServerDatabaseConnection(name, section["ConnectionString"]
                ?? throw new InvalidOperationException($"The sqlserver database connection '{name}' needs 'ConnectionString' in its configuration section.")),
            sectionName);
    }
}
