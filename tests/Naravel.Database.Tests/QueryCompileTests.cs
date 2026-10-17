using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>Expected SQL and bindings for every builder method on all four dialect grammars. Pure strings: no database.</summary>
public class QueryCompileTests
{
    private sealed record Case(string Name, Func<Query, Query> Build, string Sql, object?[] Bindings, Dictionary<DatabaseDialect, string>? Overrides = null);

    private static readonly DatabaseDialect[] AllDialects = Enum.GetValues<DatabaseDialect>();

    /// <summary>Writes a template with «name» identifiers in the quoting style of the dialect.</summary>
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static void Verify(IEnumerable<Case> cases)
    {
        foreach (var testCase in cases)
        {
            foreach (var dialect in AllDialects)
            {
                var template = testCase.Overrides is not null && testCase.Overrides.TryGetValue(dialect, out var special) ? special : testCase.Sql;
                var compiled = testCase.Build(Query.For(dialect)).Compile();

                compiled.Sql.Should().Be(Quoted(template, dialect), $"case '{testCase.Name}' on {dialect}");
                compiled.Bindings.Should().Equal(testCase.Bindings, $"case '{testCase.Name}' bindings on {dialect}");
            }
        }
    }

    private static Query Users(Query q) => q.Table("users");

    [Fact]
    public void Table_and_select_methods()
    {
        Verify(
        [
            new("table only", q => Users(q), "select * from «users»", []),
            new("schema table", q => q.Table("app.users"), "select * from «app».«users»", []),
            new("table alias", q => q.Table("users as u"), "select * from «users» as «u»", []),
            new("select columns", q => Users(q).Select("id", "name"), "select «id», «name» from «users»", []),
            new("select replaces", q => Users(q).Select("id").Select("email"), "select «email» from «users»", []),
            new("select none is star", q => Users(q).Select(), "select * from «users»", []),
            new("select star and table star", q => Users(q).Select("users.*"), "select «users».* from «users»", []),
            new("select alias", q => Users(q).Select("name as n", "u.id AS uid"), "select «name» as «n», «u».«id» as «uid» from «users»", []),
            new("addSelect", q => Users(q).Select("id").AddSelect("name", "email"), "select «id», «name», «email» from «users»", []),
            new("distinct", q => Users(q).Distinct().Select("city"), "select distinct «city» from «users»", []),
            new("selectRaw", q => Users(q).SelectRaw("count(*) as c"), "select count(*) as c from «users»", []),
            new("selectRaw bindings", q => Users(q).Select("id").SelectRaw("? + ? as total", 1, 2), "select «id», @p0 + @p1 as total from «users»", [1, 2]),
            new("raw escaped question mark", q => Users(q).SelectRaw("data ?? 'k' as has_key"), "select data ? 'k' as has_key from «users»", []),
            new("persian identifiers", q => q.Table("کاربران").Where("نام", "علی"), "select * from «کاربران» where «نام» = @p0", ["علی"]),
        ]);
    }

