using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R23.T04: delete and truncate SQL on every grammar. Pure strings.</summary>
public class QueryDeleteTests
{
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static void Verify(string name, Func<Query, CompiledQuery> compile, string sql, params object?[] bindings)
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = compile(Query.For(dialect).Table("users"));

            compiled.Sql.Should().Be(Quoted(sql, dialect), $"case '{name}' on {dialect}");
            compiled.Bindings.Should().Equal(bindings, $"case '{name}' bindings on {dialect}");
        }
    }

    [Fact]
    public void Delete_writes_from_and_where_with_bindings_in_text_order()
    {
        Verify("where", q => q.Where("id", 7).CompileDelete(), "delete from «users» where «id» = @p0", 7);
        Verify("where groups", q => q.Where("a", 1).OrWhere("b", 2).CompileDelete(), "delete from «users» where «a» = @p0 or «b» = @p1", 1, 2);
        Verify("all rows when confirmed", q => q.AllRows().CompileDelete(), "delete from «users»");
    }

    [Fact]
    public void A_delete_without_a_where_throws_unless_every_row_is_confirmed()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            FluentActions.Invoking(() => Query.For(dialect).Table("users").CompileDelete()).Should().Throw<InvalidOperationException>().WithMessage("*no where*");
        }
    }

    [Fact]
    public void A_delete_query_may_only_have_a_table_joins_and_where()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");
        FluentActions.Invoking(() => query.Where("a", 1).Select("a").CompileDelete()).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
        FluentActions.Invoking(() => query.Where("a", 1).OrderBy("a").CompileDelete()).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
        FluentActions.Invoking(() => query.Where("a", 1).Limit(1).CompileDelete()).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
    }

    [Fact]
    public void A_table_alias_is_kept_and_sql_server_deletes_the_alias()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = Query.For(dialect).Table("users as u").Where("id", 1).CompileDelete();
            var expected = dialect == DatabaseDialect.SqlServer
                ? "delete «u» from «users» as «u» where «id» = @p0"
                : "delete from «users» as «u» where «id» = @p0";

            compiled.Sql.Should().Be(Quoted(expected, dialect), dialect.ToString());
        }
    }

    [Fact]
    public void Joins_in_a_delete_follow_each_dialects_syntax()
    {
        Query Joined(DatabaseDialect d) => Query.For(d).Table("users").Join("o", "o.uid", "users.id").Where("o.x", 1);

        Joined(DatabaseDialect.MySql).CompileDelete().Sql.Should().Be("delete `users` from `users` inner join `o` on `o`.`uid` = `users`.`id` where `o`.`x` = @p0");
        Joined(DatabaseDialect.SqlServer).CompileDelete().Sql.Should().Be("delete [users] from [users] inner join [o] on [o].[uid] = [users].[id] where [o].[x] = @p0");
        Joined(DatabaseDialect.Postgres).CompileDelete().Sql.Should().Be("delete from \"users\" using \"o\" where (\"o\".\"uid\" = \"users\".\"id\") and (\"o\".\"x\" = @p0)");
        Joined(DatabaseDialect.Sqlite).CompileDelete().Sql.Should().Be("delete from \"users\" where \"rowid\" in (select \"users\".\"rowid\" from \"users\" inner join \"o\" on \"o\".\"uid\" = \"users\".\"id\" where \"o\".\"x\" = @p0)");

        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            Joined(dialect).CompileDelete().Bindings.Should().Equal(1);
        }
    }

    [Fact]
    public void PostgreSQL_refuses_a_left_join_in_a_delete()
    {
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Postgres).Table("users").LeftJoin("o", "o.uid", "users.id").Where("o.x", 1).CompileDelete())
            .Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Truncate_is_one_statement_per_dialect_and_sqlite_also_resets_its_sequence()
    {
        Query.For(DatabaseDialect.MySql).Table("users").CompileTruncate(false).Select(c => c.Sql).Should().Equal("truncate table `users`");
        Query.For(DatabaseDialect.SqlServer).Table("users").CompileTruncate(false).Select(c => c.Sql).Should().Equal("truncate table [users]");
        Query.For(DatabaseDialect.Postgres).Table("users").CompileTruncate(false).Select(c => c.Sql).Should().Equal("truncate table \"users\" restart identity");
        Query.For(DatabaseDialect.Sqlite).Table("users").CompileTruncate(false).Select(c => c.Sql).Should().Equal("delete from \"users\"");

        var sqlite = Query.For(DatabaseDialect.Sqlite).Table("users").CompileTruncate(true);
        sqlite.Select(c => c.Sql).Should().Equal("delete from \"users\"", "delete from \"sqlite_sequence\" where \"name\" = @p0");
        sqlite[1].Bindings.Should().Equal("users");
    }

    [Fact]
    public void Truncate_takes_a_bare_table_only()
    {
        var query = Query.For(DatabaseDialect.Sqlite);
        FluentActions.Invoking(() => query.Table("users").Where("a", 1).CompileTruncate(false)).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
        FluentActions.Invoking(() => query.Table("users as u").CompileTruncate(false)).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
        FluentActions.Invoking(() => query.Table("users").Limit(1).CompileTruncate(false)).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
    }
}

