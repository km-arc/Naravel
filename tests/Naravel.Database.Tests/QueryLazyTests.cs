using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R22.T03: lazy and cursor streaming on in-memory SQLite. Ten users, ids 1 to 10.</summary>
public sealed class QueryLazyTests : IAsyncLifetime
{
    private readonly RecordingListener _listener = new();
    private ServiceProvider _provider = null!;
    private IDatabase _db = null!;

    private sealed class RecordingListener : IQueryListener
    {
        public ConcurrentQueue<QueryExecuted> Seen { get; } = new();

        public void OnQueryExecuted(QueryExecuted query) => Seen.Enqueue(query);

        public int Selects => Seen.Count(q => q.Operation == QueryOperation.Select);
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Default"] = "main",
            ["Database:Connections:main:Driver"] = "sqlite",
            ["Database:Connections:main:Path"] = ":memory:"
        }).Build();
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        services.AddSingleton<IQueryListener>(_listener);
        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<IDatabase>();

        await _db.UnpreparedAsync("create table users (id integer primary key, name text not null, votes integer not null)");
        for (var i = 1; i <= 10; i++) await _db.StatementAsync("insert into users (id, name, votes) values (?, ?, ?)", [i, "user" + i, i * 10]);
        _listener.Seen.Clear();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static long Id(IReadOnlyDictionary<string, object?> row) => Convert.ToInt64(row["id"]);

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }

    [Fact]
    public async Task Lazy_streams_every_row_in_order_one_page_command_at_a_time()
    {
        var rows = await ToListAsync(_db.Table("users").OrderBy("id").LazyAsync(4));

        rows.Select(Id).Should().Equal(Enumerable.Range(1, 10).Select(i => (long)i));
        _listener.Selects.Should().Be(3, "pages of 4, 4 and 2 rows");
    }

    [Fact]
    public async Task Lazy_stopping_early_asks_for_no_further_page()
    {
        var taken = new List<long>();
        await foreach (var row in _db.Table("users").OrderBy("id").LazyAsync(4))
        {
            taken.Add(Id(row));
            if (taken.Count == 2) break;
        }

        taken.Should().Equal(1, 2);
        _listener.Selects.Should().Be(1);
    }

    [Fact]
    public async Task Lazy_maps_rows_and_keeps_the_where()
    {
        var users = await ToListAsync(_db.Table("users").Where("votes", ">", 60).OrderBy("id").LazyAsync<UserRow>(2));

        users.Select(u => u.Name).Should().Equal("user7", "user8", "user9", "user10");
    }

    [Fact]
    public async Task Lazy_checks_its_arguments_at_the_call()
    {
        FluentActions.Invoking(() => _db.Table("users").LazyAsync(3)).Should().Throw<InvalidOperationException>().WithMessage("*OrderBy*");
        FluentActions.Invoking(() => _db.Table("users").OrderBy("id").LazyAsync(0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").OrderBy("id").LazyAsync(3)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => _db.Table("users").LazyByIdAsync<UserRow>(null!)).Should().Throw<ArgumentNullException>();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LazyById_follows_the_key_and_is_stable_under_changes()
    {
        var seen = new List<long>();
        var added = false;
        await foreach (var row in _db.Table("users").OrderByDesc("votes").LazyByIdAsync(3))
        {
            seen.Add(Id(row));
            if (seen.Count % 3 != 0) continue;

            await _db.StatementAsync("delete from users where id <= ?", [seen[^1]]);
            if (added) continue;
            added = true;
            await _db.StatementAsync("insert into users (id, name, votes) values (11, 'late', 1)");
        }

        seen.Should().Equal(Enumerable.Range(1, 11).Select(i => (long)i));
    }

    [Fact]
    public async Task LazyByIdDesc_walks_from_the_highest_key_down()
    {
        var ids = (await ToListAsync(_db.Table("users").LazyByIdDescAsync(4))).Select(Id).ToList();

        ids.Should().Equal(Enumerable.Range(1, 10).Reverse().Select(i => (long)i));

        var typed = await ToListAsync(_db.Table("users").LazyByIdDescAsync<UserRow>(u => u.Id, 3));
        typed.Select(u => u.Id).Should().Equal(10, 9, 8, 7, 6, 5, 4, 3, 2, 1);
    }

    [Fact]
    public async Task LazyById_maps_rows_and_fails_clearly_when_the_key_is_not_selected()
    {
        var typed = await ToListAsync(_db.Table("users").LazyByIdAsync<UserRow>(u => u.Id, 4));
        typed.Should().HaveCount(10);

        await FluentActions.Awaiting(() => ToListAsync(_db.Table("users").Select("name").LazyByIdAsync(3)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*'id'*");
    }

    [Fact]
    public async Task Cursor_streams_every_row_with_one_command_reported_when_the_enumeration_ends()
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _db.Table("users").OrderBy("id").CursorAsync())
        {
            rows.Add(row);
            _listener.Selects.Should().Be(0, "the command is reported when the stream ends, not before");
        }

        rows.Select(Id).Should().Equal(Enumerable.Range(1, 10).Select(i => (long)i));
        _listener.Selects.Should().Be(1);
        _listener.Seen.Single().Exception.Should().BeNull();
    }

    [Fact]
    public async Task Cursor_maps_rows_and_reads_single_values()
    {
        var users = await ToListAsync(_db.Table("users").Where("votes", "<=", 30).OrderBy("id").CursorAsync<UserRow>());
        users.Select(u => u.Name).Should().Equal("user1", "user2", "user3");

        var names = await ToListAsync(_db.Table("users").Select("name").OrderBy("id").Limit(2).CursorAsync<string>());
        names.Should().Equal("user1", "user2");
    }

    [Fact]
    public async Task Cursor_fails_at_the_call_when_not_connected()
    {
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").CursorAsync()).Should().Throw<InvalidOperationException>();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Stopping_early_releases_the_reader_and_the_connection()
    {
        await foreach (var _ in _db.Table("users").OrderBy("id").CursorAsync()) break;

        // SQLite refuses to drop a table while a reader on it is still open, so this proves the reader is gone.
        await _db.UnpreparedAsync("drop table users");
    }

    [Fact]
    public async Task Cancelling_the_stream_releases_the_reader_and_reports_the_failure()
    {
        using var cancel = new CancellationTokenSource();
        var read = 0;

        await FluentActions.Awaiting(async () =>
        {
            await foreach (var _ in _db.Table("users").OrderBy("id").CursorAsync(cancel.Token))
            {
                if (++read == 2) await cancel.CancelAsync();
            }
        }).Should().ThrowAsync<OperationCanceledException>();

        read.Should().Be(2);
        await _db.UnpreparedAsync("drop table users");
        _listener.Seen.Single(q => q.Operation == QueryOperation.Select).Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task Cursor_over_a_large_table_does_not_buffer_the_rows()
    {
        await _db.UnpreparedAsync("create table big (id integer primary key, name text not null)");
        await _db.UnpreparedAsync("insert into big (id, name) with recursive seq(n) as (select 1 union all select n + 1 from seq where n < 200000) select n, 'row' || n from seq");

        var count = 0L;
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var peak = 0L;
        await foreach (var row in _db.Table("big").OrderBy("id").CursorAsync())
        {
            count++;
            if (count % 50_000 != 0) continue;

            peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: true) - baseline);
            row["name"].Should().NotBeNull();
        }

        count.Should().Be(200_000);
        peak.Should().BeLessThan(25 * 1024 * 1024, "buffering 200 000 dictionary rows would need far more than this");
    }
}
