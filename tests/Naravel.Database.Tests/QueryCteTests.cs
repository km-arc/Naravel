using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R24.T03: common table expressions on every grammar. Pure strings.</summary>
public class QueryCteTests
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

    private static Query Tree(DatabaseDialect d)
    {
        var seed = Q(d, "categories").Select("id", "name").SelectRaw("0").WhereNull("parent_id");
        var step = Q(d, "categories").Select("categories.id", "categories.name").SelectRaw("tree.depth + 1").Join("tree", "tree.id", "categories.parent_id");
        return Q(d, "tree").WithRecursiveExpression("tree", ["id", "name", "depth"], seed.UnionAll(step));
    }

    [Fact]
    public void A_plain_expression_is_written_before_the_select()
    {
        Verify("with", d => Q(d, "big").WithExpression("big", Q(d, "users").Where("votes", ">", 5)).Where("id", 1),
            "with «big» as (select * from «users» where «votes» > @p0) select * from «big» where «id» = @p1", null, 5, 1);
        Verify("with columns", d => Q(d, "big").WithExpression("big", ["a", "b"], Q(d, "users").Select("id", "name")),
            "with «big» («a», «b») as (select «id», «name» from «users») select * from «big»");
        Verify("two expressions, the second uses the first", d => Q(d, "b").WithExpression("a", Q(d, "u").Where("x", 1)).WithExpression("b", Q(d, "a").Where("y", 2)),
            "with «a» as (select * from «u» where «x» = @p0), «b» as (select * from «a» where «y» = @p1) select * from «b»", null, 1, 2);
    }

    [Fact]
    public void A_recursive_expression_writes_recursive_except_on_sql_server()
    {
        var sql = "with recursive «tree» («id», «name», «depth») as (select «id», «name», 0 from «categories» where «parent_id» is null union all select «categories».«id», «categories».«name», tree.depth + 1 from «categories» inner join «tree» on «tree».«id» = «categories».«parent_id») select * from «tree»";
        Verify("tree walk", Tree, sql, new() { [DatabaseDialect.SqlServer] = sql.Replace("with recursive", "with") });
    }

    [Fact]
    public void Recursive_is_written_once_when_any_expression_is_recursive()
    {
        Verify("mixed", d => Q(d, "n")
                .WithExpression("a", Q(d, "u"))
                .WithRecursiveExpression("n", ["v"], Q(d, "a").Select("id").UnionAll(Q(d, "n").SelectRaw("v + 1").Where("v", "<", 3))),
            "with recursive «a» as (select * from «u»), «n» («v») as (select «id» from «a» union all select v + 1 from «n» where «v» < @p0) select * from «n»",
            new() { [DatabaseDialect.SqlServer] = "with «a» as (select * from «u»), «n» («v») as (select «id» from «a» union all select v + 1 from «n» where «v» < @p0) select * from «n»" }, 3);
    }

    [Fact]
    public void The_expression_covers_the_whole_union()
    {
        Verify("union", d => Q(d, "big").WithExpression("big", Q(d, "u")).Union(Q(d, "other")),
            "with «big» as (select * from «u») select * from «big» union select * from «other»");
        Verify("union with order", d => Q(d, "big").WithExpression("big", Q(d, "u")).Union(Q(d, "other")).OrderBy("id"),
            "with «big» as (select * from «u») select * from (select * from «big» union select * from «other») as «naravel_union» order by «id» asc");
    }

    [Fact]
    public void A_union_term_with_its_own_expression_becomes_a_derived_table()
    {
        Verify("term", d => Q(d, "a").Union(Q(d, "big").WithExpression("big", Q(d, "u"))),
            "select * from «a» union select * from (with «big» as (select * from «u») select * from «big») as «naravel_union_1»");
    }

    [Fact]
    public void Count_and_exists_hoist_the_expressions_to_the_front()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var query = Q(dialect, "big").WithExpression("big", Q(dialect, "u").Where("x", 1)).Limit(5);
            var count = query.CompileAggregate("count", null).Sql;

            count.Should().StartWith(Quoted("with «big» as (select * from «u» where «x» = @p0) select count(*)", dialect), dialect.ToString());
            count.Should().NotContain("(with", "with must not be inside the derived table", dialect.ToString());
        }

        Q(DatabaseDialect.Sqlite, "big").WithExpression("big", Q(DatabaseDialect.Sqlite, "u")).CompileAggregate("count", null).Sql
            .Should().Be("with \"big\" as (select * from \"u\") select count(*) as \"aggregate\" from \"big\"");
    }

    [Fact]
    public void Names_must_be_plain_and_unique_and_the_query_must_share_the_dialect()
    {
        var sub = Q(DatabaseDialect.Sqlite, "u");
        var query = Q(DatabaseDialect.Sqlite, "t");

        FluentActions.Invoking(() => query.WithExpression("a.b", sub)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.WithExpression("a b", sub)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.WithExpression("x; drop table t", sub)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.WithExpression("a", ["b c"], sub)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.WithExpression("a", [], sub)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.WithExpression("a", Q(DatabaseDialect.MySql, "u"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.WithExpression("a", null!)).Should().Throw<ArgumentNullException>();
        query.Clone().WithExpression("a", sub).Invoking(q => q.WithExpression("A", sub)).Should().Throw<ArgumentException>().WithMessage("*already*");
    }

    [Fact]
    public void The_expression_query_is_copied_and_survives_clone()
    {
        var body = Q(DatabaseDialect.Sqlite, "u").Where("x", 1);
        var query = Q(DatabaseDialect.Sqlite, "big").WithExpression("big", body);
        body.Where("later", 9);

        query.Compile().Sql.Should().Be("with \"big\" as (select * from \"u\" where \"x\" = @p0) select * from \"big\"");
        query.Clone().Where("y", 2).Compile().Sql.Should().StartWith("with \"big\" as");
    }

    [Fact]
    public void Writes_and_insert_sources_refuse_expressions()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var values = new Dictionary<string, object?> { ["v"] = 1 };
            var withCte = Q(dialect, "t").WithExpression("big", Q(dialect, "u")).Where("x", 1);

            FluentActions.Invoking(() => withCte.CompileUpdate(values)).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => withCte.CompileDelete()).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => Q(dialect, "t").CompileInsertUsing(["id"], Q(dialect, "big").WithExpression("big", Q(dialect, "u")))).Should().Throw<InvalidOperationException>();
        }
    }
}

