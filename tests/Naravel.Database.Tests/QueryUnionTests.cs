using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R24.T01: union SQL on every grammar. Pure strings.</summary>
public class QueryUnionTests
{
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static Query A(DatabaseDialect d) => Query.For(d).Table("a");

    private static Query B(DatabaseDialect d) => Query.For(d).Table("b");

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
    public void Union_and_union_all_join_bare_selects()
    {
        Verify("union", d => A(d).Union(B(d)), "select * from «a» union select * from «b»");
        Verify("union all", d => A(d).UnionAll(B(d)), "select * from «a» union all select * from «b»");
        Verify("three terms keep their order", d => A(d).Union(B(d)).UnionAll(Query.For(d).Table("c")), "select * from «a» union select * from «b» union all select * from «c»");
        Verify("columns", d => A(d).Select("id").Union(B(d).Select("id")), "select «id» from «a» union select «id» from «b»");
    }

    [Fact]
    public void Bindings_are_numbered_in_text_order_across_terms()
    {
        Verify("where in both", d => A(d).Where("x", 1).Union(B(d).Where("y", 2).Where("z", 3)),
            "select * from «a» where «x» = @p0 union select * from «b» where «y» = @p1 and «z» = @p2", null, 1, 2, 3);
        Verify("where added after union belongs to the first select", d => A(d).Union(B(d).Where("y", 2)).Where("x", 1),
            "select * from «a» where «x» = @p0 union select * from «b» where «y» = @p1", null, 1, 2);
    }

    [Fact]
    public void Ordering_and_limit_after_union_apply_to_the_whole_union()
    {
        Verify("order by", d => A(d).Union(B(d)).OrderBy("name"),
            "select * from (select * from «a» union select * from «b») as «naravel_union» order by «name» asc");
        Verify("order desc and limit", d => A(d).Union(B(d)).OrderByDesc("name").Limit(2),
            "select * from (select * from «a» union select * from «b») as «naravel_union» order by «name» desc limit 2",
            new() { [DatabaseDialect.SqlServer] = "select top (2) * from (select * from «a» union select * from «b») as «naravel_union» order by «name» desc" });
        Verify("limit without order", d => A(d).Union(B(d)).Limit(3),
            "select * from (select * from «a» union select * from «b») as «naravel_union» limit 3",
            new() { [DatabaseDialect.SqlServer] = "select top (3) * from (select * from «a» union select * from «b») as «naravel_union»" });
        Verify("offset and limit", d => A(d).Union(B(d)).OrderBy("name").Offset(1).Limit(2),
            "select * from (select * from «a» union select * from «b») as «naravel_union» order by «name» asc limit 2 offset 1",
            new() { [DatabaseDialect.SqlServer] = "select * from (select * from «a» union select * from «b») as «naravel_union» order by «name» asc offset 1 rows fetch next 2 rows only" });
    }

    [Fact]
    public void Ordering_and_limit_before_union_stay_with_the_first_select_as_a_derived_table()
    {
        Verify("limit before", d => A(d).OrderBy("id").Limit(1).Union(B(d)),
            "select * from (select * from «a» order by «id» asc limit 1) as «naravel_union_0» union select * from «b»",
            new() { [DatabaseDialect.SqlServer] = "select * from (select top (1) * from «a» order by «id» asc) as «naravel_union_0» union select * from «b»" });
        Verify("both levels", d => A(d).Limit(1).Union(B(d)).OrderBy("id"),
            "select * from (select * from (select * from «a» limit 1) as «naravel_union_0» union select * from «b») as «naravel_union» order by «id» asc",
            new() { [DatabaseDialect.SqlServer] = "select * from (select * from (select top (1) * from [a]) as [naravel_union_0] union select * from «b») as «naravel_union» order by «id» asc" });
    }

    [Fact]
    public void A_term_with_its_own_ordering_or_limit_is_wrapped()
    {
        Verify("limited second term", d => A(d).Union(B(d).OrderBy("id").Limit(5)),
            "select * from «a» union select * from (select * from «b» order by «id» asc limit 5) as «naravel_union_1»",
            new() { [DatabaseDialect.SqlServer] = "select * from «a» union select * from (select top (5) * from «b» order by «id» asc) as «naravel_union_1»" });
    }

    [Fact]
    public void Raw_ordering_after_union_keeps_its_bindings_last()
    {
        Verify("raw order", d => A(d).Where("x", 1).Union(B(d).Where("y", 2)).OrderByRaw("name = ? desc", ["z"]),
            "select * from (select * from «a» where «x» = @p0 union select * from «b» where «y» = @p1) as «naravel_union» order by name = @p2 desc", null, 1, 2, "z");
    }

    [Fact]
    public void Union_is_copied_when_added_and_survives_clone()
    {
        var other = B(DatabaseDialect.Sqlite).Where("y", 2);
        var query = A(DatabaseDialect.Sqlite).Union(other);
        other.Where("later", 9);

        query.Compile().Sql.Should().Be("select * from \"a\" union select * from \"b\" where \"y\" = @p0");
        query.Clone().OrderBy("id").Compile().Sql.Should().Be("select * from (select * from \"a\" union select * from \"b\" where \"y\" = @p0) as \"naravel_union\" order by \"id\" asc");
        query.Compile().Sql.Should().NotContain("order by", "the clone does not change the original");
    }

