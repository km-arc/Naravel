using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R22.T02: chunking and each on in-memory SQLite. Ten users, ids 1 to 10.</summary>
public sealed class QueryChunkTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private IDatabase _db = null!;

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
        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<IDatabase>();

        await _db.UnpreparedAsync("create table users (id integer primary key, name text not null, votes integer not null)");
        for (var i = 1; i <= 10; i++) await _db.StatementAsync("insert into users (id, name, votes) values (?, ?, ?)", [i, "user" + i, i * 10]);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static long Id(IReadOnlyDictionary<string, object?> row) => Convert.ToInt64(row["id"]);

    [Fact]
    public async Task Chunk_reads_exact_pages_and_stops_after_a_short_page()
    {
        var sizes = new List<int>();
        var ids = new List<long>();
        await _db.Table("users").OrderBy("id").ChunkAsync(3, page =>
        {
            sizes.Add(page.Count);
            ids.AddRange(page.Select(Id));
            return Task.FromResult(true);
        });

        sizes.Should().Equal(3, 3, 3, 1);
        ids.Should().Equal(Enumerable.Range(1, 10).Select(i => (long)i));
    }

    [Fact]
    public async Task Chunk_does_not_call_back_with_an_empty_page_when_the_last_page_is_full()
    {
        var sizes = new List<int>();
        await _db.Table("users").OrderBy("id").ChunkAsync(5, page =>
        {
            sizes.Add(page.Count);
            return Task.FromResult(true);
        });

        sizes.Should().Equal(5, 5);
    }

    [Fact]
    public async Task Chunk_stops_when_the_callback_returns_false()
    {
        var calls = 0;
        await _db.Table("users").OrderBy("id").ChunkAsync(3, _ =>
        {
            calls++;
            return Task.FromResult(false);
        });

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Chunk_of_nothing_never_calls_back()
    {
        var calls = 0;
        await _db.Table("users").Where("votes", ">", 1000).OrderBy("id").ChunkAsync(3, _ =>
        {
            calls++;
            return Task.FromResult(true);
        });

        calls.Should().Be(0);
    }

    [Fact]
    public async Task Chunk_keeps_where_and_replaces_a_limit_and_offset_already_on_the_query()
    {
        var ids = new List<long>();
        await _db.Table("users").Where("votes", ">=", 50).OrderBy("id").Limit(1).Offset(2).ChunkAsync(2, page =>
        {
            ids.AddRange(page.Select(Id));
            return Task.FromResult(true);
        });

        ids.Should().Equal(5, 6, 7, 8, 9, 10);
    }

    [Fact]
    public async Task Chunk_needs_an_order_and_a_positive_size()
    {
        await FluentActions.Awaiting(() => _db.Table("users").ChunkAsync(3, _ => Task.FromResult(true))).Should().ThrowAsync<InvalidOperationException>().WithMessage("*OrderBy*");
        await FluentActions.Awaiting(() => _db.Table("users").OrderBy("id").ChunkAsync(0, _ => Task.FromResult(true))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => Query.For(DatabaseDialect.Sqlite).Table("users").OrderBy("id").ChunkAsync(3, _ => Task.FromResult(true))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Chunk_maps_rows_to_a_type()
    {
        var names = new List<string>();
        await _db.Table("users").OrderBy("id").ChunkAsync<UserRow>(4, page =>
        {
            names.AddRange(page.Select(u => u.Name));
            return Task.FromResult(true);
        });

        names.Should().HaveCount(10).And.StartWith("user1").And.EndWith("user10");
    }

    [Fact]
    public async Task ChunkById_follows_the_key_and_ignores_the_ordering_it_is_given()
    {
        var ids = new List<long>();
        await _db.Table("users").OrderByDesc("votes").ChunkByIdAsync(4, page =>
        {
            ids.AddRange(page.Select(Id));
            return Task.FromResult(true);
        });

        ids.Should().Equal(Enumerable.Range(1, 10).Select(i => (long)i));
    }

    [Fact]
    public async Task ChunkById_is_stable_when_rows_are_added_or_removed_during_the_loop()
    {
        var seen = new List<long>();
        var added = false;
        await _db.Table("users").ChunkByIdAsync(3, async page =>
        {
            seen.AddRange(page.Select(Id));
            await _db.StatementAsync("delete from users where id <= ?", [page.Max(Id)]);
            if (!added)
            {
                added = true;
                await _db.StatementAsync("insert into users (id, name, votes) values (11, 'late', 1)");
            }

            return true;
        });

        seen.Should().Equal(Enumerable.Range(1, 11).Select(i => (long)i), "deleting what was seen does not skip rows, and a row added after the cursor is still seen once");
    }

    [Fact]
    public async Task ChunkById_follows_a_named_column_and_maps_rows()
    {
        var ids = new List<long>();
        await _db.Table("users").ChunkByIdAsync<UserRow>(4, u => u.Id, page =>
        {
            ids.AddRange(page.Select(u => u.Id));
            return Task.FromResult(true);
        });

        ids.Should().HaveCount(10);

        var byVotes = new List<long>();
        await _db.Table("users").Select("id", "votes").ChunkByIdAsync(6, page =>
        {
            byVotes.AddRange(page.Select(r => Convert.ToInt64(r["votes"])));
            return Task.FromResult(true);
        }, "votes");
        byVotes.Should().Equal(10, 20, 30, 40, 50, 60, 70, 80, 90, 100);
    }

    [Fact]
    public async Task ChunkById_fails_clearly_when_the_key_is_not_selected_and_stops_on_false()
    {
        await FluentActions.Awaiting(() => _db.Table("users").Select("name").ChunkByIdAsync(3, _ => Task.FromResult(true)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*'id'*");

        var calls = 0;
        await _db.Table("users").ChunkByIdAsync(3, _ =>
        {
            calls++;
            return Task.FromResult(false);
        });
        calls.Should().Be(1);
    }

    [Fact]
    public async Task ChunkMap_collects_the_mapped_rows_in_order()
    {
        var names = await _db.Table("users").OrderBy("id").ChunkMapAsync(row => (string)row["name"]!, 4);

        names.Should().HaveCount(10);
        names[0].Should().Be("user1");
        names[9].Should().Be("user10");
    }

    [Fact]
    public async Task Each_and_EachById_visit_every_row_and_stop_early()
    {
        var all = new List<long>();
        await _db.Table("users").OrderBy("id").EachAsync(row =>
        {
            all.Add(Id(row));
            return Task.FromResult(true);
        }, 4);
        all.Should().HaveCount(10);

        var firstThree = new List<long>();
        await _db.Table("users").EachByIdAsync(row =>
        {
            firstThree.Add(Id(row));
            return Task.FromResult(firstThree.Count < 3);
        }, 2);
        firstThree.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_loop_before_any_command()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await FluentActions.Awaiting(() => _db.Table("users").OrderBy("id").ChunkAsync(3, _ => Task.FromResult(true), cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
