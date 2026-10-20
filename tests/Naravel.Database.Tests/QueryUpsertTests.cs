using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R23.T03: upsert SQL on every grammar, plus SQLite execution.</summary>
public class QueryUpsertTests
{
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    private static IReadOnlyList<CompiledQuery> Compile(DatabaseDialect dialect, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string[] uniqueBy, string[]? update = null) =>
        Query.For(dialect).Table("users").CompileUpsert(rows, uniqueBy, update);

    private static readonly IReadOnlyDictionary<string, object?>[] TwoRows =
    [
        Row(("id", 1), ("a", "x"), ("b", 2)),
        Row(("id", 3), ("a", "y"), ("b", 4)),
    ];

    [Fact]
    public void Sqlite_and_postgres_write_on_conflict_do_update_from_excluded()
    {
        foreach (var dialect in new[] { DatabaseDialect.Sqlite, DatabaseDialect.Postgres })
        {
            var statements = Compile(dialect, TwoRows, ["id"]);

            statements.Should().ContainSingle();
            statements[0].Sql.Should().Be("insert into \"users\" (\"id\", \"a\", \"b\") values (@p0, @p1, @p2), (@p3, @p4, @p5) on conflict (\"id\") do update set \"id\" = excluded.\"id\", \"a\" = excluded.\"a\", \"b\" = excluded.\"b\"", dialect.ToString());
            statements[0].Bindings.Should().Equal(1, "x", 2, 3, "y", 4);
        }
    }

    [Fact]
    public void The_update_list_limits_what_a_collision_overwrites_and_several_keys_are_listed()
    {
        var sql = Compile(DatabaseDialect.Postgres, TwoRows, ["id", "a"], ["b"])[0].Sql;

        sql.Should().EndWith("on conflict (\"id\", \"a\") do update set \"b\" = excluded.\"b\"");
    }

    [Fact]
    public void Mysql_uses_a_row_alias_with_on_duplicate_key_update_and_ignores_the_key_list()
    {
        var statements = Compile(DatabaseDialect.MySql, TwoRows, ["id"], ["a", "b"]);

        statements.Should().ContainSingle();
        statements[0].Sql.Should().Be("insert into `users` (`id`, `a`, `b`) values (@p0, @p1, @p2), (@p3, @p4, @p5) as `naravel_upsert` on duplicate key update `a` = `naravel_upsert`.`a`, `b` = `naravel_upsert`.`b`");
    }

    [Fact]
    public void Sql_server_uses_a_merge_with_holdlock_and_a_terminator()
    {
        var statements = Compile(DatabaseDialect.SqlServer, TwoRows, ["id"], ["a"]);

        statements.Should().ContainSingle();
        statements[0].Sql.Should().Be(
            "merge [users] with (holdlock) as [naravel_target] using (values (@p0, @p1, @p2), (@p3, @p4, @p5)) as [naravel_source] ([id], [a], [b]) on [naravel_target].[id] = [naravel_source].[id] " +
            "when matched then update set [naravel_target].[a] = [naravel_source].[a] when not matched then insert ([id], [a], [b]) values ([naravel_source].[id], [naravel_source].[a], [naravel_source].[b]);");
        statements[0].Bindings.Should().Equal(1, "x", 2, 3, "y", 4);
    }

    [Fact]
    public void Sql_server_joins_on_every_key_column()
    {
        Compile(DatabaseDialect.SqlServer, TwoRows, ["id", "a"], ["b"])[0].Sql
            .Should().Contain("on [naravel_target].[id] = [naravel_source].[id] and [naravel_target].[a] = [naravel_source].[a] when matched");
    }

