using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R24.T02: FromSub, SelectSub, JoinSub and lateral joins on every grammar. Pure strings.</summary>
public class QuerySubquerySourceTests
{
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static Query Q(DatabaseDialect d, string table) => Query.For(d).Table(table);

    private static void Verify(string name, Func<DatabaseDialect, Query> build, string sql, Dictionary<DatabaseDialect, string>? overrides = null, params object?[] bindings)
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var template = overrides is not null && overrides.TryGetValue(dialect, out var special) ? special : sql;
            var compiled = build(dialect).Compile();

            compiled.Sql.Should().Be(Quoted(template, dialect), $"case '{name}' on {dialect}");
            compiled.Bindings.Should().Equal(bindings, $"case '{name}' bindings on {dialect}");
        }
    }

    [Fact]
    public void FromSub_selects_from_a_derived_table()
    {
        Verify("from sub", d => Query.For(d).FromSub(Q(d, "a").Where("x", 1), "t").Where("y", 2),
            "select * from (select * from «a» where «x» = @p0) as «t» where «y» = @p1", null, 1, 2);
        Verify("from a union", d => Query.For(d).FromSub(Q(d, "a").Union(Q(d, "b")), "u"),
            "select * from (select * from «a» union select * from «b») as «u»");
        Verify("table after from sub replaces it", d => Query.For(d).FromSub(Q(d, "a"), "t").Table("c"), "select * from «c»");
        Verify("from sub after table replaces it", d => Q(d, "c").FromSub(Q(d, "a"), "t"), "select * from (select * from «a») as «t»");
    }

    [Fact]
    public void SelectSub_adds_a_scalar_subquery_column_in_text_order()
    {
        Verify("correlated count", d => Q(d, "users").Select("id").SelectSub(Q(d, "posts").SelectRaw("count(*)").WhereColumn("posts.user_id", "users.id"), "post_count"),
            "select «id», (select count(*) from «posts» where «posts».«user_id» = «users».«id») as «post_count» from «users»");
        Verify("alone", d => Q(d, "users").SelectSub(Q(d, "posts").Select("n"), "p"), "select (select «n» from «posts») as «p» from «users»");
        Verify("bindings before the outer where", d => Q(d, "users").SelectSub(Q(d, "posts").Select("n").Where("k", 5), "p").Where("id", 1),
            "select (select «n» from «posts» where «k» = @p0) as «p» from «users» where «id» = @p1", null, 5, 1);
    }

    [Fact]
    public void Join_subqueries_write_a_derived_table_before_on()
    {
        Verify("join sub", d => Q(d, "users").JoinSub(Q(d, "posts").Select("user_id").Where("x", 1), "p", "p.user_id", "users.id"),
            "select * from «users» inner join (select «user_id» from «posts» where «x» = @p0) as «p» on «p».«user_id» = «users».«id»", null, 1);
        Verify("left join sub with operator", d => Q(d, "users").LeftJoinSub(Q(d, "posts").Select("user_id"), "p", "p.user_id", ">=", "users.id"),
            "select * from «users» left join (select «user_id» from «posts») as «p» on «p».«user_id» >= «users».«id»");
        Verify("right join sub", d => Q(d, "users").RightJoinSub(Q(d, "posts").Select("user_id"), "p", "p.user_id", "users.id"),
            "select * from «users» right join (select «user_id» from «posts») as «p» on «p».«user_id» = «users».«id»");
        Verify("callback conditions bind after the subquery", d => Q(d, "users").JoinSub(Q(d, "posts").Select("user_id").Where("x", 1), "p", on => on.On("p.user_id", "users.id").Where("p.n", ">", 3)).Where("users.z", 9),
            "select * from «users» inner join (select «user_id» from «posts» where «x» = @p0) as «p» on «p».«user_id» = «users».«id» and «p».«n» > @p1 where «users».«z» = @p2", null, 1, 3, 9);
    }

    [Fact]
    public void Every_source_binds_in_the_order_it_appears_in_the_text()
    {
        Verify("select sub, from sub, join sub, where", d => Query.For(d)
                .FromSub(Q(d, "a").Where("a1", 1), "t")
                .SelectSub(Q(d, "b").Select("n").Where("b1", 2), "s")
                .JoinSub(Q(d, "c").Select("k").Where("c1", 3), "j", "j.k", "t.k")
                .Where("w", 4),
            "select (select «n» from «b» where «b1» = @p0) as «s» from (select * from «a» where «a1» = @p1) as «t» inner join (select «k» from «c» where «c1» = @p2) as «j» on «j».«k» = «t».«k» where «w» = @p3",
            null, 2, 1, 3, 4);
    }

    [Fact]
    public void Lateral_joins_use_join_lateral_or_apply()
    {
        Query Posts(DatabaseDialect d) => Q(d, "posts").WhereColumn("posts.user_id", "users.id").Limit(3);

        var inner = "select * from «users» inner join lateral (select * from «posts» where «posts».«user_id» = «users».«id» limit 3) as «p» on true";
        var left = "select * from «users» left join lateral (select * from «posts» where «posts».«user_id» = «users».«id» limit 3) as «p» on true";
        foreach (var dialect in new[] { DatabaseDialect.MySql, DatabaseDialect.Postgres })
        {
            Quoted(inner, dialect).Should().Be(Q(dialect, "users").JoinLateral(Posts(dialect), "p").Compile().Sql);
            Quoted(left, dialect).Should().Be(Q(dialect, "users").LeftJoinLateral(Posts(dialect), "p").Compile().Sql);
        }

        Q(DatabaseDialect.SqlServer, "users").JoinLateral(Posts(DatabaseDialect.SqlServer), "p").Compile().Sql
            .Should().Be("select * from [users] cross apply (select top (3) * from [posts] where [posts].[user_id] = [users].[id]) as [p]");
        Q(DatabaseDialect.SqlServer, "users").LeftJoinLateral(Posts(DatabaseDialect.SqlServer), "p").Compile().Sql
            .Should().Be("select * from [users] outer apply (select top (3) * from [posts] where [posts].[user_id] = [users].[id]) as [p]");
    }

    [Fact]
    public void SQLite_has_no_lateral_join()
    {
        FluentActions.Invoking(() => Q(DatabaseDialect.Sqlite, "users").JoinLateral(Q(DatabaseDialect.Sqlite, "posts"), "p").Compile()).Should().Throw<NotSupportedException>().WithMessage("*Sqlite*");
        FluentActions.Invoking(() => Q(DatabaseDialect.Sqlite, "users").LeftJoinLateral(Q(DatabaseDialect.Sqlite, "posts"), "p").Compile()).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void The_subquery_is_copied_and_must_share_the_dialect()
    {
        var sub = Q(DatabaseDialect.Sqlite, "a").Where("x", 1);
        var query = Query.For(DatabaseDialect.Sqlite).FromSub(sub, "t");
        sub.Where("later", 9);

        query.Compile().Sql.Should().Be("select * from (select * from \"a\" where \"x\" = @p0) as \"t\"");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).FromSub(Q(DatabaseDialect.MySql, "a"), "t")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).SelectSub(Q(DatabaseDialect.MySql, "a"), "t")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Q(DatabaseDialect.Sqlite, "a").JoinSub(Q(DatabaseDialect.MySql, "b"), "t", "t.a", "a.b")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).FromSub(null!, "t")).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData("*")]
    [InlineData("a; drop table x")]
    [InlineData("x as y")]
    public void An_alias_is_one_plain_identifier(string alias)
    {
        var sub = Q(DatabaseDialect.Sqlite, "a");

        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).FromSub(sub, alias)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Q(DatabaseDialect.Sqlite, "t").SelectSub(sub, alias)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Q(DatabaseDialect.Sqlite, "t").JoinSub(sub, alias, "a.x", "t.x")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Q(DatabaseDialect.Sqlite, "t").JoinLateral(sub, alias)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_join_callback_needs_a_condition()
    {
        FluentActions.Invoking(() => Q(DatabaseDialect.Sqlite, "t").JoinSub(Q(DatabaseDialect.Sqlite, "a"), "j", _ => { })).Should().Throw<InvalidOperationException>().WithMessage("*at least one condition*");
    }

    [Fact]
    public void A_query_with_a_subquery_source_is_read_only()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var values = new Dictionary<string, object?> { ["v"] = 1 };
            FluentActions.Invoking(() => Query.For(dialect).FromSub(Q(dialect, "a"), "t").Where("x", 1).CompileUpdate(values)).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => Query.For(dialect).FromSub(Q(dialect, "a"), "t").Where("x", 1).CompileDelete()).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => Q(dialect, "t").JoinSub(Q(dialect, "a"), "j", "j.x", "t.x").Where("x", 1).CompileUpdate(values)).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => Q(dialect, "t").JoinSub(Q(dialect, "a"), "j", "j.x", "t.x").Where("x", 1).CompileDelete()).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void Aggregates_work_over_a_subquery_source()
    {
        Query.For(DatabaseDialect.Sqlite).FromSub(Q(DatabaseDialect.Sqlite, "a").Where("x", 1), "t").CompileAggregate("count", null).Sql
            .Should().Be("select count(*) as \"aggregate\" from (select * from \"a\" where \"x\" = @p0) as \"t\"");
    }
}