    [Fact]
    public void Basic_where_methods()
    {
        Verify(
        [
            new("where equals", q => Users(q).Where("a", 1), "select * from «users» where «a» = @p0", [1]),
            new("where operator", q => Users(q).Where("age", ">=", 18), "select * from «users» where «age» >= @p0", [18]),
            new("operator is normalised", q => Users(q).Where("n", "  NOT   LIKE ", "x%"), "select * from «users» where «n» not like @p0", ["x%"]),
            new("not equal bang", q => Users(q).Where("a", "!=", 1), "select * from «users» where «a» != @p0", [1]),
            new("where null", q => Users(q).Where("a", null), "select * from «users» where «a» is null", []),
            new("where not-equal null", q => Users(q).Where("a", "<>", null), "select * from «users» where «a» is not null", []),
            new("and chain", q => Users(q).Where("a", 1).Where("b", 2), "select * from «users» where «a» = @p0 and «b» = @p1", [1, 2]),
            new("orWhere", q => Users(q).Where("a", 1).OrWhere("b", 2), "select * from «users» where «a» = @p0 or «b» = @p1", [1, 2]),
            new("orWhere operator", q => Users(q).Where("a", 1).OrWhere("b", "<", 2), "select * from «users» where «a» = @p0 or «b» < @p1", [1, 2]),
            new("table-qualified column", q => Users(q).Where("users.id", 7), "select * from «users» where «users».«id» = @p0", [7]),
            new("array form", q => Users(q).Where(new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x" }), "select * from «users» where («a» = @p0 and «b» = @p1)", [1, "x"]),
            new("orWhere array form", q => Users(q).Where("z", 0).OrWhere(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 }), "select * from «users» where «z» = @p0 or («a» = @p1 and «b» = @p2)", [0, 1, 2]),
            new("empty array form adds nothing", q => Users(q).Where(new Dictionary<string, object?>()), "select * from «users»", []),
            new("closure group", q => Users(q).Where("a", 1).Where(g => g.Where("b", 2).OrWhere("c", 3)), "select * from «users» where «a» = @p0 and («b» = @p1 or «c» = @p2)", [1, 2, 3]),
            new("orWhere closure group", q => Users(q).Where("a", 1).OrWhere(g => g.Where("b", 2).Where("c", 3)), "select * from «users» where «a» = @p0 or («b» = @p1 and «c» = @p2)", [1, 2, 3]),
            new("nested closure groups", q => Users(q).Where(g => g.Where(h => h.Where("a", 1).OrWhere("b", 2)).Where("c", 3)), "select * from «users» where ((«a» = @p0 or «b» = @p1) and «c» = @p2)", [1, 2, 3]),
            new("empty closure adds nothing", q => Users(q).Where("a", 1).Where(_ => { }), "select * from «users» where «a» = @p0", [1]),
            new("whereNot", q => Users(q).WhereNot("a", 1), "select * from «users» where not «a» = @p0", [1]),
            new("whereNot operator", q => Users(q).WhereNot("a", ">", 1), "select * from «users» where not «a» > @p0", [1]),
            new("orWhereNot", q => Users(q).Where("a", 1).OrWhereNot("b", 2), "select * from «users» where «a» = @p0 or not «b» = @p1", [1, 2]),
            new("orWhereNot operator", q => Users(q).Where("a", 1).OrWhereNot("b", "<", 2), "select * from «users» where «a» = @p0 or not «b» < @p1", [1, 2]),
            new("whereNot closure", q => Users(q).WhereNot(g => g.Where("a", 1).OrWhere("b", 2)), "select * from «users» where not («a» = @p0 or «b» = @p1)", [1, 2]),
            new("orWhereNot closure", q => Users(q).Where("z", 0).OrWhereNot(g => g.Where("a", 1)), "select * from «users» where «z» = @p0 or not («a» = @p1)", [0, 1]),
            new("whereRaw", q => Users(q).WhereRaw("age > ? and age < ?", 18, 65), "select * from «users» where (age > @p0 and age < @p1)", [18, 65]),
            new("orWhereRaw", q => Users(q).Where("a", 1).OrWhereRaw("b = ?", 2), "select * from «users» where «a» = @p0 or (b = @p1)", [1, 2]),
            new("whereIn", q => Users(q).WhereIn("id", new[] { 1, 2, 3 }), "select * from «users» where «id» in (@p0, @p1, @p2)", [1, 2, 3]),
            new("whereNotIn", q => Users(q).WhereNotIn("id", new List<long> { 4, 5 }), "select * from «users» where «id» not in (@p0, @p1)", [4L, 5L]),
            new("orWhereIn", q => Users(q).Where("a", 1).OrWhereIn("id", new[] { 9 }), "select * from «users» where «a» = @p0 or «id» in (@p1)", [1, 9]),
            new("orWhereNotIn", q => Users(q).Where("a", 1).OrWhereNotIn("id", new[] { 9 }), "select * from «users» where «a» = @p0 or «id» not in (@p1)", [1, 9]),
            new("whereIn empty matches nothing", q => Users(q).WhereIn("id", Array.Empty<int>()), "select * from «users» where 0 = 1", []),
            new("whereNotIn empty matches everything", q => Users(q).WhereNotIn("id", Array.Empty<int>()), "select * from «users» where 1 = 1", []),
        ]);
    }

    [Fact]
    public void Ordering_limit_and_offset_methods()
    {
        var limitOffsetStandard = "select * from «users» order by «id» asc limit 10 offset 5";
        Verify(
        [
            new("orderBy", q => Users(q).OrderBy("name"), "select * from «users» order by «name» asc", []),
            new("orderBy desc any case", q => Users(q).OrderBy("name", "DESC"), "select * from «users» order by «name» desc", []),
            new("orderByDesc and chain", q => Users(q).OrderBy("a").OrderByDesc("b"), "select * from «users» order by «a» asc, «b» desc", []),
            new("orderByRaw", q => Users(q).OrderBy("a").OrderByRaw("field(x, ?, ?)", 1, 2), "select * from «users» order by «a» asc, field(x, @p0, @p1)", [1, 2]),
            new("limit", q => Users(q).Limit(10), "select * from «users» limit 10", [], new() { [DatabaseDialect.SqlServer] = "select top (10) * from «users»" }),
            new("take", q => Users(q).Take(3), "select * from «users» limit 3", [], new() { [DatabaseDialect.SqlServer] = "select top (3) * from «users»" }),
            new("limit zero", q => Users(q).Limit(0), "select * from «users» limit 0", [], new() { [DatabaseDialect.SqlServer] = "select top (0) * from «users»" }),
            new("distinct limit", q => Users(q).Distinct().Limit(3), "select distinct * from «users» limit 3", [], new() { [DatabaseDialect.SqlServer] = "select distinct top (3) * from «users»" }),
            new("limit and offset", q => Users(q).OrderBy("id").Limit(10).Offset(5), limitOffsetStandard, [], new() { [DatabaseDialect.SqlServer] = "select * from «users» order by «id» asc offset 5 rows fetch next 10 rows only" }),
            new("take and skip", q => Users(q).OrderBy("id").Take(10).Skip(5), limitOffsetStandard, [], new() { [DatabaseDialect.SqlServer] = "select * from «users» order by «id» asc offset 5 rows fetch next 10 rows only" }),
            new("offset zero with limit", q => Users(q).OrderBy("id").Limit(10).Offset(0), "select * from «users» order by «id» asc limit 10 offset 0", [], new() { [DatabaseDialect.SqlServer] = "select top (10) * from «users» order by «id» asc" }),
            new("offset only", q => Users(q).OrderBy("id").Offset(5), "select * from «users» order by «id» asc limit -1 offset 5", [], new()
            {
                [DatabaseDialect.MySql] = "select * from «users» order by «id» asc limit 18446744073709551615 offset 5",
                [DatabaseDialect.Postgres] = "select * from «users» order by «id» asc offset 5",
                [DatabaseDialect.SqlServer] = "select * from «users» order by «id» asc offset 5 rows",
            }),
        ]);
    }

    [Fact]
    public void A_combined_query_keeps_binding_order_in_text_order()
    {
        Verify(
        [
            new(
                "combined",
                q => q.Table("orders as o").Select("o.id", "o.total").SelectRaw("? as tag", "x").Where("o.status", "paid").Where(g => g.Where("o.total", ">", 100).OrWhereRaw("o.vip = ?", true)).OrderByRaw("o.id % ?", 7).OrderByDesc("o.total"),
                "select «o».«id», «o».«total», @p0 as tag from «orders» as «o» where «o».«status» = @p1 and («o».«total» > @p2 or (o.vip = @p3)) order by o.id % @p4, «o».«total» desc",
                ["x", "paid", 100, true, 7]),
        ]);
    }

    [Fact]
    public void SqlServer_offset_without_order_by_is_rejected_with_an_explanation()
    {
        var act = () => Query.For(DatabaseDialect.SqlServer).Table("users").Limit(5).Offset(2).Compile();

        act.Should().Throw<InvalidOperationException>().WithMessage("*OrderBy*");
    }

    [Fact]
    public void Compiling_without_a_table_names_the_missing_call()
    {
        var act = () => Query.For(DatabaseDialect.Sqlite).Select("id").Compile();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Table*");
    }

    [Fact]
    public void Clone_copies_the_query_and_later_changes_do_not_leak_either_way()
    {
        var original = Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1);
        var copy = original.Clone().Where("b", 2).Limit(5);
        original.OrderBy("id");

        original.ToSql().Should().Be("select * from \"users\" where \"a\" = @p0 order by \"id\" asc");
        copy.ToSql().Should().Be("select * from \"users\" where \"a\" = @p0 and \"b\" = @p1 limit 5");
        copy.Grammar.Should().BeSameAs(original.Grammar);
    }