    [Fact]
    public void An_empty_update_list_is_a_plain_insert_and_no_rows_run_nothing()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            Compile(dialect, TwoRows, ["id"], [])[0].Sql.Should().StartWith(dialect == DatabaseDialect.SqlServer ? "insert into [users]" : dialect == DatabaseDialect.MySql ? "insert into `users`" : "insert into \"users\"").And.NotContain("conflict").And.NotContain("duplicate");
            Compile(dialect, [], ["id"]).Should().BeEmpty(dialect.ToString());
        }
    }

    [Fact]
    public void A_batch_is_split_at_the_dialect_limits_with_the_same_clause_on_every_statement()
    {
        var rows = Enumerable.Range(0, 1200).Select(i => (IReadOnlyDictionary<string, object?>)Row(("id", i), ("a", "x"), ("b", i))).ToArray();

        var server = Compile(DatabaseDialect.SqlServer, rows, ["id"]);
        server.Select(s => s.Bindings.Count).Should().Equal(699 * 3, 501 * 3);
        server.Should().OnlyContain(s => s.Sql.StartsWith("merge [users]", StringComparison.Ordinal) && s.Sql.EndsWith(");", StringComparison.Ordinal));

        Compile(DatabaseDialect.Postgres, rows, ["id"]).Should().ContainSingle();
        Compile(DatabaseDialect.MySql, rows, ["id"]).Should().ContainSingle();
        Compile(DatabaseDialect.Sqlite, rows, ["id"]).Should().ContainSingle();
    }

    [Fact]
    public void Bad_input_fails_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        FluentActions.Invoking(() => query.CompileUpsert(TwoRows, [], null)).Should().Throw<ArgumentException>().WithMessage("*identify a row*");
        FluentActions.Invoking(() => query.CompileUpsert(TwoRows, ["missing"], null)).Should().Throw<ArgumentException>().WithMessage("*not one of the row columns*");
        FluentActions.Invoking(() => query.CompileUpsert(TwoRows, ["id"], ["missing"])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileUpsert(TwoRows, ["users.id"], null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileUpsert([Row(("id", 1)), Row(("a", 1))], ["id"], null)).Should().Throw<ArgumentException>().WithMessage("*exactly the columns*");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").Where("id", 1).CompileUpsert(TwoRows, ["id"], null)).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.SqlServer).Table("users as u").CompileUpsert(TwoRows, ["id"], null)).Should().Throw<InvalidOperationException>().WithMessage("*alias*");
    }

    [Fact]
    public async Task Unconnected_upserts_fail_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        await FluentActions.Awaiting(() => query.UpsertAsync(Row(("id", 1)), ["id"])).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => query.UpsertAsync((IReadOnlyDictionary<string, object?>)null!, ["id"])).Should().ThrowAsync<ArgumentNullException>();
    }
}

/// <summary>R23.T03: upserts run on in-memory SQLite.</summary>
public sealed class QueryUpsertExecutionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table users (id integer primary key, email text not null unique, name text not null, votes integer not null)");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    private static IReadOnlyDictionary<string, object?> User(string email, string name, int votes) => Row(("email", email), ("name", name), ("votes", votes));

    [Fact]
    public async Task Upsert_inserts_new_rows_updates_colliding_ones_and_is_idempotent()
    {
        var rows = new[] { User("a@x", "A", 1), User("b@x", "B", 2) };
        await _db.Table("users").UpsertAsync(rows, ["email"]);
        await _db.Table("users").UpsertAsync(rows, ["email"]);

        (await _db.Table("users").CountAsync()).Should().Be(2, "re-running the same upsert changes nothing");

        await _db.Table("users").UpsertAsync([User("b@x", "B2", 20), User("c@x", "C", 3)], ["email"]);

        (await _db.Table("users").OrderBy("email").PluckAsync<string>("name")).Should().Equal("A", "B2", "C");
        (await _db.Table("users").Where("email", "b@x").ValueAsync<long>("votes")).Should().Be(20);
    }

    [Fact]
    public async Task The_update_list_decides_which_columns_a_collision_overwrites()
    {
        await _db.Table("users").UpsertAsync(User("a@x", "A", 1), ["email"]);
        await _db.Table("users").UpsertAsync(User("a@x", "changed", 9), ["email"], ["votes"]);

        (await _db.Table("users").Where("email", "a@x").ValueAsync<string>("name")).Should().Be("A");
        (await _db.Table("users").Where("email", "a@x").ValueAsync<long>("votes")).Should().Be(9);
    }

    [Fact]
    public async Task An_empty_update_list_inserts_and_so_a_collision_fails()
    {
        await _db.Table("users").UpsertAsync(User("a@x", "A", 1), ["email"], []);

        await FluentActions.Awaiting(() => _db.Table("users").UpsertAsync(User("a@x", "A", 1), ["email"], [])).Should().ThrowAsync<System.Data.Common.DbException>();
    }

    [Fact]
    public async Task A_large_batch_is_one_atomic_unit()
    {
        var rows = Enumerable.Range(1, 3000).Select(i => User($"u{i}@x", "n", i)).ToArray();
        await _db.Table("users").UpsertAsync(rows, ["email"]);
        await _db.Table("users").UpsertAsync(rows, ["email"]);

        (await _db.Table("users").CountAsync()).Should().Be(3000);
    }
}