/// <summary>R24.T02: subquery sources run on in-memory SQLite.</summary>
public sealed class QuerySubquerySourceExecutionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table users (id integer primary key, name text not null)");
        await _db.UnpreparedAsync("create table posts (id integer primary key, user_id integer not null, views integer not null)");
        await _db.UnpreparedAsync("insert into users (id, name) values (1, 'ann'), (2, 'bob'), (3, 'cy')");
        await _db.UnpreparedAsync("insert into posts (id, user_id, views) values (1, 1, 4), (2, 1, 3), (3, 2, 10)");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task SelectSub_reads_a_correlated_count()
    {
        var rows = await _db.Table("users").Select("name")
            .SelectSub(_db.Table("posts").SelectRaw("count(*)").WhereColumn("posts.user_id", "users.id"), "post_count")
            .OrderBy("id").GetAsync();

        rows.Select(r => (string)r["name"]!).Should().Equal("ann", "bob", "cy");
        rows.Select(r => Convert.ToInt64(r["post_count"])).Should().Equal(2, 1, 0);
    }

    [Fact]
    public async Task FromSub_filters_over_an_aggregate()
    {
        var users = await _db.Table("posts").FromSub(_db.Table("posts").Select("user_id").SelectRaw("sum(views) as total").GroupBy("user_id"), "t")
            .Where("total", ">", 8).PluckAsync<long>("user_id");

        users.Should().Equal(2);
    }

    [Fact]
    public async Task JoinSub_and_LeftJoinSub_join_a_derived_table()
    {
        var totals = _db.Table("posts").Select("user_id").SelectRaw("sum(views) as total").GroupBy("user_id");

        var inner = await _db.Table("users").Select("users.name", "t.total").JoinSub(totals, "t", "t.user_id", "users.id").OrderBy("users.id").GetAsync();
        var left = await _db.Table("users").Select("users.name", "t.total").LeftJoinSub(totals, "t", "t.user_id", "users.id").OrderBy("users.id").GetAsync();

        inner.Select(r => (string)r["name"]!).Should().Equal("ann", "bob");
        left.Select(r => (string)r["name"]!).Should().Equal("ann", "bob", "cy");
        left[2]["total"].Should().BeNull();
    }

    [Fact]
    public async Task Count_over_a_subquery_source()
    {
        (await _db.Table("users").FromSub(_db.Table("posts").Select("user_id").Where("views", ">", 3), "t").CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_lateral_join_throws_on_sqlite_before_any_command()
    {
        await FluentActions.Invoking(() => _db.Table("users").JoinLateral(_db.Table("posts").WhereColumn("posts.user_id", "users.id"), "p").GetAsync()).Should().ThrowAsync<NotSupportedException>();
    }
}
