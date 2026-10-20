using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R23.T02: update SQL on every grammar. Pure strings.</summary>
public class QueryUpdateTests
{
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static Dictionary<string, object?> Values(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

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
    public void Update_writes_set_then_where_with_bindings_in_text_order()
    {
        Verify("one column", q => q.Where("id", 7).CompileUpdate(Values(("a", 1))), "update «users» set «a» = @p0 where «id» = @p1", 1, 7);
        Verify("two columns", q => q.Where("id", 7).CompileUpdate(Values(("a", 1), ("b", "x"))), "update «users» set «a» = @p0, «b» = @p1 where «id» = @p2", 1, "x", 7);
        Verify("null is bound", q => q.Where("id", 7).CompileUpdate(Values(("a", null))), "update «users» set «a» = @p0 where «id» = @p1", null, 7);
        Verify("all rows when confirmed", q => q.AllRows().CompileUpdate(Values(("a", 1))), "update «users» set «a» = @p0", 1);
        Verify("where groups", q => q.Where("a", 1).OrWhere("b", 2).CompileUpdate(Values(("c", 3))), "update «users» set «c» = @p0 where «a» = @p1 or «b» = @p2", 3, 1, 2);
    }

    [Fact]
    public void Increment_and_decrement_add_to_the_current_value()
    {
        Verify("increment", q => q.Where("id", 1).CompileUpdate(null, Values(("votes", 1L))), "update «users» set «votes» = «votes» + @p0 where «id» = @p1", 1L, 1);
        Verify("decrement is a negative addition", q => q.Where("id", 1).CompileUpdate(null, Values(("votes", -3L))), "update «users» set «votes» = «votes» + @p0 where «id» = @p1", -3L, 1);
        Verify("each", q => q.Where("id", 1).CompileUpdate(null, Values(("a", 1L), ("b", 2L))), "update «users» set «a» = «a» + @p0, «b» = «b» + @p1 where «id» = @p2", 1L, 2L, 1);
        Verify("extra columns come first", q => q.Where("id", 1).CompileUpdate(Values(("tag", "x")), Values(("votes", 1L))),
            "update «users» set «tag» = @p0, «votes» = «votes» + @p1 where «id» = @p2", "x", 1L, 1);
    }

    [Fact]
    public void A_table_alias_is_kept_and_sql_server_updates_the_alias()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = Query.For(dialect).Table("users as u").Where("id", 1).CompileUpdate(Values(("a", 1)));
            var expected = dialect == DatabaseDialect.SqlServer
                ? "update «u» set «a» = @p0 from «users» as «u» where «id» = @p1"
                : "update «users» as «u» set «a» = @p0 where «id» = @p1";

            compiled.Sql.Should().Be(Quoted(expected, dialect), dialect.ToString());
        }
    }

    [Fact]
    public void Joins_in_an_update_follow_each_dialects_syntax()
    {
        Query Joined(DatabaseDialect d) => Query.For(d).Table("users").Join("o", "o.uid", "users.id").Where("o.x", 1);

        Joined(DatabaseDialect.MySql).CompileUpdate(Values(("a", 1))).Sql.Should().Be("update `users` inner join `o` on `o`.`uid` = `users`.`id` set `a` = @p0 where `o`.`x` = @p1");
        Joined(DatabaseDialect.SqlServer).CompileUpdate(Values(("a", 1))).Sql.Should().Be("update [users] set [a] = @p0 from [users] inner join [o] on [o].[uid] = [users].[id] where [o].[x] = @p1");
        foreach (var dialect in new[] { DatabaseDialect.Postgres, DatabaseDialect.Sqlite })
        {
            var compiled = Joined(dialect).CompileUpdate(Values(("a", 1)));
            compiled.Sql.Should().Be("update \"users\" set \"a\" = @p0 from \"o\" where (\"o\".\"uid\" = \"users\".\"id\") and (\"o\".\"x\" = @p1)", dialect.ToString());
            compiled.Bindings.Should().Equal(1, 1);
        }
    }

    [Fact]
    public void Update_from_takes_several_inner_joins_and_refuses_the_others()
    {
        var two = Query.For(DatabaseDialect.Postgres).Table("users").Join("o", "o.uid", "users.id").Join("p", "p.oid", "o.id");
        two.AllRows().CompileUpdate(Values(("a", 1))).Sql.Should().Be("update \"users\" set \"a\" = @p0 from \"o\" inner join \"p\" on \"p\".\"oid\" = \"o\".\"id\" where (\"o\".\"uid\" = \"users\".\"id\")");

        foreach (var dialect in new[] { DatabaseDialect.Postgres, DatabaseDialect.Sqlite })
        {
            FluentActions.Invoking(() => Query.For(dialect).Table("users").LeftJoin("o", "o.uid", "users.id").Where("id", 1).CompileUpdate(Values(("a", 1)))).Should().Throw<NotSupportedException>().WithMessage($"*{dialect}*");
            FluentActions.Invoking(() => Query.For(dialect).Table("users").CrossJoin("o").Where("id", 1).CompileUpdate(Values(("a", 1)))).Should().Throw<NotSupportedException>();
        }

        Query.For(DatabaseDialect.MySql).Table("users").LeftJoin("o", "o.uid", "users.id").Where("id", 1).CompileUpdate(Values(("a", 1))).Sql.Should().Contain("left join");
        Query.For(DatabaseDialect.SqlServer).Table("users").LeftJoin("o", "o.uid", "users.id").Where("id", 1).CompileUpdate(Values(("a", 1))).Sql.Should().Contain("left join");
    }

    [Fact]
    public void Json_paths_use_each_dialects_set_function_and_several_paths_nest()
    {
        var one = Values(("data->a", 5));
        var two = Values(("data->a", 5), ("data->b->c", "x"));

        var sqlite = Query.For(DatabaseDialect.Sqlite).Table("users").Where("id", 1);
        sqlite.CompileUpdate(one).Sql.Should().Be("update \"users\" set \"data\" = json_set(\"data\", @p0, json(@p1)) where \"id\" = @p2");
        sqlite.CompileUpdate(one).Bindings.Should().Equal("$.\"a\"", "5", 1);
        sqlite.CompileUpdate(two).Sql.Should().Be("update \"users\" set \"data\" = json_set(json_set(\"data\", @p0, json(@p1)), @p2, json(@p3)) where \"id\" = @p4");
        sqlite.CompileUpdate(two).Bindings.Should().Equal("$.\"a\"", "5", "$.\"b\".\"c\"", "\"x\"", 1);

        Query.For(DatabaseDialect.MySql).Table("users").Where("id", 1).CompileUpdate(one).Sql.Should().Be("update `users` set `data` = json_set(`data`, @p0, cast(@p1 as json)) where `id` = @p2");

        var postgres = Query.For(DatabaseDialect.Postgres).Table("users").Where("id", 1).CompileUpdate(one);
        postgres.Sql.Should().Be("update \"users\" set \"data\" = jsonb_set(cast(\"data\" as jsonb), cast(@p0 as text[]), cast(@p1 as jsonb)) where \"id\" = @p2");
        postgres.Bindings.Should().Equal("{a}", "5", 1);

        var server = Query.For(DatabaseDialect.SqlServer).Table("users").Where("id", 1);
        server.CompileUpdate(one).Sql.Should().Be("update [users] set [data] = json_modify([data], @p0, @p1) where [id] = @p2");
        server.CompileUpdate(Values(("data->flag", true))).Sql.Should().Be("update [users] set [data] = json_modify([data], @p0, cast(@p1 as bit)) where [id] = @p2");
        server.CompileUpdate(Values(("data->obj", new Dictionary<string, int> { ["k"] = 1 }))).Sql.Should().Be("update [users] set [data] = json_modify([data], @p0, json_query(@p1)) where [id] = @p2");
        FluentActions.Invoking(() => server.CompileUpdate(Values(("data->a", null)))).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_json_path_and_a_plain_column_can_share_one_update_but_not_the_same_column()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users").Where("id", 1);

        query.CompileUpdate(Values(("votes", 1), ("data->a", 2))).Sql.Should().Be("update \"users\" set \"votes\" = @p0, \"data\" = json_set(\"data\", @p1, json(@p2)) where \"id\" = @p3");
        FluentActions.Invoking(() => query.CompileUpdate(Values(("data", "x"), ("data->a", 2)))).Should().Throw<ArgumentException>().WithMessage("*whole*");
        FluentActions.Invoking(() => query.CompileUpdate(Values(("data->a", 2), ("data", "x")))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Bad_updates_fail_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users").Where("id", 1);

        FluentActions.Invoking(() => query.CompileUpdate(Values())).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileUpdate(Values(("users.a", 1)))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileUpdate(Values(("a b", 1)))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileUpdate(Values(("a as b", 1)))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileUpdate(null, Values(("data->a", 1L)))).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).CompileUpdate(Values(("a", 1)))).Should().Throw<InvalidOperationException>().WithMessage("*no table*");
    }

    [Fact]
    public void An_update_without_a_where_is_refused_unless_all_rows_are_confirmed()
    {
        var values = Values(("a", 1));

        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").CompileUpdate(values)).Should().Throw<InvalidOperationException>().WithMessage("*every row*");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").Join("o", "o.a", "users.a").CompileUpdate(values)).Should().Throw<InvalidOperationException>().WithMessage("*every row*");
        Query.For(DatabaseDialect.Sqlite).Table("users").AllRows().CompileUpdate(values).Sql.Should().Be("update \"users\" set \"a\" = @p0");
        Query.For(DatabaseDialect.Sqlite).Table("users").AllRows().Clone().CompileUpdate(values).Sql.Should().Be("update \"users\" set \"a\" = @p0", "Clone keeps the confirmation");
    }

    [Fact]
    public void An_update_query_may_only_have_a_table_joins_and_where()
    {
        foreach (var query in new[]
        {
            Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1).Select("a"),
            Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1).OrderBy("a"),
            Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1).Limit(1),
            Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1).Offset(1),
            Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1).GroupBy("a"),
            Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1).Distinct(),
        })
        {
            FluentActions.Invoking(() => query.CompileUpdate(Values(("a", 1)))).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
        }
    }

    [Fact]
    public async Task Unconnected_updates_fail_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users").Where("id", 1);

        await FluentActions.Awaiting(() => query.UpdateAsync(Values(("a", 1)))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => query.IncrementAsync("votes")).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => query.UpdateOrInsertAsync(Values(("a", 1)), Values())).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => query.UpdateOrInsertAsync(Values(), Values(("b", 1)))).Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>R23.T02: updates run on in-memory SQLite.</summary>
