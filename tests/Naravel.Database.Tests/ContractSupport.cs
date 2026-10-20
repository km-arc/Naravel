using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>A theory that is skipped (reported as NOT RUN) unless the environment variable holds a connection string.</summary>
public sealed class DialectTheoryAttribute : TheoryAttribute
{
    public DialectTheoryAttribute(string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
        {
            Skip = $"Set {environmentVariable} to run this database contract test (NOT RUN).";
        }
    }
}

/// <summary>What a contract scenario works on: the database, its dialect and the run-unique tables.</summary>
internal sealed record ContractContext(IDatabase Db, DatabaseDialect Dialect, string People, string Types, string Docs, string Writes);

/// <summary>
/// One database per dialect for the contract suite: a run-unique table prefix, the schema created once and a small fixed data set
/// seeded once (PDR-022 A6). Without its connection string a server fixture does nothing and its tests are skipped.
/// </summary>
public abstract class ContractFixture : IAsyncLifetime
{
    private ServiceProvider? _provider;

    internal ContractContext? Context { get; private set; }

    protected abstract DatabaseDialect Dialect { get; }

    protected abstract string? ConnectionString { get; }

    protected abstract void Register(IServiceCollection services, IConfiguration configuration);

    protected abstract string DriverName { get; }

    public async Task InitializeAsync()
    {
        var connection = ConnectionString;
        if (connection is null) return;

        var settings = new Dictionary<string, string?>
        {
            ["Database:Default"] = "main",
            ["Database:Connections:main:Driver"] = DriverName
        };
        if (Dialect == DatabaseDialect.Sqlite) settings["Database:Connections:main:Path"] = ":memory:";
        else settings["Database:Connections:main:ConnectionString"] = connection;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        Register(services, configuration);
        _provider = services.BuildServiceProvider();

        var prefix = "nc_" + Guid.NewGuid().ToString("N")[..8];
        var context = new ContractContext(_provider.GetRequiredService<IDatabase>(), Dialect, prefix + "_people", prefix + "_types", prefix + "_docs", prefix + "_writes");
        foreach (var statement in ContractSchema.Create(context)) await context.Db.UnpreparedAsync(statement);
        await ContractSchema.SeedAsync(context);
        Context = context;
    }

    public async Task DisposeAsync()
    {
        if (Context is not null)
        {
            // Best effort: the CI containers are throwaway, but a developer's local server should be left clean.
            foreach (var table in new[] { Context.People, Context.Types, Context.Docs, Context.Writes })
            {
                try { await Context.Db.UnpreparedAsync($"drop table {table}"); }
                catch (Exception) { /* ignore */ }
            }
        }

        if (_provider is not null) await _provider.DisposeAsync();
    }
}

internal static class ContractSchema
{
    public static IEnumerable<string> Create(ContractContext context)
    {
        var (boolType, decimalType, guidType, dateTimeType, offsetType, dateType, timeType, bytesType, doubleType, stringType) = context.Dialect switch
        {
            DatabaseDialect.MySql => ("boolean", "decimal(18,4)", "char(36)", "datetime(6)", "datetime(6)", "date", "time(6)", "varbinary(255)", "double", "varchar(100)"),
            DatabaseDialect.Postgres => ("boolean", "numeric(18,4)", "uuid", "timestamp", "timestamptz", "date", "time", "bytea", "double precision", "varchar(100)"),
            DatabaseDialect.SqlServer => ("bit", "decimal(18,4)", "uniqueidentifier", "datetime2", "datetimeoffset", "date", "time", "varbinary(255)", "float", "nvarchar(100)"),
            _ => ("integer", "numeric", "text", "text", "text", "text", "text", "blob", "real", "text")
        };

        yield return $"create table {context.People} (id integer not null primary key, name {stringType} not null, votes integer not null, active {boolType} not null, note {stringType} null, born {dateTimeType} not null)";
        yield return $"create table {context.Types} (id integer not null primary key, c_bool {boolType} null, c_decimal {decimalType} null, c_guid {guidType} null, c_datetime {dateTimeType} null, " +
                     $"c_offset {offsetType} null, c_date {dateType} null, c_time {timeType} null, c_bytes {bytesType} null, c_long bigint null, c_double {doubleType} null, c_string {stringType} null, c_enum integer null)";
        var jsonType = context.Dialect switch { DatabaseDialect.MySql => "json", DatabaseDialect.Postgres => "jsonb", DatabaseDialect.SqlServer => "nvarchar(max)", _ => "text" };
        var bodyType = context.Dialect == DatabaseDialect.SqlServer ? "nvarchar(max)" : context.Dialect == DatabaseDialect.Sqlite ? "text" : "text";
        yield return $"create table {context.Docs} (id integer not null primary key, name {stringType} not null, body {bodyType} not null, payload {jsonType} not null)";
        if (context.Dialect == DatabaseDialect.MySql) yield return $"alter table {context.Docs} add fulltext index ft_body (body)";
        var idColumn = context.Dialect switch
        {
            DatabaseDialect.MySql => "bigint not null auto_increment primary key",
            DatabaseDialect.Postgres => "bigint generated by default as identity primary key",
            DatabaseDialect.SqlServer => "bigint identity(1,1) not null primary key",
            _ => "integer primary key",
        };
        yield return $"create table {context.Writes} (id {idColumn}, name {stringType} not null unique, votes integer not null)";
    }

