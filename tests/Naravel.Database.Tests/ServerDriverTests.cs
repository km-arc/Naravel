using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;
using Naravel.Database.MySql;
using Naravel.Database.Postgres;
using Naravel.Database.SqlServer;
using Naravel.Testing;

namespace Naravel.Database.Tests;

/// <summary>
/// The MySQL, PostgreSQL and SQL Server drivers: registration, <c>Extend</c> and reload need no server; the open-connection tests run only when
/// <c>NARAVEL_TEST_MYSQL</c>, <c>NARAVEL_TEST_POSTGRES</c> or <c>NARAVEL_TEST_SQLSERVER</c> holds a connection string (CI <c>database</c> job). Skipped means NOT RUN.
/// </summary>
public class ServerDriverTests
{
    private static ConfigurationManager Config(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values.ToDictionary(item => $"Database:{item.Key}", item => item.Value));
        return configuration;
    }

    private static ServiceProvider BuildAll(ConfigurationManager configuration)
    {
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        services.AddNaravelMySqlDatabase(configuration);
        services.AddNaravelPostgresDatabase(configuration);
        services.AddNaravelSqlServerDatabase(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Each_configured_connection_is_created_by_the_driver_package_that_owns_its_Driver_value()
    {
        using var provider = BuildAll(Config(
            ("Connections:lite:Driver", "sqlite"), ("Connections:lite:Path", ":memory:"),
            ("Connections:my:Driver", "mysql"), ("Connections:my:ConnectionString", "Server=localhost;Database=x"),
            ("Connections:pg:Driver", "pgsql"), ("Connections:pg:ConnectionString", "Host=localhost;Database=x"),
            ("Connections:ms:Driver", "sqlserver"), ("Connections:ms:ConnectionString", "Server=localhost;Database=x")));
        var manager = provider.GetRequiredService<DatabaseManager>();

        manager.Connection("lite").Dialect.Should().Be(DatabaseDialect.Sqlite);
        manager.Connection("my").Should().BeOfType<MySqlDatabaseConnection>().Which.Dialect.Should().Be(DatabaseDialect.MySql);
        manager.Connection("pg").Should().BeOfType<PostgresDatabaseConnection>().Which.Dialect.Should().Be(DatabaseDialect.Postgres);
        manager.Connection("ms").Should().BeOfType<SqlServerDatabaseConnection>().Which.Dialect.Should().Be(DatabaseDialect.SqlServer);
        manager.Connection("my").Name.Should().Be("my");
    }

    [Theory]
    [InlineData("mysql")]
    [InlineData("pgsql")]
    [InlineData("sqlserver")]
    public void A_server_connection_without_a_connection_string_explains_what_is_missing(string driver)
    {
        using var provider = BuildAll(Config(("Connections:bad:Driver", driver)));

        var act = () => provider.GetRequiredService<DatabaseManager>().Connection("bad");

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{driver}*ConnectionString*");
    }

    [Theory]
    [InlineData("mysql", "Server=a;Database=x", "Server=b;Database=x")]
    [InlineData("pgsql", "Host=a;Database=x", "Host=b;Database=x")]
    [InlineData("sqlserver", "Server=a;Database=x", "Server=b;Database=x")]
    public void Config_reload_rebuilds_a_changed_server_connection_and_ignores_unrelated_keys(string driver, string before, string after)
    {
        var config = Config(("Default", "main"), ("Connections:main:Driver", driver), ("Connections:main:ConnectionString", before));
        using var provider = BuildAll(config);
        var manager = provider.GetRequiredService<DatabaseManager>();
        var original = manager.Connection("main");

        config["Database:Unrelated"] = "x";
        ((IConfigurationRoot)config).Reload();
        manager.Connection("main").Should().BeSameAs(original);

        config["Database:Connections:main:ConnectionString"] = after;
        ((IConfigurationRoot)config).Reload();
        manager.Connection("main").Should().NotBeSameAs(original);
    }

    [Fact]
    public void A_server_connection_can_be_added_at_runtime_with_Extend()
    {
        using var provider = BuildAll(Config());
        var manager = provider.GetRequiredService<DatabaseManager>();
        var custom = new MySqlDatabaseConnection("custom", "Server=localhost;Database=x");

        manager.Extend("custom", _ => custom);

        manager.Connection("custom").Should().BeSameAs(custom);
    }

    [Fact]
    public async Task A_disposed_postgres_connection_refuses_new_connections()
    {
        var connection = new PostgresDatabaseConnection("x", "Host=localhost;Database=x");
        await connection.DisposeAsync();

        var act = async () => await connection.OpenConnectionAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static async Task AssertSelectOneAsync(string environmentVariable, string driver, Action<IServiceCollection, IConfiguration> register)
    {
        var configuration = Config(("Default", "main"), ("Connections:main:Driver", driver), ("Connections:main:ConnectionString", Environment.GetEnvironmentVariable(environmentVariable)));
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        register(services, configuration);
        await using var provider = services.BuildServiceProvider();

        var value = await provider.GetRequiredService<IDatabase>().ScalarAsync<object>("select 1");

        Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture).Should().Be(1);
    }

    [ServiceFact("NARAVEL_TEST_MYSQL")]
    [Trait("Dialect", "MySql")]
    public Task MySql_opens_a_connection_and_runs_a_query() =>
        AssertSelectOneAsync("NARAVEL_TEST_MYSQL", "mysql", (s, c) => s.AddNaravelMySqlDatabase(c));

    [ServiceFact("NARAVEL_TEST_POSTGRES")]
    [Trait("Dialect", "Postgres")]
    public Task Postgres_opens_a_connection_and_runs_a_query() =>
        AssertSelectOneAsync("NARAVEL_TEST_POSTGRES", "pgsql", (s, c) => s.AddNaravelPostgresDatabase(c));

    [ServiceFact("NARAVEL_TEST_SQLSERVER")]
    [Trait("Dialect", "SqlServer")]
    public Task SqlServer_opens_a_connection_and_runs_a_query() =>
        AssertSelectOneAsync("NARAVEL_TEST_SQLSERVER", "sqlserver", (s, c) => s.AddNaravelSqlServerDatabase(c));
}