    [Fact]
    public void A_union_of_another_dialect_is_refused()
    {
        FluentActions.Invoking(() => A(DatabaseDialect.Sqlite).Union(B(DatabaseDialect.MySql))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => A(DatabaseDialect.Sqlite).Union(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SQL_Server_needs_an_order_for_an_offset_on_the_union()
    {
        FluentActions.Invoking(() => A(DatabaseDialect.SqlServer).Union(B(DatabaseDialect.SqlServer)).Offset(1).Compile()).Should().Throw<InvalidOperationException>().WithMessage("*OrderBy*");
    }

    [Fact]
    public void A_union_is_a_read_only_query()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var union = A(dialect).Where("x", 1).Union(B(dialect));
            FluentActions.Invoking(() => union.CompileUpdate(new Dictionary<string, object?> { ["v"] = 1 })).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => union.CompileDelete()).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => union.CompileInsert([new Dictionary<string, object?> { ["v"] = 1 }], false)).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => union.CompileExists()).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void Count_over_a_union_counts_the_combined_rows_through_a_derived_table()
    {
        Query.For(DatabaseDialect.Sqlite).Table("a").Select("id").Union(Query.For(DatabaseDialect.Sqlite).Table("b").Select("id")).CompileAggregate("count", null).Sql
            .Should().Be("select count(*) as \"aggregate\" from (select \"id\" from \"a\" union select \"id\" from \"b\") as \"naravel_aggregate\"");
    }

    [Fact]
    public void A_union_is_usable_as_an_insert_source()
    {
        var source = A(DatabaseDialect.Sqlite).Select("id").Union(B(DatabaseDialect.Sqlite).Select("id"));

        Query.For(DatabaseDialect.Sqlite).Table("t").CompileInsertUsing(["id"], source).Sql
            .Should().Be("insert into \"t\" (\"id\") select \"id\" from \"a\" union select \"id\" from \"b\"");
    }
}

/// <summary>R24.T01: unions run on in-memory SQLite.</summary>
public sealed class QueryUnionExecutionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table cats (id integer primary key, name text not null)");
        await _db.UnpreparedAsync("create table dogs (id integer primary key, name text not null)");
        await _db.UnpreparedAsync("insert into cats (id, name) values (1, 'tom'), (2, 'kit'), (3, 'max')");
        await _db.UnpreparedAsync("insert into dogs (id, name) values (1, 'rex'), (2, 'max'), (3, 'ace')");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Union_removes_duplicates_and_union_all_keeps_them()
    {
        var distinct = await _db.Table("cats").Select("name").Union(_db.Table("dogs").Select("name")).OrderBy("name").PluckAsync<string>("name");
        var all = await _db.Table("cats").Select("name").UnionAll(_db.Table("dogs").Select("name")).OrderBy("name").PluckAsync<string>("name");

        distinct.Should().Equal("ace", "kit", "max", "rex", "tom");
        all.Should().Equal("ace", "kit", "max", "max", "rex", "tom");
    }

    [Fact]
    public async Task Limit_and_offset_cut_the_combined_rows()
    {
        var page = await _db.Table("cats").Select("name").Union(_db.Table("dogs").Select("name")).OrderBy("name").Offset(1).Limit(2).PluckAsync<string>("name");

        page.Should().Equal("kit", "max");
    }

    [Fact]
    public async Task A_limit_before_the_union_cuts_only_the_first_select()
    {
        var names = await _db.Table("cats").Select("name").OrderBy("id").Limit(1).Union(_db.Table("dogs").Select("name").Where("id", 3)).OrderBy("name").PluckAsync<string>("name");

        names.Should().Equal("ace", "tom");
    }

    [Fact]
    public async Task Count_and_exists_work_over_the_combined_rows()
    {
        var union = _db.Table("cats").Select("name").Union(_db.Table("dogs").Select("name"));

        (await union.CountAsync()).Should().Be(5, "max is in both tables and a union keeps it once");
        (await union.ExistsAsync()).Should().BeTrue();
        (await _db.Table("cats").Select("name").Where("id", 99).Union(_db.Table("dogs").Select("name").Where("id", 99)).ExistsAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Bound_values_of_every_term_are_sent()
    {
        var names = await _db.Table("cats").Select("name").Where("id", 1).Union(_db.Table("dogs").Select("name").Where("id", 2)).OrderBy("name").PluckAsync<string>("name");

        names.Should().Equal("max", "tom");
    }

    [Fact]
    public async Task Keyed_paging_refuses_a_union()
    {
        var union = _db.Table("cats").Select("id").Union(_db.Table("dogs").Select("id"));

        await FluentActions.Invoking(() => union.ChunkByIdAsync(2, _ => Task.FromResult(true))).Should().ThrowAsync<InvalidOperationException>();
        FluentActions.Invoking(() => union.LazyByIdAsync(2)).Should().Throw<InvalidOperationException>();
    }
}
