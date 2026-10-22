using System.Reflection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R24.T06: ToSql, GetBindings and ToRawSql. Pure strings.</summary>
public class QueryDebugTests
{
    private static Query Users(DatabaseDialect d) => Query.For(d).Table("users");

    [Fact]
    public void ToSql_and_GetBindings_are_what_Compile_returns_and_change_nothing()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var query = Users(dialect).Where("a", 1).Where("b", "x").OrderBy("a").Limit(3);
            var before = query.Clone().Compile();

            query.ToSql().Should().Be(before.Sql, dialect.ToString());
            query.GetBindings().Should().Equal(before.Bindings, dialect.ToString());
            query.ToRawSql();
            query.Compile().Sql.Should().Be(before.Sql, "calling the helpers does not change the query");
            query.Compile().Bindings.Should().Equal(before.Bindings);
        }
    }

    [Fact]
    public void ToRawSql_writes_null_text_and_numbers_the_way_each_dialect_reads_them()
    {
        var sqlite = Users(DatabaseDialect.Sqlite).WhereRaw("coalesce(a, ?) = 1", (object?)null).Where("n", 3).Where("s", "it's").ToRawSql();
        var mysql = Users(DatabaseDialect.MySql).Where("s", "a'b\\c").Where("f", 1.5).ToRawSql();
        var postgres = Users(DatabaseDialect.Postgres).Where("ok", true).Where("s", "it's").ToRawSql();
        var sqlServer = Users(DatabaseDialect.SqlServer).Where("ok", false).Where("s", "it's").ToRawSql();

        sqlite.Should().Be("select * from \"users\" where (coalesce(a, NULL) = 1) and \"n\" = 3 and \"s\" = 'it''s'");
        mysql.Should().Be("select * from `users` where `s` = 'a''b\\\\c' and `f` = 1.5");
        postgres.Should().Be("select * from \"users\" where \"ok\" = true and \"s\" = 'it''s'");
        sqlServer.Should().Be("select * from [users] where [ok] = 0 and [s] = N'it''s'");
    }

    [Fact]
    public void A_value_that_tries_to_end_the_text_stays_inside_it()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var raw = Users(dialect).Where("name", "x'; drop table users; --").ToRawSql();

            raw.Should().Contain("'x''; drop table users; --'", dialect.ToString());
            Users(dialect).Where("name", "x'; drop table users; --").ToSql().Should().NotContain("drop table", "the SQL text never holds the value");
        }
    }

    [Fact]
    public void ToRawSql_covers_markers_in_text_order_across_every_kind_of_clause()
    {
        var query = Users(DatabaseDialect.Postgres)
            .WithExpression("big", Query.For(DatabaseDialect.Postgres).Table("u").Where("x", 1))
            .Where("a", 2)
            .WhereRaw("b > ?", 3)
            .WhereIn("id", new[] { 4, 5 })
            .Limit(6);

        query.ToRawSql().Should().Be("with \"big\" as (select * from \"u\" where \"x\" = 1) select * from \"users\" where \"a\" = 2 and (b > 3) and \"id\" in (4, 5) limit 6");
    }

    [Fact]
    public void ToRawSql_works_for_unions_subqueries_and_locks_too()
    {
        var union = Users(DatabaseDialect.Sqlite).Where("a", 1).Union(Query.For(DatabaseDialect.Sqlite).Table("old").Where("a", 2));
        var locked = Users(DatabaseDialect.MySql).Where("id", 7).LockForUpdate();

        union.ToRawSql().Should().Be("select * from \"users\" where \"a\" = 1 union select * from \"old\" where \"a\" = 2");
        locked.ToRawSql().Should().Be("select * from `users` where `id` = 7 for update");
    }

    [Fact]
    public void Text_in_a_raw_part_that_looks_like_a_marker_is_replaced_in_the_display_only()
    {
        var query = Users(DatabaseDialect.Sqlite).Where("a", 9).WhereRaw("note = '@p0'");

        query.ToSql().Should().Be("select * from \"users\" where \"a\" = @p0 and (note = '@p0')");
        query.GetBindings().Should().Equal(9);
        query.ToRawSql().Should().Be("select * from \"users\" where \"a\" = 9 and (note = '9')", "the display form is a substitution, which is why it must never be executed");
    }

    [Fact]
    public void Compile_errors_surface_from_every_debug_helper()
    {
        var noTable = Query.For(DatabaseDialect.Sqlite);
        var lockedOnSqlite = Users(DatabaseDialect.Sqlite).SharedLock();

        foreach (var act in new Action[] { () => noTable.ToSql(), () => noTable.GetBindings(), () => noTable.ToRawSql() })
        {
            act.Should().Throw<InvalidOperationException>();
        }

        foreach (var act in new Action[] { () => lockedOnSqlite.ToSql(), () => lockedOnSqlite.GetBindings(), () => lockedOnSqlite.ToRawSql() })
        {
            act.Should().Throw<NotSupportedException>();
        }
    }

    [Fact]
    public void The_php_debug_helpers_are_not_ported()
    {
        var names = typeof(Query).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        names.Should().NotContain(["Dump", "Dd", "DumpRawSql", "DdRawSql"]);
        names.Should().Contain(["ToSql", "ToRawSql", "GetBindings"]);
    }
}
