using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Benchmarks;

/// <summary>
/// PDR-022 R23.T06: what a batch saves against a loop. Every variant writes the same 1 000 rows into an empty in-memory SQLite table.
/// <c>RowByRowInserts</c> (baseline) runs one <c>InsertAsync</c> per row, so it pays a command and a commit each; <c>BatchInsert</c> is one statement in one transaction;
/// <c>BatchUpsert</c> adds <c>on conflict do update</c> to the batch and should stay close to it.
/// </summary>
[MemoryDiagnoser]
public class WritesBenchmarks
{
    private const int RowCount = 1_000;

    private ServiceProvider _provider = null!;
    private IDatabase _db = null!;
    private IReadOnlyDictionary<string, object?>[] _rows = null!;

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

        await _db.StatementAsync("create table items (id integer primary key, name text not null unique, votes integer not null)");
        _rows = Enumerable.Range(0, RowCount)
            .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["name"] = "item" + i, ["votes"] = i })
            .ToArray();
    }

    [IterationSetup]
    public void EmptyTable() => _db.StatementAsync("delete from items").GetAwaiter().GetResult();

    [Benchmark(Baseline = true)]
    public async Task<int> RowByRowInserts()
    {
        var total = 0;
        foreach (var row in _rows) total += await _db.Table("items").InsertAsync(row);
        return total;
    }

    [Benchmark]
    public Task<int> BatchInsert() => _db.Table("items").InsertAsync(_rows);

    [Benchmark]
    public Task<int> BatchUpsert() => _db.Table("items").UpsertAsync(_rows, ["name"]);

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();
}
