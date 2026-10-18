using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Benchmarks;

public sealed class BenchmarkUser
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public long Age { get; set; }
}

/// <summary>
/// PDR-022 S2: what the query builder costs on top of hand-written SQL. Compile benchmarks isolate the string generation (no I/O);
/// execution benchmarks run the same query on in-memory SQLite through the builder, through hand-written Dapper and through raw ADO.NET,
/// each opening one connection per call so the comparison is fair.
/// </summary>
[MemoryDiagnoser]
public class DatabaseBenchmarks
{
    private const string HandWrittenSql = "select \"id\", \"name\", \"age\" from \"users\" where \"age\" >= @p0 order by \"id\" asc limit 10";

    private ServiceProvider _provider = null!;
    private IDatabase _db = null!;
    private IDatabaseConnection _connection = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Default"] = "main",
            ["Database:Connections:main:Driver"] = "sqlite",
            ["Database:Connections:main:Path"] = ":memory:",
        });
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<IDatabase>();
        _connection = _provider.GetRequiredService<DatabaseManager>().Connection();

        await _db.StatementAsync("create table users (id integer primary key, name text, age integer)");
        for (var i = 1; i <= 100; i++) await _db.AffectingStatementAsync("insert into users (name, age) values (?, ?)", ["user" + i, 10 + i % 50]);
    }

    [Benchmark]
    public CompiledQuery CompileBuilder() =>
        Query.For(DatabaseDialect.Sqlite).Table("users").Select("id", "name", "age").Where("age", ">=", 18).OrderBy("id").Limit(10).Compile();

    [Benchmark(Baseline = true)]
    public string CompileFloor() => HandWrittenSql;

    [Benchmark]
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BuilderRows() =>
        _db.Table("users").Select("id", "name", "age").Where("age", ">=", 18).OrderBy("id").Limit(10).GetAsync();

    [Benchmark]
    public Task<IReadOnlyList<BenchmarkUser>> BuilderTyped() =>
        _db.Table("users").Select("id", "name", "age").Where("age", ">=", 18).OrderBy("id").Limit(10).GetAsync<BenchmarkUser>();

    [Benchmark]
    public async Task<List<BenchmarkUser>> HandWrittenDapper()
    {
        await using var connection = await _connection.OpenConnectionAsync();
        return (await connection.QueryAsync<BenchmarkUser>(new CommandDefinition(HandWrittenSql, new { p0 = 18 }))).AsList();
    }

    [Benchmark]
    public async Task<List<(long Id, string? Name, long Age)>> HandWrittenAdo()
    {
        await using var connection = await _connection.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = HandWrittenSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@p0";
        parameter.Value = 18L;
        command.Parameters.Add(parameter);
        var rows = new List<(long, string?, long)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2)));
        return rows;
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();
}
