using Naravel.Database;
using Naravel.Database.Testing;

namespace Naravel.Database.Tests;

/// <summary>R24.T06: ToSql, GetBindings and ToRawSql are for looking, never for running.</summary>
public class QueryDebugTests
{
    private static Query Users(DatabaseDialect d) => Query.For(d).Table("users");

    [Fact]
    public void ToSql_and_GetBindings_are_what_compile_returns_and_change_nothing()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var query = Users(dialect).Where("a", 1).Union(Query.For(dialect).Table("b").Where("c", "x")).OrderBy("a").Limit(5);
            var before = query.Compile();

            query.ToSql().Should().Be(before.Sql);
            query.GetBindings().Should().Equal(before.Bindings);
            query.ToSql().Should().Be(before.Sql, "a second call gives the same text");
            query.ToRawSql().Should().NotBeNullOrEmpty();
            query.Compile().Sql.Should().Be(before.Sql, "nothing on the query changed");
        }
    }

    [Fact]
    public void ToRawSql_writes_every_kind_of_value_as_a_literal()
    {
        var offset = new DateTimeOffset(2026, 10, 17, 9, 30, 0, TimeSpan.FromHours(3.5));
        var raw = Users(DatabaseDialect.Sqlite)
            .WhereRaw("c1 = ?", 'x').WhereRaw("c2 = ?", 7L).WhereRaw("c3 = ?", 1.25m).WhereRaw("c4 = ?", 0.5).WhereRaw("c5 = ?", (object?)null)
            .WhereRaw("c6 = ?", offset).WhereRaw("c7 = ?", new DateOnly(2026, 1, 2)).WhereRaw("c8 = ?", new TimeOnly(3, 4, 5)).WhereRaw("c9 = ?", DayOfWeek.Friday).WhereRaw("c10 = ?", false)
            .ToRawSql();

        raw.Should().Be("select * from \"users\" where (c1 = 'x') and (c2 = 7) and (c3 = 1.25) and (c4 = 0.5) and (c5 = NULL) and (c6 = '2026-10-17 09:30:00.000000+03:30') and (c7 = '2026-01-02') and (c8 = '03:04:05.000000') and (c9 = 5) and (c10 = 0)");
    }

    [Fact]
    public void Markers_with_two_digits_are_replaced_whole()
    {
        var query = Users(DatabaseDialect.Sqlite);
        for (var i = 0; i < 12; i++) query.Where("c" + i, i);

        var raw = query.ToRawSql();

        raw.Should().Contain("\"c1\" = 1 and").And.Contain("\"c10\" = 10 and").And.Contain("\"c11\" = 11");
        raw.Should().NotContain("@p");
    }

    [Fact]
    public void ToRawSql_follows_unions_expressions_subqueries_and_raw_values()
    {
        var sql = Query.For(DatabaseDialect.Postgres).Table("big")
            .WithExpression("big", Query.For(DatabaseDialect.Postgres).Table("u").Where("x", "o'neil"))
            .SelectSub(Query.For(DatabaseDialect.Postgres).Table("p").Select("n").Where("k", true), "pk")
            .Where("votes", ">", new Raw("avg_votes + ?", 2))
            .Union(Query.For(DatabaseDialect.Postgres).Table("other").Where("y", 9))
            .ToRawSql();

        sql.Should().Be("with \"big\" as (select * from \"u\" where \"x\" = 'o''neil') select (select \"n\" from \"p\" where \"k\" = true) as \"pk\" from \"big\" where \"votes\" > avg_votes + 2 union select * from \"other\" where \"y\" = 9");
    }

    [Fact]
    public void Strings_are_escaped_for_each_dialect()
    {
        var attack = "a'b\\c";

        Users(DatabaseDialect.Sqlite).Where("s", attack).ToRawSql().Should().EndWith("= 'a''b\\c'");
        Users(DatabaseDialect.Postgres).Where("s", attack).ToRawSql().Should().EndWith("= 'a''b\\c'");
        Users(DatabaseDialect.MySql).Where("s", attack).ToRawSql().Should().EndWith("= 'a''b\\\\c'", "MySQL reads a backslash as an escape");
        Users(DatabaseDialect.SqlServer).Where("s", attack).ToRawSql().Should().EndWith("= N'a''b\\c'");
    }

    [Fact]
    public async Task What_runs_is_the_parameterised_text_and_never_the_raw_form()
    {
        var fake = new DatabaseFake(DatabaseDialect.SqlServer);
        var query = fake.Table("users").Where("name", "x'; drop table users; --").Where("votes", ">", 3).OrderBy("id").Limit(2);

        await query.GetAsync();

        var executed = fake.AssertExecuted();
        executed.Sql.Should().Be(query.ToSql());
        executed.Bindings.Should().Equal(query.GetBindings());
        executed.Sql.Should().NotContain("drop table").And.Contain("@p0");
        query.ToRawSql().Should().Contain("drop table", "the display form shows the value, and is never executed");
    }

    [Fact]
    public async Task Writes_are_shown_through_the_query_log_not_through_ToSql()
    {
        var fake = new DatabaseFake();

        await fake.Table("users").Where("id", 1).UpdateAsync(new Dictionary<string, object?> { ["name"] = "n" });

        fake.AssertExecuted(e => e.Operation == QueryOperation.Statement).Sql.Should().Be("update \"users\" set \"name\" = @p0 where \"id\" = @p1");
    }

    [Theory]
    [InlineData("a = @p0")]
    [InlineData("a = @p12 or b = 1")]
    [InlineData("x@p1.example")]
    public void Raw_text_may_not_contain_the_builders_own_parameter_names(string sql)
    {
        var query = Users(DatabaseDialect.Sqlite);

        FluentActions.Invoking(() => query.WhereRaw(sql)).Should().Throw<ArgumentException>().WithMessage("*@p*");
        FluentActions.Invoking(() => query.SelectRaw(sql)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.OrderByRaw(sql)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.GroupByRaw(sql)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.HavingRaw(sql)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.FromRaw(sql)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Raw(sql)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Other_at_names_in_raw_text_are_fine()
    {
        Users(DatabaseDialect.SqlServer).WhereRaw("a = @param and b = @pending").ToSql().Should().Contain("@param").And.Contain("@pending");
    }

    [Fact]
    public void Dump_and_dd_are_not_ported()
    {
        typeof(Query).GetMethods().Select(m => m.Name).Should().NotContain(name => name.StartsWith("Dump", StringComparison.Ordinal) || name == "Dd" || name == "DD");
    }
}
