using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R22.T01: aggregate and existence SQL on every grammar. Pure strings: no database.</summary>
public class QueryAggregateTests
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
    public void Plain_aggregates_replace_the_columns_and_drop_the_ordering()
    {
        Verify("count", q => q.CompileAggregate("count", null), "select count(*) as «aggregate» from «users»");
        Verify("count star", q => q.CompileAggregate("count", "*"), "select count(*) as «aggregate» from «users»");
        Verify("count column", q => q.CompileAggregate("count", "note"), "select count(«note») as «aggregate» from «users»");
        Verify("count qualified column", q => q.CompileAggregate("count", "users.note"), "select count(«users».«note») as «aggregate» from «users»");
        Verify("sum", q => q.CompileAggregate("sum", "votes"), "select sum(«votes») as «aggregate» from «users»");
        Verify("avg", q => q.CompileAggregate("avg", "votes"), "select avg(«votes») as «aggregate» from «users»");
        Verify("min and max", q => q.CompileAggregate("min", "votes"), "select min(«votes») as «aggregate» from «users»");
        Verify("selected columns are replaced", q => q.Select("a", "b").CompileAggregate("count", null), "select count(*) as «aggregate» from «users»");
        Verify("where and bindings are kept", q => q.Where("a", 1).WhereIn("b", new[] { 2, 3 }).CompileAggregate("sum", "votes"),
            "select sum(«votes») as «aggregate» from «users» where «a» = @p0 and «b» in (@p1, @p2)", 1, 2, 3);
        Verify("order by is dropped", q => q.OrderBy("id").CompileAggregate("count", null), "select count(*) as «aggregate» from «users»");
        Verify("joins are kept", q => q.Join("posts", "posts.user_id", "users.id").CompileAggregate("count", null),
            "select count(*) as «aggregate» from «users» inner join «posts» on «posts».«user_id» = «users».«id»");
    }

    [Fact]
    public void Distinct_with_a_column_counts_distinct_values()
    {
        Verify("distinct column", q => q.Distinct().CompileAggregate("count", "votes"), "select count(distinct «votes») as «aggregate» from «users»");
        Verify("distinct sum", q => q.Distinct().CompileAggregate("sum", "votes"), "select sum(distinct «votes») as «aggregate» from «users»");
    }

    [Fact]
    public void Grouped_distinct_and_having_queries_are_counted_as_a_derived_table()
    {
        Verify("group by", q => q.Select("votes").GroupBy("votes").CompileAggregate("count", null),
            "select count(*) as «aggregate» from (select «votes» from «users» group by «votes») as «naravel_aggregate»");
        Verify("having keeps bindings", q => q.Select("votes").GroupBy("votes").Having("votes", ">", 5).CompileAggregate("count", null),
            "select count(*) as «aggregate» from (select «votes» from «users» group by «votes» having «votes» > @p0) as «naravel_aggregate»", 5);
        Verify("distinct without a column", q => q.Select("votes").Distinct().CompileAggregate("count", null),
            "select count(*) as «aggregate» from (select distinct «votes» from «users») as «naravel_aggregate»");
        Verify("order by dropped inside the derived table", q => q.Select("votes").GroupBy("votes").OrderBy("votes").CompileAggregate("count", null),
            "select count(*) as «aggregate» from (select «votes» from «users» group by «votes») as «naravel_aggregate»");
        Verify("column of a derived table is read by its last segment", q => q.Select("users.votes").GroupBy("users.votes").CompileAggregate("max", "users.votes"),
            "select max(«votes») as «aggregate» from (select «users».«votes» from «users» group by «users».«votes») as «naravel_aggregate»");
    }

    [Fact]
    public void A_limit_or_offset_is_counted_as_a_derived_table_and_keeps_its_ordering()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var limited = Query.For(dialect).Table("users").OrderBy("id").Limit(5).CompileAggregate("count", null).Sql;
            limited.Should().StartWith(Quoted("select count(*) as «aggregate» from (select ", dialect), dialect.ToString());
            limited.Should().EndWith(Quoted(") as «naravel_aggregate»", dialect), dialect.ToString());
            limited.Should().Contain("order by", $"the ordering decides which rows the limit keeps ({dialect})");
        }
    }

    [Fact]
    public void Exists_wraps_the_query_in_a_portable_case_expression()
    {
        Verify("exists", q => q.CompileExists(), "select case when exists (select 1 from «users») then 1 else 0 end as «exists»");
        Verify("exists keeps where and bindings", q => q.Where("a", 1).OrWhere("b", 2).CompileExists(),
            "select case when exists (select 1 from «users» where «a» = @p0 or «b» = @p1) then 1 else 0 end as «exists»", 1, 2);
        Verify("exists drops columns and order", q => q.Select("a", "b").OrderBy("a").CompileExists(),
            "select case when exists (select 1 from «users») then 1 else 0 end as «exists»");
        Verify("exists ignores distinct", q => q.Distinct().CompileExists(), "select case when exists (select 1 from «users») then 1 else 0 end as «exists»");
        Verify("exists over groups", q => q.GroupBy("votes").Having("votes", ">", 5).CompileExists(),
            "select case when exists (select 1 from «users» group by «votes» having «votes» > @p0) then 1 else 0 end as «exists»", 5);
    }

    [Fact]
    public async Task Bad_arguments_fail_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        FluentActions.Invoking(() => query.CompileAggregate("sum", null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileAggregate("sum", "*")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileAggregate("sum", "votes as v")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileAggregate("sum", "votes; drop table users")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileAggregate("count", "users.*")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileAggregate("max", "data->name")).Should().Throw<NotSupportedException>();
        await FluentActions.Awaiting(() => query.MinAsync<int>(null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => query.CountAsync()).Should().ThrowAsync<InvalidOperationException>("a query made with For is not connected");
    }
}
