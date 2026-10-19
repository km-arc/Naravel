using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R21.T04: exists, subquery where and the conditional methods. Pure strings: no database.</summary>
public class QueryExistsWhenTests
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
    public void Exists_and_not_exists_compile_the_callback_as_a_subquery_with_bindings_in_text_order()
    {
        Verify("whereExists", q => q.Where("a", 1).WhereExists(s => s.Table("posts").WhereColumn("posts.user_id", "users.id").Where("p", 2)),
            "select * from «users» where «a» = @p0 and exists (select * from «posts» where «posts».«user_id» = «users».«id» and «p» = @p1)", 1, 2);
        Verify("orWhereExists", q => q.Where("a", 1).OrWhereExists(s => s.Table("t").Where("x", 5)),
            "select * from «users» where «a» = @p0 or exists (select * from «t» where «x» = @p1)", 1, 5);
        Verify("whereNotExists", q => q.WhereNotExists(s => s.Table("t").Where("x", 5)),
            "select * from «users» where not exists (select * from «t» where «x» = @p0)", 5);
        Verify("orWhereNotExists", q => q.Where("a", 1).OrWhereNotExists(s => s.Table("t").Where("x", 5)),
            "select * from «users» where «a» = @p0 or not exists (select * from «t» where «x» = @p1)", 1, 5);
    }

    [Fact]
    public void Subquery_comparison_and_in_closure()
    {
        Verify("whereSubquery", q => q.Where("a", 1).WhereSubquery("votes", ">", s => s.Table("t").Select("max_votes").Where("id", 3)),
            "select * from «users» where «a» = @p0 and «votes» > (select «max_votes» from «t» where «id» = @p1)", 1, 3);
        Verify("orWhereSubquery", q => q.Where("a", 1).OrWhereSubquery("votes", "<=", s => s.Table("t").Select("m")),
            "select * from «users» where «a» = @p0 or «votes» <= (select «m» from «t»)", 1);
        Verify("whereIn closure", q => q.WhereIn("id", s => s.Table("o").Select("user_id").Where("t", 9)),
            "select * from «users» where «id» in (select «user_id» from «o» where «t» = @p0)", 9);
        Verify("whereNotIn closure", q => q.WhereNotIn("id", s => s.Table("o").Select("user_id")),
            "select * from «users» where «id» not in (select «user_id» from «o»)");
        Verify("orWhereIn closure", q => q.Where("a", 1).OrWhereIn("id", s => s.Table("o").Select("user_id")),
            "select * from «users» where «a» = @p0 or «id» in (select «user_id» from «o»)", 1);
        Verify("orWhereNotIn closure", q => q.Where("a", 1).OrWhereNotIn("id", s => s.Table("o").Select("user_id")),
            "select * from «users» where «a» = @p0 or «id» not in (select «user_id» from «o»)", 1);
        Query.For(DatabaseDialect.Sqlite).Table("users").WhereIn("data->owner", s => s.Table("o").Select("name")).Compile().Sql
            .Should().Be("select * from \"users\" where json_extract(\"data\", @p0) in (select \"name\" from \"o\")");
    }

    [Fact]
    public void Deep_nesting_keeps_parameter_numbers_unique_and_in_text_order()
    {
        Verify("nested closures, exists and in", q => q
                .Where("a", 1)
                .Where(g => g.Where("b", 2).OrWhereExists(s => s.Table("t").Where("c", 3).WhereIn("d", s2 => s2.Table("u").Select("e").Where("f", 4)).Where("g", 5)))
                .Where("h", 6),
            "select * from «users» where «a» = @p0 and («b» = @p1 or exists (select * from «t» where «c» = @p2 and «d» in (select «e» from «u» where «f» = @p3) and «g» = @p4)) and «h» = @p5",
            1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void A_subquery_with_limit_uses_the_dialects_own_form()
    {
        Query Build(DatabaseDialect d) => Query.For(d).Table("users").WhereIn("id", s => s.Table("o").Select("user_id").Limit(3));

        Build(DatabaseDialect.Sqlite).Compile().Sql.Should().Be("select * from \"users\" where \"id\" in (select \"user_id\" from \"o\" limit 3)");
        Build(DatabaseDialect.MySql).Compile().Sql.Should().Be("select * from `users` where `id` in (select `user_id` from `o` limit 3)");
        Build(DatabaseDialect.Postgres).Compile().Sql.Should().Be("select * from \"users\" where \"id\" in (select \"user_id\" from \"o\" limit 3)");
        Build(DatabaseDialect.SqlServer).Compile().Sql.Should().Be("select * from [users] where [id] in (select top (3) [user_id] from [o])");
    }

    [Fact]
    public void When_and_unless_apply_the_matching_callback_to_the_same_query()
    {
        Verify("when true", q => q.When(true, g => g.Where("a", 1), g => g.Where("b", 2)), "select * from «users» where «a» = @p0", 1);
        Verify("when false uses otherwise", q => q.When(false, g => g.Where("a", 1), g => g.Where("b", 2)), "select * from «users» where «b» = @p0", 2);
        Verify("when false without otherwise", q => q.When(false, g => g.Where("a", 1)), "select * from «users»");
        Verify("unless false", q => q.Unless(false, g => g.Where("a", 1), g => g.Where("b", 2)), "select * from «users» where «a» = @p0", 1);
        Verify("unless true uses otherwise", q => q.Unless(true, g => g.Where("a", 1), g => g.Where("b", 2)), "select * from «users» where «b» = @p0", 2);
        Verify("when keeps the chain", q => q.Where("z", 0).When(true, g => g.Where("a", 1).OrderBy("a")).Where("y", 9),
            "select * from «users» where «z» = @p0 and «a» = @p1 and «y» = @p2 order by «a» asc", 0, 1, 9);

        var query = Query.For(DatabaseDialect.Sqlite).Table("users");
        query.When(true, _ => { }).Should().BeSameAs(query);
        query.Unless(true, _ => { }).Should().BeSameAs(query);
    }

    [Fact]
    public void A_literal_null_value_still_means_is_null_because_the_closure_forms_have_their_own_names()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", null).OrWhere("b", "<>", null);

        query.Compile().Sql.Should().Be("select * from \"users\" where \"a\" is null or \"b\" is not null");
    }

    [Fact]
    public void Subquery_methods_reject_bad_input_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Postgres).Table("users");

        ((Action)(() => query.WhereExists(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.WhereExists(s => s.Where("a", 1)))).Should().Throw<InvalidOperationException>().WithMessage("*Table*");
        ((Action)(() => query.WhereIn("id", s => { }))).Should().Throw<InvalidOperationException>();
        ((Action)(() => query.WhereSubquery("a", "; drop", s => s.Table("t")))).Should().Throw<ArgumentException>();
        ((Action)(() => query.WhereSubquery("a; drop", "=", s => s.Table("t")))).Should().Throw<ArgumentException>();
        ((Action)(() => query.When(true, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.Unless(true, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Subquery_parameters_count_against_the_dialect_limit()
    {
        var query = Query.For(DatabaseDialect.SqlServer).Table("users")
            .WhereIn("id", Enumerable.Range(1, 2097).Cast<object>())
            .WhereExists(s => s.Table("t").Where("a", 1).Where("b", 2));

        var act = () => query.Compile();

        act.Should().Throw<InvalidOperationException>().WithMessage("*2098*");
    }
}
