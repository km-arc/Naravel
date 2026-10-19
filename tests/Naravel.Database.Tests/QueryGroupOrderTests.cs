using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R21.T06: group by, having and ordering helpers. Pure strings: no database.</summary>
public class QueryGroupOrderTests
{
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static void Verify(string name, Func<Query, Query> build, string sql, params object?[] bindings)
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = build(Query.For(dialect).Table("users")).Compile();

            compiled.Sql.Should().Be(Quoted(sql, dialect), $"case '{name}' on {dialect}");
            compiled.Bindings.Should().Equal(bindings, $"case '{name}' bindings on {dialect}");
        }
    }

    [Fact]
    public void Group_by_and_having_compile_between_where_and_order_by()
    {
        Verify("groupBy", q => q.GroupBy("a"), "select * from «users» group by «a»");
        Verify("groupBy several and qualified", q => q.GroupBy("users.a", "b"), "select * from «users» group by «users».«a», «b»");
        Verify("groupBy calls accumulate", q => q.GroupBy("a").GroupBy("b"), "select * from «users» group by «a», «b»");
        Verify("having", q => q.GroupBy("a").Having("a", 1), "select * from «users» group by «a» having «a» = @p0", 1);
        Verify("having operator", q => q.GroupBy("a").Having("a", ">", 2), "select * from «users» group by «a» having «a» > @p0", 2);
        Verify("orHaving", q => q.GroupBy("a").Having("a", 1).OrHaving("a", "<", 0).OrHaving("b", 7),
            "select * from «users» group by «a» having «a» = @p0 or «a» < @p1 or «b» = @p2", 1, 0, 7);
        Verify("havingBetween", q => q.GroupBy("a").HavingBetween("a", 1, 5).OrHavingBetween("b", 8, 9),
            "select * from «users» group by «a» having «a» between @p0 and @p1 or «b» between @p2 and @p3", 1, 5, 8, 9);
        Verify("havingNull family", q => q.GroupBy("a").HavingNull("a").HavingNotNull("b").OrHavingNull("c").OrHavingNotNull("d"),
            "select * from «users» group by «a» having «a» is null and «b» is not null or «c» is null or «d» is not null");
        Verify("having null value means is null", q => q.GroupBy("a").Having("a", null), "select * from «users» group by «a» having «a» is null");
        Verify("full statement order and binding order",
            q => q.Select("a").Where("x", 1).GroupBy("a").Having("a", ">", 2).OrderBy("a"),
            "select «a» from «users» where «x» = @p0 group by «a» having «a» > @p1 order by «a» asc", 1, 2);
    }

    [Fact]
    public void Group_by_and_having_follow_joins_and_survive_clone()
    {
        Verify("join then group then having", q => q.Join("posts", "users.id", "posts.user_id").GroupBy("users.id").Having("users.id", ">", 1),
            "select * from «users» inner join «posts» on «users».«id» = «posts».«user_id» group by «users».«id» having «users».«id» > @p0", 1);

        var original = Query.For(DatabaseDialect.Sqlite).Table("users").GroupBy("a").Having("a", 1);
        var copy = original.Clone().GroupBy("b").OrHaving("b", 2);

        original.ToSql().Should().Be("select * from \"users\" group by \"a\" having \"a\" = @p0");
        copy.ToSql().Should().Be("select * from \"users\" group by \"a\", \"b\" having \"a\" = @p0 or \"b\" = @p1");
    }

    [Fact]
    public void Latest_oldest_and_reorder_change_the_order_by_list()
    {
        Verify("latest default", q => q.Latest(), "select * from «users» order by «created_at» desc");
        Verify("latest column", q => q.Latest("id"), "select * from «users» order by «id» desc");
        Verify("oldest default", q => q.Oldest(), "select * from «users» order by «created_at» asc");
        Verify("oldest column then latest", q => q.Oldest("a").Latest("b"), "select * from «users» order by «a» asc, «b» desc");
        Verify("reorder clears", q => q.OrderBy("a").OrderByDesc("b").Reorder(), "select * from «users»");
        Verify("reorder column", q => q.OrderBy("a").Reorder("c"), "select * from «users» order by «c» asc");
        Verify("reorderDesc column", q => q.OrderBy("a").ReorderDesc("c"), "select * from «users» order by «c» desc");
        Verify("reorder keeps later orders", q => q.OrderBy("a").Reorder().OrderBy("z"), "select * from «users» order by «z» asc");
    }

    [Fact]
    public void Random_order_uses_each_engines_own_function()
    {
        Query.For(DatabaseDialect.Sqlite).Table("users").InRandomOrder().ToSql().Should().Be("select * from \"users\" order by random()");
        Query.For(DatabaseDialect.MySql).Table("users").InRandomOrder().ToSql().Should().Be("select * from `users` order by rand()");
        Query.For(DatabaseDialect.Postgres).Table("users").InRandomOrder().ToSql().Should().Be("select * from \"users\" order by random()");
        Query.For(DatabaseDialect.SqlServer).Table("users").InRandomOrder().ToSql().Should().Be("select * from [users] order by newid()");
        Query.For(DatabaseDialect.Sqlite).Table("users").OrderBy("a").InRandomOrder().Limit(1).ToSql().Should().Be("select * from \"users\" order by \"a\" asc, random() limit 1");
        Query.For(DatabaseDialect.SqlServer).Table("users").InRandomOrder().Limit(1).ToSql().Should().Be("select top (1) * from [users] order by newid()");
    }

    [Fact]
    public void Group_having_and_ordering_helpers_reject_bad_input_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        ((Action)(() => query.GroupBy())).Should().Throw<ArgumentException>();
        ((Action)(() => query.GroupBy(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.GroupBy("a; drop"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.GroupBy("*"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.GroupBy("data->x"))).Should().Throw<NotSupportedException>();
        ((Action)(() => query.Having("a; drop", 1))).Should().Throw<ArgumentException>();
        ((Action)(() => query.Having("a", "; drop", 1))).Should().Throw<ArgumentException>();
        ((Action)(() => query.HavingBetween("a", null!, 2))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.Latest("a b"))).Should().Throw<ArgumentException>();

        var ordered = Query.For(DatabaseDialect.Sqlite).Table("users").OrderBy("a");
        ((Action)(() => ordered.Reorder("bad column"))).Should().Throw<ArgumentException>();
        ordered.ToSql().Should().Be("select * from \"users\" order by \"a\" asc", "a rejected Reorder must not drop the existing ordering");
        ((Action)(() => ordered.ReorderDesc("data->x"))).Should().Throw<NotSupportedException>();
        ordered.ToSql().Should().Contain("order by \"a\" asc");
    }

    [Fact]
    public void Having_parameters_count_against_the_dialect_limit()
    {
        var query = Query.For(DatabaseDialect.SqlServer).Table("users")
            .WhereIn("id", Enumerable.Range(1, 2097).Cast<object>())
            .GroupBy("a").Having("a", 1).Having("a", 2);

        var act = () => query.Compile();

        act.Should().Throw<InvalidOperationException>().WithMessage("*2098*");
    }
}
