using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;
using Naravel.Database.Sqlite;

namespace Naravel.Database.Tests;

public class DatabaseManagerTests
{
    private sealed class FakeConnection(string name) : IDatabaseConnection
    {
        public string Name { get; } = name;
        public DatabaseDialect Dialect => DatabaseDialect.Sqlite;

        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("not opened in this test");
    }

    private static ConfigurationManager Config(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values.ToDictionary(item => $"Database:{item.Key}", item => item.Value));
        return configuration;
    }

    private static ServiceProvider Build(ConfigurationManager configuration)
    {
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<long> CountAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public void Without_a_configured_default_asking_for_the_default_connection_fails_clearly()
    {
        using var provider = Build(Config());

        var act = () => provider.GetRequiredService<DatabaseManager>().Connection();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_default_connection_comes_from_configuration_and_reports_the_sqlite_dialect()
    {
        using var provider = Build(Config(("Default", "main"), ("Connections:main:Driver", "sqlite"), ("Connections:main:Path", ":memory:")));

        var connection = provider.GetRequiredService<DatabaseManager>().Connection();

        connection.Name.Should().Be("main");
        connection.Dialect.Should().Be(DatabaseDialect.Sqlite);
        provider.GetRequiredService<IDatabaseConnection>().Should().BeSameAs(connection);
    }

    [Fact]
    public void Connections_with_another_driver_are_not_claimed_by_the_sqlite_package()
    {
        using var provider = Build(Config(
            ("Connections:lite:Driver", "sqlite"),
            ("Connections:lite:Path", ":memory:"),
            ("Connections:other:Driver", "mysql"),
            ("Connections:other:ConnectionString", "Server=x")));
        var manager = provider.GetRequiredService<DatabaseManager>();

        manager.Connection("lite").Name.Should().Be("lite");
        var act = () => manager.Connection("other");

        act.Should().Throw<Naravel.Foundation.DriverNotRegisteredException>();
    }

    [Fact]
    public void A_sqlite_connection_without_path_or_connection_string_explains_what_is_missing()
    {
        using var provider = Build(Config(("Connections:bad:Driver", "sqlite")));

        var act = () => provider.GetRequiredService<DatabaseManager>().Connection("bad");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString*Path*");
    }

    [Fact]
    public void A_connection_can_be_added_at_runtime_with_Extend()
    {
        using var provider = Build(Config());
        var manager = provider.GetRequiredService<DatabaseManager>();
        var custom = new FakeConnection("custom");

        manager.Extend("custom", _ => custom);

        manager.Connection("custom").Should().BeSameAs(custom);
    }

    [Fact]
    public void Config_reload_rebuilds_a_changed_connection_ignores_unrelated_keys_and_follows_a_new_default()
    {
        var config = Config(("Default", "a"), ("Connections:a:Driver", "sqlite"), ("Connections:a:Path", ":memory:"), ("Connections:b:Driver", "sqlite"), ("Connections:b:Path", ":memory:"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<DatabaseManager>();
        var original = manager.Connection("a");

        config["Database:Unrelated"] = "x";
        ((IConfigurationRoot)config).Reload();
        manager.Connection("a").Should().BeSameAs(original);

        config["Database:Connections:a:Path"] = "file:other?mode=memory";
        ((IConfigurationRoot)config).Reload();
        manager.Connection("a").Should().NotBeSameAs(original);

        config["Database:Default"] = "b";
        ((IConfigurationRoot)config).Reload();
        manager.Connection().Name.Should().Be("b");
    }

    [Fact]
    public async Task The_pooled_scope_hands_out_an_owned_connection_without_a_transaction_and_closes_it_on_dispose()
    {
        using var provider = Build(Config(("Default", "main"), ("Connections:main:Driver", "sqlite"), ("Connections:main:Path", ":memory:")));
        var scope = provider.GetRequiredService<IConnectionScope>();

        DbConnection connection;
        await using (var lease = await scope.AcquireAsync(null))
        {
            lease.OwnsConnection.Should().BeTrue();
            lease.Transaction.Should().BeNull();
            connection = lease.Connection;
            connection.State.Should().Be(System.Data.ConnectionState.Open);
        }

        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public async Task An_in_memory_database_keeps_its_data_across_separate_leases()
    {
        using var provider = Build(Config(("Default", "main"), ("Connections:main:Driver", "sqlite"), ("Connections:main:Path", ":memory:")));
        var scope = provider.GetRequiredService<IConnectionScope>();

        await using (var first = await scope.AcquireAsync("main"))
        {
            await ExecuteAsync(first.Connection, "CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1), (2)");
        }

        await using var second = await scope.AcquireAsync("main");
        (await CountAsync(second.Connection, "t")).Should().Be(2);
    }

    [Fact]
    public async Task Two_in_memory_connection_names_never_share_a_database()
    {
        using var provider = Build(Config(
            ("Connections:one:Driver", "sqlite"), ("Connections:one:Path", ":memory:"),
            ("Connections:two:Driver", "sqlite"), ("Connections:two:Path", ":memory:")));
        var scope = provider.GetRequiredService<IConnectionScope>();

        await using (var one = await scope.AcquireAsync("one"))
        {
            await ExecuteAsync(one.Connection, "CREATE TABLE t (id INTEGER)");
        }

        await using var two = await scope.AcquireAsync("two");
        var act = () => CountAsync(two.Connection, "t");
        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task A_file_database_is_shared_between_leases_and_a_cancelled_token_stops_the_open()
    {
        var path = Path.Combine(Path.GetTempPath(), $"naravel-db-{Guid.NewGuid():N}.db");
        try
        {
            using (var provider = Build(Config(("Default", "f"), ("Connections:f:Driver", "sqlite"), ("Connections:f:Path", path))))
            {
                var scope = provider.GetRequiredService<IConnectionScope>();
                await using (var write = await scope.AcquireAsync(null))
                {
                    await ExecuteAsync(write.Connection, "CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (7)");
                }

                await using (var read = await scope.AcquireAsync(null))
                {
                    (await CountAsync(read.Connection, "t")).Should().Be(1);
                }

                using var cts = new CancellationTokenSource();
                cts.Cancel();
                var act = async () => await scope.AcquireAsync(null, cts.Token);
                await act.Should().ThrowAsync<OperationCanceledException>();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Disposing_the_driver_stops_new_connections()
    {
        var connection = new SqliteDatabaseConnection("x", "Data Source=:memory:");
        await connection.DisposeAsync();

        var act = async () => await connection.OpenConnectionAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
