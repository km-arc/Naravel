using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R23.T05: returning clauses per dialect. Pure strings.</summary>
public class QueryReturningTests
{
    private static IReadOnlyDictionary<string, object?> Row(string name, int votes) => new Dictionary<string, object?> { ["name"] = name, ["votes"] = votes };

    private static Query Users(DatabaseDialect dialect) => Query.For(dialect).Table("users");

    [Fact]
    public void Insert_returning_uses_returning_on_sqlite_and_postgres_and_output_on_sql_server()
    {
        var rows = new[] { Row("a", 1) };

        Users(DatabaseDialect.Sqlite).CompileInsertReturning(rows, ["id", "name"]).Single().Sql
            .Should().Be("insert into \"users\" (\"name\", \"votes\") values (@p0, @p1) returning \"id\", \"name\"");
        Users(DatabaseDialect.Postgres).CompileInsertReturning(rows, ["id"]).Single().Sql
            .Should().Be("insert into \"users\" (\"name\", \"votes\") values (@p0, @p1) returning \"id\"");
        Users(DatabaseDialect.SqlServer).CompileInsertReturning(rows, ["id", "name"]).Single().Sql
            .Should().Be("insert into [users] ([name], [votes]) output inserted.[id], inserted.[name] values (@p0, @p1)");
    }

    [Fact]
    public void Update_returning_puts_output_before_from_and_where_on_sql_server()
    {
        var values = new Dictionary<string, object?> { ["votes"] = 5 };

        Users(DatabaseDialect.Sqlite).Where("id", 1).CompileUpdateReturning(values, ["votes"]).Sql
            .Should().Be("update \"users\" set \"votes\" = @p0 where \"id\" = @p1 returning \"votes\"");
        Users(DatabaseDialect.Postgres).Where("id", 1).CompileUpdateReturning(values, ["votes"]).Sql
            .Should().Be("update \"users\" set \"votes\" = @p0 where \"id\" = @p1 returning \"votes\"");
        Users(DatabaseDialect.SqlServer).Where("id", 1).CompileUpdateReturning(values, ["votes"]).Sql
            .Should().Be("update [users] set [votes] = @p0 output inserted.[votes] where [id] = @p1");
        Users(DatabaseDialect.SqlServer).Join("o", "o.uid", "users.id").Where("o.x", 1).CompileUpdateReturning(values, ["votes"]).Sql
            .Should().Be("update [users] set [votes] = @p0 output inserted.[votes] from [users] inner join [o] on [o].[uid] = [users].[id] where [o].[x] = @p1");
    }

    [Fact]
    public void Delete_returning_reads_the_deleted_rows()
    {
        Users(DatabaseDialect.Sqlite).Where("id", 1).CompileDeleteReturning(["id"]).Sql.Should().Be("delete from \"users\" where \"id\" = @p0 returning \"id\"");
        Users(DatabaseDialect.Postgres).Where("id", 1).CompileDeleteReturning(["id"]).Sql.Should().Be("delete from \"users\" where \"id\" = @p0 returning \"id\"");
        Users(DatabaseDialect.SqlServer).Where("id", 1).CompileDeleteReturning(["id", "name"]).Sql.Should().Be("delete from [users] output deleted.[id], deleted.[name] where [id] = @p0");
        Query.For(DatabaseDialect.SqlServer).Table("users as u").Where("id", 1).CompileDeleteReturning(["id"]).Sql.Should().Be("delete [u] output deleted.[id] from [users] as [u] where [id] = @p0");
        Users(DatabaseDialect.SqlServer).Join("o", "o.uid", "users.id").Where("o.x", 1).CompileDeleteReturning(["id"]).Sql
            .Should().Be("delete [users] output deleted.[id] from [users] inner join [o] on [o].[uid] = [users].[id] where [o].[x] = @p0");
        Users(DatabaseDialect.Sqlite).Join("o", "o.uid", "users.id").Where("o.x", 1).CompileDeleteReturning(["id"]).Sql
            .Should().EndWith("where \"o\".\"x\" = @p0) returning \"id\"");
    }

