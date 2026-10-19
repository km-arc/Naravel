using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R21.T05: joins. Pure strings: no database.</summary>
public class QueryJoinTests
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
    public void Simple_joins_compile_to_inner_left_right_and_cross()
    {
        Verify("join", q => q.Join("posts", "users.id", "posts.user_id"),
            "select * from «users» inner join «posts» on «users».«id» = «posts».«user_id»");
        Verify("join operator", q => q.Join("posts", "users.id", "<>", "posts.user_id"),
            "select * from «users» inner join «posts» on «users».«id» <> «posts».«user_id»");
        Verify("leftJoin alias", q => q.LeftJoin("posts as p", "users.id", "p.user_id"),
            "select * from «users» left join «posts» as «p» on «users».«id» = «p».«user_id»");
        Verify("rightJoin", q => q.RightJoin("posts", "users.id", ">=", "posts.user_id"),
            "select * from «users» right join «posts» on «users».«id» >= «posts».«user_id»");
        Verify("crossJoin", q => q.CrossJoin("sizes"), "select * from «users» cross join «sizes»");
        Verify("schema qualified table", q => q.Join("app.posts", "users.id", "app.posts.user_id"),
            "select * from «users» inner join «app».«posts» on «users».«id» = «app».«posts».«user_id»");
    }

    [Fact]
    public void Join_closure_builds_on_or_on_and_value_conditions()
    {
        Verify("closure", q => q.Join("posts", j => j
                .On("users.id", "posts.user_id")
                .OrOn("users.alt", "posts.user_id")
                .Where("posts.active", true)
                .OrWhere("posts.n", ">", 5)
                .WhereNull("posts.d")
                .WhereNotNull("posts.e")
                .WhereIn("posts.k", new[] { 1, 2 })),
            "select * from «users» inner join «posts» on «users».«id» = «posts».«user_id» or «users».«alt» = «posts».«user_id» and «posts».«active» = @p0 or «posts».«n» > @p1 and «posts».«d» is null and «posts».«e» is not null and «posts».«k» in (@p2, @p3)",
            true, 5, 1, 2);
        Verify("closure on with operator", q => q.LeftJoin("posts", j => j.On("users.id", "<", "posts.user_id").OrOn("users.a", "<>", "posts.b").Where("posts.t", null).OrWhere("posts.u", "like", "x%")),
            "select * from «users» left join «posts» on «users».«id» < «posts».«user_id» or «users».«a» <> «posts».«b» and «posts».«t» is null or «posts».«u» like @p0", "x%");
    }

    [Fact]
    public void Join_values_are_bound_before_where_values_whatever_the_call_order()
    {
        Verify("binding order", q => q.Where("a", 1).Join("p", j => j.On("users.id", "p.uid").Where("x", 2)).Where("b", 3),
            "select * from «users» inner join «p» on «users».«id» = «p».«uid» and «x» = @p0 where «a» = @p1 and «b» = @p2", 2, 1, 3);
        Verify("several joins keep call order", q => q
                .Select("users.name", "posts.title")
                .Join("posts", j => j.On("users.id", "posts.user_id").Where("posts.live", true))
                .LeftJoin("tags", j => j.On("tags.post_id", "posts.id").Where("tags.kind", "a"))
                .CrossJoin("sizes")
                .Where("users.age", ">", 18)
                .OrderBy("users.name"),
            "select «users».«name», «posts».«title» from «users» inner join «posts» on «users».«id» = «posts».«user_id» and «posts».«live» = @p0 left join «tags» on «tags».«post_id» = «posts».«id» and «tags».«kind» = @p1 cross join «sizes» where «users».«age» > @p2 order by «users».«name» asc",
            true, "a", 18);
    }

    [Fact]
    public void Joins_work_inside_subqueries_and_survive_clone()
    {
        Verify("join inside exists", q => q.WhereExists(s => s.Table("a").Join("b", "a.id", "b.aid").Where("b.k", 7)),
            "select * from «users» where exists (select * from «a» inner join «b» on «a».«id» = «b».«aid» where «b».«k» = @p0)", 7);

        var original = Query.For(DatabaseDialect.Postgres).Table("users").Join("posts", "users.id", "posts.user_id");
        var copy = original.Clone().LeftJoin("tags", "tags.uid", "users.id");

        original.ToSql().Should().Be("select * from \"users\" inner join \"posts\" on \"users\".\"id\" = \"posts\".\"user_id\"");
        copy.ToSql().Should().Be("select * from \"users\" inner join \"posts\" on \"users\".\"id\" = \"posts\".\"user_id\" left join \"tags\" on \"tags\".\"uid\" = \"users\".\"id\"");
    }

    [Fact]
    public void Join_methods_reject_bad_input_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        ((Action)(() => query.Join("posts", j => { }))).Should().Throw<InvalidOperationException>().WithMessage("*condition*");
        ((Action)(() => query.Join("posts", (Action<JoinClause>)null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.Join("posts; drop", "a.id", "b.id"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.Join("posts", "a.id; drop", "b.id"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.Join("posts", "a.id", "; drop", "b.id"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.Join("posts", j => j.Where("a;b", 1)))).Should().Throw<ArgumentException>();
        ((Action)(() => query.Join("data->x", "a.id", "b.id"))).Should().Throw<NotSupportedException>();
        ((Action)(() => query.CrossJoin("*"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.LeftJoin("posts as", "a.id", "b.id"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Join_parameters_count_against_the_dialect_limit()
    {
        var query = Query.For(DatabaseDialect.SqlServer).Table("users")
            .WhereIn("id", Enumerable.Range(1, 2097).Cast<object>())
            .Join("posts", j => j.On("users.id", "posts.user_id").Where("a", 1).Where("b", 2));

        var act = () => query.Compile();

        act.Should().Throw<InvalidOperationException>().WithMessage("*2098*");
    }
}
