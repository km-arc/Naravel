using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>
/// The R21 dialect matrix of docs/en/database.md and docs/fa/database.md, as code: a call a dialect cannot express throws
/// <see cref="NotSupportedException"/> naming the dialect when compiled; every other call compiles on all four. Change this table and the docs together.
/// </summary>
public class DialectMatrixTests
{
    private sealed record Feature(string Name, Func<Query, Query> Build, params DatabaseDialect[] Throws);

    private static readonly Feature[] Matrix =
    [
        new("WhereLike", q => q.WhereLike("name", "a%")),
        new("WhereLike case-sensitive", q => q.WhereLike("name", "a%", caseSensitive: true), DatabaseDialect.Sqlite),
        new("WhereNotLike case-sensitive", q => q.WhereNotLike("name", "a%", caseSensitive: true), DatabaseDialect.Sqlite),
        new("WhereFullText", q => q.WhereFullText("body", "x"), DatabaseDialect.Sqlite),
        new("WhereFullText boolean, several columns", q => q.WhereFullText(["a", "b"], "x", FullTextMode.Boolean), DatabaseDialect.Sqlite),
        new("WhereJsonContains scalar", q => q.WhereJsonContains("data->tags", "x")),
        new("WhereJsonContains array", q => q.WhereJsonContains("data->tags", new[] { "x", "y" }), DatabaseDialect.Sqlite, DatabaseDialect.SqlServer),
        new("WhereJsonDoesntContain object", q => q.WhereJsonDoesntContain("data", new { a = 1 }), DatabaseDialect.Sqlite, DatabaseDialect.SqlServer),
        new("WhereJsonContainsKey", q => q.WhereJsonContainsKey("data->k")),
        new("WhereJsonLength", q => q.WhereJsonLength("data->tags", ">", 1)),
        new("JSON path in where", q => q.Where("data->a->b[0]", 1)),
        new("WhereDate/Month/Day/Year/Time", q => q.WhereDate("c", "2024-01-01").WhereMonth("c", 1).WhereDay("c", 1).WhereYear("c", 2024).WhereTime("c", "08:00")),
        new("WhereAny/All/None", q => q.WhereAny(["a", "b"], 1).WhereAll(["a", "b"], 1).WhereNone(["a", "b"], 1)),
        new("WhereBetween/Null/Column", q => q.WhereBetween("a", 1, 2).WhereNull("b").WhereColumn("c", "d")),
        new("WhereExists and subqueries", q => q.WhereExists(s => s.Table("t")).WhereIn("id", s => s.Table("t").Select("id")).WhereSubquery("a", ">", s => s.Table("t").Select("a"))),
        new("When/Unless", q => q.When(true, g => g.Where("a", 1)).Unless(false, g => g.Where("b", 1))),
        new("Join, LeftJoin, CrossJoin", q => q.Join("p", "users.id", "p.uid").LeftJoin("q", "users.id", "q.uid").CrossJoin("r")),
        new("RightJoin", q => q.RightJoin("p", "users.id", "p.uid")),
        new("GroupBy and Having", q => q.GroupBy("a").Having("a", ">", 1).HavingBetween("a", 1, 2).HavingNull("b")),
        new("Latest, Oldest, Reorder", q => q.Latest().Oldest("id").Reorder("x")),
        new("InRandomOrder", q => q.InRandomOrder()),
    ];

    public static IEnumerable<object[]> Cases() =>
        from feature in Matrix
        from dialect in Enum.GetValues<DatabaseDialect>()
        select new object[] { feature.Name, dialect };

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_call_compiles_or_throws_NotSupported_naming_the_dialect_exactly_as_the_matrix_says(string name, DatabaseDialect dialect)
    {
        var feature = Matrix.Single(f => f.Name == name);
        var query = feature.Build(Query.For(dialect).Table("users"));

        var act = () => query.Compile();

        if (feature.Throws.Contains(dialect)) act.Should().Throw<NotSupportedException>().WithMessage($"*{dialect}*");
        else act.Should().NotThrow().Which.Sql.Should().StartWith("select ");
    }

    [Fact]
    public void Every_dialect_is_covered_by_the_matrix()
    {
        Cases().Select(c => (DatabaseDialect)c[1]).Distinct().Should().BeEquivalentTo(Enum.GetValues<DatabaseDialect>());
        Matrix.Select(f => f.Name).Should().OnlyHaveUniqueItems();
    }
}