    [Fact]
    public void Negative_limit_and_offset_are_rejected_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("t");

        ((Action)(() => query.Limit(-1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => query.Offset(-1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Raw_placeholders_must_match_bindings_and_a_bad_direction_is_rejected_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("t");

        ((Action)(() => query.WhereRaw("a = ? and b = ?", 1))).Should().Throw<ArgumentException>().WithMessage("*placeholders*");
        ((Action)(() => query.OrderBy("a", "sideways"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.WhereIn("a", "abc"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.Where("a", ">", null))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToSql_never_contains_values_and_GetBindings_returns_them()
    {
        var query = Query.For(DatabaseDialect.MySql).Table("users").Where("name", "x'; drop table users; --");

        query.ToSql().Should().Be("select * from `users` where `name` = @p0");
        query.GetBindings().Should().Equal("x'; drop table users; --");
    }

    [Fact]
    public void ToRawSql_writes_escaped_literals_for_display_per_dialect()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var when = new DateTime(2026, 10, 17, 9, 30, 0, DateTimeKind.Utc);

        string Raw(DatabaseDialect d) => Query.For(d).Table("t")
            .Where("s", "o'neil\\x").Where("b", true).Where("n", 1.5m).Where("g", id).Where("d", when).Where("y", new byte[] { 0xAB, 0x01 }).Where("e", DayOfWeek.Tuesday)
            .ToRawSql();

        Raw(DatabaseDialect.Sqlite).Should().Be("select * from \"t\" where \"s\" = 'o''neil\\x' and \"b\" = 1 and \"n\" = 1.5 and \"g\" = '11111111-2222-3333-4444-555555555555' and \"d\" = '2026-10-17 09:30:00.000000' and \"y\" = x'AB01' and \"e\" = 2");
        Raw(DatabaseDialect.MySql).Should().Contain("`s` = 'o''neil\\\\x'").And.Contain("`y` = x'AB01'");
        Raw(DatabaseDialect.Postgres).Should().Contain("\"b\" = true").And.Contain("\"y\" = '\\xAB01'");
        Raw(DatabaseDialect.SqlServer).Should().Contain("[s] = N'o''neil\\x'").And.Contain("[y] = 0xAB01");
    }

    [Fact]
    public void ToRawSql_does_not_re_expand_text_that_looks_like_a_marker()
    {
        var sql = Query.For(DatabaseDialect.Sqlite).Table("t").Where("a", "@p1").Where("b", 2).ToRawSql();

        sql.Should().Be("select * from \"t\" where \"a\" = '@p1' and \"b\" = 2");
    }

    [Fact]
    public void Operators_come_from_an_allow_list_and_dialect_gaps_say_so()
    {
        var sqlite = Query.For(DatabaseDialect.Sqlite).Table("t");
        var postgres = Query.For(DatabaseDialect.Postgres).Table("t");
        var mysql = Query.For(DatabaseDialect.MySql).Table("t");

        ((Action)(() => sqlite.Where("a", "= 1 or 1=1 --", 1))).Should().Throw<ArgumentException>();
        ((Action)(() => sqlite.Where("a", "", 1))).Should().Throw<ArgumentException>();
        ((Action)(() => sqlite.Where("a", "ilike", "x"))).Should().Throw<NotSupportedException>();
        ((Action)(() => mysql.Where("a", "ilike", "x"))).Should().Throw<NotSupportedException>();
        ((Action)(() => postgres.Where("a", "<=>", 1))).Should().Throw<NotSupportedException>();
        postgres.Where("a", "ILIKE", "x%").ToSql().Should().Be("select * from \"t\" where \"a\" ilike @p0");
        mysql.Where("a", "<=>", 1).ToSql().Should().Contain("`a` <=> @p0");
        Query.For(DatabaseDialect.Sqlite).Table("t").Where("a", "glob", "x*").ToSql().Should().Contain("glob");
    }

    [Fact]
    public void Grammar_For_covers_every_dialect_and_rejects_unknown_values()
    {
        foreach (var dialect in AllDialects) Grammar.For(dialect).Dialect.Should().Be(dialect);

        ((Action)(() => Grammar.For((DatabaseDialect)99))).Should().Throw<ArgumentOutOfRangeException>();
    }
}