public sealed class QueryUpdateExecutionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table users (id integer primary key, name text not null, votes integer not null, score real null, data text null)");
        await _db.UnpreparedAsync("create table teams (id integer primary key, user_id integer not null, label text not null)");
        for (var i = 1; i <= 3; i++) await _db.StatementAsync("insert into users (id, name, votes, score, data) values (?, ?, ?, ?, ?)", [i, "user" + i, i * 10, i * 1.5, "{\"n\":" + i + ",\"keep\":true,\"nest\":{\"x\":0}}"]);
        await _db.StatementAsync("insert into teams (user_id, label) values (1, 'red'), (3, 'blue')");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static Dictionary<string, object?> Values(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public async Task Update_changes_only_the_matching_rows_and_reports_how_many()
    {
        (await _db.Table("users").Where("votes", ">=", 20).UpdateAsync(Values(("name", "big")))).Should().Be(2);

        (await _db.Table("users").OrderBy("id").PluckAsync<string>("name")).Should().Equal("user1", "big", "big");
        (await _db.Table("users").Where("id", 99).UpdateAsync(Values(("name", "x")))).Should().Be(0);
    }

    [Fact]
    public async Task Update_without_a_where_is_refused_and_update_all_changes_every_row()
    {
        await FluentActions.Awaiting(() => _db.Table("users").UpdateAsync(Values(("name", "x")))).Should().ThrowAsync<InvalidOperationException>();
        (await _db.Table("users").Where("name", "x").CountAsync()).Should().Be(0);

        (await _db.Table("users").UpdateAllAsync(Values(("name", "x")))).Should().Be(3);
        (await _db.Table("users").Where("name", "x").CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Increment_and_decrement_run_in_one_statement_and_take_extra_columns()
    {
        (await _db.Table("users").Where("id", 1).IncrementAsync("votes")).Should().Be(1);
        (await _db.Table("users").Where("id", 1).IncrementAsync("votes", 4)).Should().Be(1);
        (await _db.Table("users").Where("id", 1).DecrementAsync("votes", 2, Values(("name", "renamed")))).Should().Be(1);
        (await _db.Table("users").Where("id", 1).ValueAsync<long>("votes")).Should().Be(13);
        (await _db.Table("users").Where("id", 1).ValueAsync<string>("name")).Should().Be("renamed");

        (await _db.Table("users").Where("id", 2).IncrementEachAsync(new Dictionary<string, long> { ["votes"] = 5, ["score"] = 0 })).Should().Be(1);
        (await _db.Table("users").Where("id", 2).ValueAsync<long>("votes")).Should().Be(25);

        (await _db.Table("users").Where("id", 3).IncrementAsync("score", 0.5m)).Should().Be(1);
        (await _db.Table("users").Where("id", 3).ValueAsync<double>("score")).Should().Be(5.0);
    }

    [Fact]
    public async Task Update_or_insert_inserts_once_then_updates()
    {
        var attributes = Values(("id", 10));
        (await _db.Table("users").UpdateOrInsertAsync(attributes, Values(("name", "new"), ("votes", 1)))).Should().BeTrue();
        (await _db.Table("users").UpdateOrInsertAsync(attributes, Values(("votes", 2)))).Should().BeFalse();

        (await _db.Table("users").Where("id", 10).ValueAsync<long>("votes")).Should().Be(2);
        (await _db.Table("users").Where("id", 10).ValueAsync<string>("name")).Should().Be("new");
        (await _db.Table("users").Where("id", 10).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Update_with_a_join_uses_update_from_on_sqlite()
    {
        var changed = await _db.Table("users").Join("teams", "teams.user_id", "users.id").Where("teams.label", "blue").UpdateAsync(Values(("name", "blue-user")));

        changed.Should().Be(1);
        (await _db.Table("users").OrderBy("id").PluckAsync<string>("name")).Should().Equal("user1", "user2", "blue-user");
    }

    [Fact]
    public async Task Json_paths_are_set_inside_the_column_and_the_rest_is_kept()
    {
        await _db.Table("users").Where("id", 1).UpdateAsync(Values(("data->n", 42), ("data->nest->x", "yes"), ("votes", 7)));

        var json = System.Text.Json.JsonDocument.Parse((await _db.Table("users").Where("id", 1).ValueAsync<string>("data"))!).RootElement;
        json.GetProperty("n").GetInt32().Should().Be(42);
        json.GetProperty("keep").GetBoolean().Should().BeTrue();
        json.GetProperty("nest").GetProperty("x").GetString().Should().Be("yes");
        (await _db.Table("users").Where("id", 1).ValueAsync<long>("votes")).Should().Be(7);
    }
}