/// <summary>R23.T04: deletes run on in-memory SQLite.</summary>
public sealed class QueryDeleteExecutionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table users (id integer primary key autoincrement, name text not null, votes integer not null)");
        await _db.UnpreparedAsync("create table orders (id integer primary key, uid integer not null, paid integer not null)");
        await _db.UnpreparedAsync("insert into users (name, votes) values ('a', 1), ('b', 2), ('c', 3)");
        await _db.UnpreparedAsync("insert into orders (id, uid, paid) values (1, 1, 0), (2, 2, 1), (3, 3, 0)");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Delete_removes_matching_rows_and_returns_the_count()
    {
        (await _db.Table("users").Where("votes", ">", 1).DeleteAsync()).Should().Be(2);
        (await _db.Table("users").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delete_without_a_where_throws_and_removes_nothing()
    {
        await FluentActions.Invoking(() => _db.Table("users").DeleteAsync()).Should().ThrowAsync<InvalidOperationException>();
        (await _db.Table("users").CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task DeleteById_and_DeleteAll()
    {
        (await _db.Table("users").DeleteByIdAsync(2)).Should().Be(1);
        (await _db.Table("users").DeleteByIdAsync(2)).Should().Be(0);
        (await _db.Table("users").DeleteAllAsync()).Should().Be(2);
        (await _db.Table("users").CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_joined_delete_uses_rowid_on_sqlite()
    {
        var removed = await _db.Table("users").Join("orders", "orders.uid", "users.id").Where("orders.paid", 0).DeleteAsync();

        removed.Should().Be(2);
        (await _db.Table("users").PluckAsync<string>("name")).Should().Equal("b");
        (await _db.Table("orders").CountAsync()).Should().Be(3, "only the target table loses rows");
    }

    [Fact]
    public async Task Truncate_empties_the_table_and_restarts_the_autoincrement_counter()
    {
        await _db.Table("users").TruncateAsync();
        (await _db.Table("users").CountAsync()).Should().Be(0);

        var id = await _db.Table("users").InsertGetIdAsync<long>(new Dictionary<string, object?> { ["name"] = "n", ["votes"] = 1 });
        id.Should().Be(1);
    }

    [Fact]
    public async Task Truncate_works_on_a_database_that_has_no_sequence_table()
    {
        await _db.Table("orders").TruncateAsync();
        (await _db.Table("orders").CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Truncate_refuses_a_where()
    {
        await FluentActions.Invoking(() => _db.Table("users").Where("id", 1).TruncateAsync()).Should().ThrowAsync<InvalidOperationException>();
        (await _db.Table("users").CountAsync()).Should().Be(3);
    }
}