/// <summary>R24.T03: a tree walk on in-memory SQLite.</summary>
public sealed class QueryCteExecutionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table categories (id integer primary key, parent_id integer null, name text not null)");
        await _db.UnpreparedAsync("insert into categories (id, parent_id, name) values (1, null, 'root'), (2, 1, 'a'), (3, 1, 'b'), (4, 2, 'a1'), (5, 4, 'a1x'), (6, null, 'other')");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private Query Tree(string startName)
    {
        var seed = _db.Table("categories").Select("id", "name").SelectRaw("0").Where("name", startName);
        var step = _db.Table("categories").Select("categories.id", "categories.name").SelectRaw("tree.depth + 1").Join("tree", "tree.id", "categories.parent_id");
        return _db.Table("tree").WithRecursiveExpression("tree", ["id", "name", "depth"], seed.UnionAll(step));
    }

    [Fact]
    public async Task A_recursive_expression_walks_a_tree_with_its_depth()
    {
        var rows = await Tree("root").OrderBy("depth").OrderBy("id").GetAsync();

        rows.Select(r => (string)r["name"]!).Should().Equal("root", "a", "b", "a1", "a1x");
        rows.Select(r => Convert.ToInt64(r["depth"])).Should().Equal(0, 1, 1, 2, 3);
    }

    [Fact]
    public async Task The_start_value_is_a_bound_parameter()
    {
        var rows = await Tree("a1").OrderBy("id").PluckAsync<string>("name");

        rows.Should().Equal("a1", "a1x");
    }

    [Fact]
    public async Task Count_and_exists_run_over_the_expression()
    {
        (await Tree("root").CountAsync()).Should().Be(5);
        (await Tree("root").Where("depth", 3).ExistsAsync()).Should().BeTrue();
        (await Tree("root").Where("depth", 9).ExistsAsync()).Should().BeFalse();
        (await Tree("root").Limit(2).CountAsync()).Should().Be(2, "a limited query is counted through a derived table with the expression hoisted");
    }

    [Fact]
    public async Task A_plain_expression_can_be_joined()
    {
        var names = await _db.Table("categories").Select("categories.name")
            .WithExpression("roots", _db.Table("categories").Select("id").WhereNull("parent_id"))
            .Join("roots", "roots.id", "categories.parent_id").OrderBy("categories.id").PluckAsync<string>("name");

        names.Should().Equal("a", "b");
    }
}
