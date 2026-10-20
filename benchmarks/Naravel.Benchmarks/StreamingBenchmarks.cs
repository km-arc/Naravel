using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Benchmarks;

/// <summary>
/// PDR-022 R22.T04: what streaming costs and saves against loading everything. All variants read the same 50 000 rows from in-memory SQLite and
/// visit every row. <c>BufferedGet</c> (baseline) materialises the whole result; the others keep memory bounded: one open reader
/// (<c>CursorEnumerate</c>), <c>limit/offset</c> pages (<c>LazyPages</c>), or keyset pages (<c>LazyByIdPages</c>, <c>ChunkByIdPages</c>).
/// Read <c>Allocated</c> next to <c>Mean</c>: the point of streaming is the memory column, and keyset paging is expected to stay flat where offset paging slows down.
/// </summary>
[MemoryDiagnoser]
public class StreamingBenchmarks
{
    private const int RowCount = 50_000;

    private ServiceProvider _provider = null!;
    private IDatabase _db = null!;

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

        await _db.StatementAsync("create table events (id integer primary key, name text, age integer)");
        await _db.StatementAsync(
            "insert into events (name, age) with recursive c(x) as (select 1 union all select x + 1 from c where x < " + RowCount + ") select 'event' || x, x % 50 from c");
    }

    [Benchmark(Baseline = true)]
    public async Task<int> BufferedGet() => (await _db.Table("events").Select("id", "name", "age").OrderBy("id").GetAsync()).Count;

    [Benchmark]
    public async Task<int> CursorEnumerate()
    {
        var count = 0;
        await foreach (var row in _db.Table("events").Select("id", "name", "age").OrderBy("id").CursorAsync()) count += row.Count > 0 ? 1 : 0;
        return count;
    }

    [Benchmark]
    public async Task<int> LazyPages()
    {
        var count = 0;
        await foreach (var row in _db.Table("events").Select("id", "name", "age").OrderBy("id").LazyAsync(1000)) count += row.Count > 0 ? 1 : 0;
        return count;
    }

    [Benchmark]
    public async Task<int> LazyByIdPages()
    {
        var count = 0;
        await foreach (var row in _db.Table("events").Select("id", "name", "age").LazyByIdAsync(1000)) count += row.Count > 0 ? 1 : 0;
        return count;
    }

    [Benchmark]
    public async Task<int> ChunkByIdPages()
    {
        var count = 0;
        await _db.Table("events").Select("id", "name", "age").ChunkByIdAsync(1000, rows =>
        {
            count += rows.Count;
            return Task.FromResult(true);
        });
        return count;
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();
}