    [Fact]
    public void MySQL_refuses_returning_everywhere()
    {
        var mysql = Users(DatabaseDialect.MySql);
        FluentActions.Invoking(() => mysql.CompileInsertReturning([Row("a", 1)], ["id"])).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => mysql.Where("id", 1).CompileUpdateReturning(new Dictionary<string, object?> { ["votes"] = 1 }, ["id"])).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => mysql.Where("id", 1).CompileDeleteReturning(["id"])).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void The_write_guards_still_apply_with_returning()
    {
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).CompileDeleteReturning(["id"])).Should().Throw<InvalidOperationException>().WithMessage("*no where*");
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).CompileUpdateReturning(new Dictionary<string, object?> { ["votes"] = 1 }, ["id"])).Should().Throw<InvalidOperationException>().WithMessage("*no where*");
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).Where("a", 1).CompileInsertReturning([Row("a", 1)], ["id"])).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Returned_columns_must_be_plain_names_and_not_empty()
    {
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).CompileInsertReturning([Row("a", 1)], [])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).CompileInsertReturning([Row("a", 1)], ["id; drop table users"])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).CompileInsertReturning([Row("a", 1)], ["u.id"])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_batch_that_needs_two_statements_is_refused_because_rows_cannot_be_returned_across_statements()
    {
        var rows = Enumerable.Range(0, 1001).Select(i => Row("n" + i, i)).ToArray();
        FluentActions.Invoking(() => Users(DatabaseDialect.SqlServer).CompileInsertReturning(rows, ["id"])).Should().Throw<InvalidOperationException>().WithMessage("*smaller batches*");
    }
}

/// <summary>R23.T05: returning runs on in-memory SQLite.</summary>
public sealed class QueryReturningExecutionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table users (id integer primary key autoincrement, name text not null, votes integer not null default 7)");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static Dictionary<string, object?> Row(string name) => new() { ["name"] = name };

    [Fact]
    public async Task Insert_returning_gives_back_generated_keys_and_defaults()
    {
        var row = await _db.Table("users").InsertReturningAsync(Row("a"), ["id", "votes"]);

        Convert.ToInt64(row["id"]).Should().Be(1);
        Convert.ToInt64(row["votes"]).Should().Be(7, "the default the database applied");
    }

    [Fact]
    public async Task Insert_returning_a_batch_returns_one_row_each()
    {
        var rows = await _db.Table("users").InsertReturningAsync(new[] { Row("a"), Row("b"), Row("c") }, ["id", "name"]);

        rows.Select(r => (string)r["name"]!).Should().Equal("a", "b", "c");
        rows.Select(r => Convert.ToInt64(r["id"])).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Update_returning_gives_back_the_new_values_of_the_updated_rows()
    {
        await _db.Table("users").InsertAsync(new[] { Row("a"), Row("b") });

        var rows = await _db.Table("users").Where("name", "a").UpdateReturningAsync(new Dictionary<string, object?> { ["votes"] = 99 }, ["name", "votes"]);

        rows.Should().ContainSingle();
        rows[0]["name"].Should().Be("a");
        Convert.ToInt64(rows[0]["votes"]).Should().Be(99);
        (await _db.Table("users").Where("name", "b").ValueAsync<long>("votes")).Should().Be(7);
    }

    [Fact]
    public async Task Delete_returning_gives_back_the_deleted_rows()
    {
        await _db.Table("users").InsertAsync(new[] { Row("a"), Row("b") });

        var rows = await _db.Table("users").Where("name", "b").DeleteReturningAsync(["id", "name"]);

        rows.Should().ContainSingle();
        rows[0]["name"].Should().Be("b");
        (await _db.Table("users").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Delete_returning_still_needs_a_where()
    {
        await _db.Table("users").InsertAsync(Row("a"));

        await FluentActions.Invoking(() => _db.Table("users").DeleteReturningAsync(["id"])).Should().ThrowAsync<InvalidOperationException>();
        (await _db.Table("users").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Returning_nothing_matched_gives_an_empty_list()
    {
        var rows = await _db.Table("users").Where("id", 404).DeleteReturningAsync(["id"]);

        rows.Should().BeEmpty();
    }
}