    public static async Task SeedAsync(ContractContext context)
    {
        (int Id, string Name, int Votes, bool Active, string? Note, DateTime Born)[] people =
        [
            (1, "Ada", 10, true, "pioneer", new DateTime(2024, 1, 15, 8, 30, 0)),
            (2, "Grace", 20, true, null, new DateTime(2024, 2, 20, 12, 0, 0)),
            (3, "Linus", 20, false, "kernel", new DateTime(2023, 1, 15, 23, 59, 59)),
            (4, "Margaret", 30, true, null, new DateTime(2024, 2, 29, 0, 0, 0)),
            (5, "Dennis", 5, false, "c", new DateTime(2022, 12, 31, 17, 45, 30))
        ];
        foreach (var person in people)
        {
            await context.Db.StatementAsync($"insert into {context.People} (id, name, votes, active, note, born) values (?, ?, ?, ?, ?, ?)",
                [person.Id, person.Name, person.Votes, person.Active, person.Note, person.Born]);
        }

        // Literals, not bindings: PostgreSQL will not store a text parameter into a jsonb column without a cast.
        string[] docs =
        [
            $"insert into {context.Docs} (id, name, body, payload) values (1, 'Ada', 'kernel hackers love unix', '{{\"name\":\"Ada\",\"age\":36,\"tags\":[\"php\",\"go\"],\"active\":true}}')",
            $"insert into {context.Docs} (id, name, body, payload) values (2, 'Grace', 'compilers and cobol', '{{\"name\":\"Grace\",\"age\":45,\"tags\":[\"cobol\"],\"active\":false}}')",
            $"insert into {context.Docs} (id, name, body, payload) values (3, 'Linus', 'the linux kernel', '{{\"name\":\"Linus\",\"age\":55,\"tags\":[\"c\",\"git\",\"kernel\"],\"active\":true,\"nick\":\"torvalds\"}}')",
        ];
        foreach (var statement in docs) await context.Db.UnpreparedAsync(statement);
    }
}

public sealed class SqliteContractFixture : ContractFixture
{
    protected override DatabaseDialect Dialect => DatabaseDialect.Sqlite;
    protected override string? ConnectionString => ":memory:";
    protected override string DriverName => "sqlite";
    protected override void Register(IServiceCollection services, IConfiguration configuration) => services.AddNaravelSqliteDatabase(configuration);
}

public sealed class MySqlContractFixture : ContractFixture
{
    protected override DatabaseDialect Dialect => DatabaseDialect.MySql;
    protected override string? ConnectionString => Environment.GetEnvironmentVariable("NARAVEL_TEST_MYSQL") is { Length: > 0 } value ? value : null;
    protected override string DriverName => "mysql";
    protected override void Register(IServiceCollection services, IConfiguration configuration) => services.AddNaravelMySqlDatabase(configuration);
}

public sealed class PostgresContractFixture : ContractFixture
{
    protected override DatabaseDialect Dialect => DatabaseDialect.Postgres;
    protected override string? ConnectionString => Environment.GetEnvironmentVariable("NARAVEL_TEST_POSTGRES") is { Length: > 0 } value ? value : null;
    protected override string DriverName => "pgsql";
    protected override void Register(IServiceCollection services, IConfiguration configuration) => services.AddNaravelPostgresDatabase(configuration);
}

public sealed class SqlServerContractFixture : ContractFixture
{
    protected override DatabaseDialect Dialect => DatabaseDialect.SqlServer;
    protected override string? ConnectionString => Environment.GetEnvironmentVariable("NARAVEL_TEST_SQLSERVER") is { Length: > 0 } value ? value : null;
    protected override string DriverName => "sqlserver";
    protected override void Register(IServiceCollection services, IConfiguration configuration) => services.AddNaravelSqlServerDatabase(configuration);
}
