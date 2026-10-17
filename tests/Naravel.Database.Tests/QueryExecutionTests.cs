using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

public sealed class UserRow
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Votes { get; set; }
}

public sealed class QueryExecutionTests : IAsyncLifetime
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
        await _db.StatementAsync("insert into users (id, name, votes) values (?, ?, ?), (?, ?, ?), (?, ?, ?)",
            [1, "Ada", 10, 2, "Grace", 20, 3, "Linus", 20]);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task GetAsync_returns_every_row_as_a_dictionary_with_case_insensitive_keys()
    {
        var rows = await _db.Table("users").OrderBy("id").GetAsync();

        rows.Should().HaveCount(3);
        rows[0]["name"].Should().Be("Ada");
        rows[0]["NAME"].Should().Be("Ada");
        rows[2]["votes"].Should().Be(20L);
    }

    [Fact]
    public async Task GetAsync_of_T_maps_rows_with_dapper()
    {
        var users = await _db.Table("users").Where("votes", 20).OrderBy("id").GetAsync<UserRow>();

        users.Select(u => u.Name).Should().Equal("Grace", "Linus");
    }

    [Fact]
    public async Task FirstAsync_returns_the_first_row_or_null()
    {
        var first = await _db.Table("users").OrderBy("votes").OrderBy("id").FirstAsync();
        var none = await _db.Table("users").Where("name", "nobody").FirstAsync();
        var typed = await _db.Table("users").Where("name", "Grace").FirstAsync<UserRow>();
        var typedNone = await _db.Table("users").Where("name", "nobody").FirstAsync<UserRow>();

        first!["name"].Should().Be("Ada");
        none.Should().BeNull();
        typed.Id.Should().Be(2);
        typedNone.Should().BeNull();
    }

    [Fact]
    public async Task SoleAsync_returns_exactly_one_row_and_throws_otherwise()
    {
        var sole = await _db.Table("users").Where("name", "Ada").SoleAsync();
        var typed = await _db.Table("users").Where("name", "Ada").SoleAsync<UserRow>();

        sole["id"].Should().Be(1L);
        typed.Name.Should().Be("Ada");
        await _db.Table("users").Where("name", "nobody").Awaiting(q => q.SoleAsync()).Should().ThrowAsync<RecordsNotFoundException>();
        await _db.Table("users").Where("votes", 20).Awaiting(q => q.SoleAsync()).Should().ThrowAsync<MultipleRecordsFoundException>();
        await _db.Table("users").Where("votes", 20).Awaiting(q => q.SoleAsync<UserRow>()).Should().ThrowAsync<MultipleRecordsFoundException>();
    }

    [Fact]
    public async Task ValueAsync_returns_one_column_or_the_default()
    {
        var name = await _db.Table("users").Where("id", 2).ValueAsync<string>("name");
        var votes = await _db.Table("users").Where("id", 2).ValueAsync<long>("votes");
        var missing = await _db.Table("users").Where("id", 99).ValueAsync<string>("name");
        var missingNumber = await _db.Table("users").Where("id", 99).ValueAsync<long>("votes");

        name.Should().Be("Grace");
        votes.Should().Be(20);
        missing.Should().BeNull();
        missingNumber.Should().Be(0);
    }

    [Fact]
    public async Task FindAsync_looks_up_by_key_and_returns_null_when_missing()
    {
        var found = await _db.Table("users").FindAsync(3);
        var typed = await _db.Table("users").FindAsync<UserRow>(3);
        var byName = await _db.Table("users").FindAsync("Ada", "name");
        var missing = await _db.Table("users").FindAsync(99);

        found!["name"].Should().Be("Linus");
        typed.Name.Should().Be("Linus");
        byName!["id"].Should().Be(1L);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task PluckAsync_returns_one_column_of_every_row()
    {
        var names = await _db.Table("users").OrderByDesc("id").PluckAsync<string>("name");

        names.Should().Equal("Linus", "Grace", "Ada");
    }

    [Fact]
    public async Task A_hostile_value_is_data_not_sql()
    {
        var hostile = "x'; drop table users; --";

        var rows = await _db.Table("users").Where("name", hostile).GetAsync();
        var stillThere = await _db.Table("users").GetAsync();

        rows.Should().BeEmpty();
        stillThere.Should().HaveCount(3);
    }

    [Fact]
    public async Task Execution_methods_honour_a_cancelled_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await _db.Table("users").Awaiting(q => q.GetAsync(cts.Token)).Should().ThrowAsync<OperationCanceledException>();
        await _db.Awaiting(d => d.SelectAsync("select 1", null, cts.Token)).Should().ThrowAsync<OperationCanceledException>();
        await _db.Awaiting(d => d.UnpreparedAsync("select 1", cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_query_that_is_not_connected_explains_how_to_connect_it()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        await query.Awaiting(q => q.GetAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*IDatabase.Table*");
    }

    [Fact]
    public async Task A_cloned_query_stays_connected()
    {
        var clone = _db.Table("users").Where("votes", 20).Clone();

        (await clone.GetAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task SelectAsync_binds_question_marks_and_keeps_double_question_marks_literal()
    {
        var rows = await _db.SelectAsync("select name from users where votes = ? and name <> ? order by id", [20, "Linus"]);
        var literal = await _db.ScalarAsync<string>("select 'what??' as q");

        rows.Should().ContainSingle().Which["name"].Should().Be("Grace");
        literal.Should().Be("what?");
    }

    [Fact]
    public async Task SelectAsync_of_T_and_ScalarAsync_work_with_bindings()
    {
        var users = await _db.SelectAsync<UserRow>("select id, name, votes from users where id in (?, ?) order by id", [1, 3]);
        var count = await _db.ScalarAsync<long>("select count(*) from users where votes >= ?", [20]);
        var none = await _db.ScalarAsync<string>("select name from users where id = ?", [99]);

        users.Select(u => u.Name).Should().Equal("Ada", "Linus");
        count.Should().Be(2);
        none.Should().BeNull();
    }

    [Fact]
    public async Task A_wrong_number_of_bindings_is_rejected_before_anything_runs()
    {
        await _db.Awaiting(d => d.SelectAsync("select * from users where id = ?")).Should().ThrowAsync<ArgumentException>().WithMessage("*placeholder*");
        await _db.Awaiting(d => d.SelectAsync("select * from users", [1])).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AffectingStatementAsync_returns_the_number_of_rows_changed()
    {
        var updated = await _db.AffectingStatementAsync("update users set votes = votes + 1 where votes = ?", [20]);
        var deleted = await _db.AffectingStatementAsync("delete from users where id = ?", [99]);

        updated.Should().Be(2);
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task StatementAsync_throws_when_the_statement_fails()
    {
        await _db.Awaiting(d => d.StatementAsync("insert into missing_table values (1)")).Should().ThrowAsync<SqliteException>();
    }

    private enum Color
    {
        Red = 1,
        Blue = 2
    }

    [Fact]
    public async Task Typed_parameters_are_bound_with_an_explicit_db_type_and_stored()
    {
        await _db.UnpreparedAsync("create table kinds (b integer, d text, g text, dt text, dto text, dateonly text, timeonly text, bl blob, e integer, n text)");
        var bytes = new byte[] { 1, 2, 3 };

        await _db.StatementAsync(
            "insert into kinds (b, d, g, dt, dto, dateonly, timeonly, bl, e, n) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [true, 12.345m, Guid.NewGuid(), new DateTime(2026, 10, 17, 10, 46, 30, DateTimeKind.Utc), new DateTimeOffset(2026, 10, 17, 10, 46, 30, TimeSpan.Zero),
             new DateOnly(2026, 10, 17), new TimeOnly(10, 46, 30), bytes, Color.Blue, null]);

        var row = (await _db.Table("kinds").GetAsync()).Single();
        row["b"].Should().Be(1L);
        row["e"].Should().Be(2L);
        row["bl"].Should().BeEquivalentTo(bytes);
        row["n"].Should().BeNull();
        foreach (var column in new[] { "d", "g", "dt", "dto", "dateonly", "timeonly" }) row[column].Should().NotBeNull(column);
    }

    [Fact]
    public async Task Connection_returns_an_instance_bound_to_a_name()
    {
        var named = _db.Connection("main");

        named.ConnectionName.Should().Be("main");
        _db.ConnectionName.Should().BeNull();
        (await named.Table("users").GetAsync()).Should().HaveCount(3);
    }

    // --- PDR-022 A1: the executor works through a lease it may not own -----------------------------------------------------

    private sealed class BorrowedScope(DbConnection connection, DbTransaction transaction) : IConnectionScope
    {
        public int Acquired { get; private set; }

        public int Disposed { get; private set; }

        public ValueTask<IConnectionLease> AcquireAsync(string? connectionName, CancellationToken cancellationToken = default)
        {
            Acquired++;
            return ValueTask.FromResult<IConnectionLease>(new Lease(this, connection, transaction));
        }

        private sealed class Lease(BorrowedScope owner, DbConnection connection, DbTransaction transaction) : IConnectionLease
        {
            public DbConnection Connection => connection;

            public DbTransaction? Transaction => transaction;

            public bool OwnsConnection => false;

            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task The_executor_joins_the_lease_transaction_and_never_closes_a_borrowed_connection()
    {
        await using var borrowed = new SqliteConnection("Data Source=:memory:");
        await borrowed.OpenAsync();
        await using (var create = borrowed.CreateCommand())
        {
            create.CommandText = "create table notes (id integer primary key, body text not null)";
            await create.ExecuteNonQueryAsync();
        }

        await using var transaction = await borrowed.BeginTransactionAsync();
        var scope = new BorrowedScope(borrowed, transaction);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Default"] = "main",
            ["Database:Connections:main:Driver"] = "sqlite",
            ["Database:Connections:main:Path"] = ":memory:"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionScope>(scope);
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        await using var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<IDatabase>();

        // Microsoft.Data.Sqlite refuses to run a command on a connection with a pending transaction unless command.Transaction is set,
        // so a successful insert proves the executor assigned the lease transaction.
        await db.StatementAsync("insert into notes (id, body) values (?, ?)", [1, "inside the transaction"]);
        var visible = await db.Table("notes").GetAsync();

        visible.Should().HaveCount(1);
        borrowed.State.Should().Be(System.Data.ConnectionState.Open, "a connection the lease does not own is never closed");
        scope.Acquired.Should().Be(2);
        scope.Disposed.Should().Be(2, "every operation releases its lease");

        await transaction.RollbackAsync();
        await using var check = borrowed.CreateCommand();
        check.CommandText = "select count(*) from notes";
        Convert.ToInt64(await check.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture).Should().Be(0, "the insert joined the transaction, so the rollback removed it");
    }
}
