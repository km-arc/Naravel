using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Naravel.Database.Sqlite;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the SQLite driver for Naravel.Database.</summary>
public static class SqliteServiceCollectionExtensions
{
    /// <summary>
    /// Adds a SQLite connection for every <c>Database:Connections:&lt;name&gt;</c> whose <c>Driver</c> is <c>sqlite</c>. Each takes either
    /// <c>ConnectionString</c> or <c>Path</c> (a file path, or <c>:memory:</c>). Call it after <c>AddNaravelDatabase</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The configuration section, <c>Database</c> by default.</param>
    public static IServiceCollection AddNaravelSqliteDatabase(this IServiceCollection services, IConfiguration configuration, string sectionName = "Database")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        return services.AddDatabaseConnection(
            configuration,
            "sqlite",
            (_, name, section) => new SqliteDatabaseConnection(name, ResolveConnectionString(name, section)),
            sectionName);
    }

    private static string ResolveConnectionString(string name, IConfigurationSection section)
    {
        var connectionString = section["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        var path = section["Path"];
        if (!string.IsNullOrWhiteSpace(path)) return new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        throw new InvalidOperationException($"The sqlite database connection '{name}' needs either 'ConnectionString' or 'Path' in its configuration section.");
    }
}
