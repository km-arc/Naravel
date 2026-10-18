using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Sample.App.Data;

namespace Naravel.Sample.Config;

public static class DatabaseSampleConfig
{
    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        // The connection comes from the "Database" section of config/appsettings.json: Default = main, main = sqlite in memory.
        // Use Driver mysql, pgsql or sqlserver with a ConnectionString (and the matching Add...Database call) to run the same demo elsewhere.
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);

        // Keeps the last queries (SQL, bindings, duration) so the page can show what actually ran. Development and tests only: it holds values.
        services.AddNaravelDatabaseQueryLog();

        services.AddSingleton<PeopleSeeder>();
    }
}
