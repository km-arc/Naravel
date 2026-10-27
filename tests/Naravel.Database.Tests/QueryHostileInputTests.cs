using Naravel.Database;
using Naravel.Database.Testing;

namespace Naravel.Database.Tests;

/// <summary>
/// Negative and edge cases for input that looks like SQL, on every dialect, with no server needed (AUDIT-free hardening of
/// the PDR-022 A3 contract): an identifier, an operator or a direction is checked at the call and a value only ever travels as a binding.
/// </summary>
public class QueryHostileInputTests
{
    private static readonly DatabaseDialect[] AllDialects = Enum.GetValues<DatabaseDialect>();

    private static readonly string[] Payloads =
    [
        "'; drop table users; --", "\" or 1=1 --", "x' union select password from users --", "`; drop table x; --", "]; drop table x; --",
        "a\\'; drop table x; --", "%' or '1'='1", "\0; drop table x", "line1\nline2; drop table x", "Robert'); DROP TABLE Students;--",
    ];

    private static bool IsInert(string sql, string payload) =>
        !sql.Contains(payload, StringComparison.Ordinal) && !sql.Contains("drop table", StringComparison.OrdinalIgnoreCase) && !sql.Contains("union select", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void A_value_in_any_read_method_is_a_binding_and_never_part_of_the_sql()
    {
        foreach (var dialect in AllDialects)
        {
            foreach (var payload in Payloads)
            {
                var compiled = Query.For(dialect).Table("t")
                    .Where("a", payload)
                    .OrWhere("b", "<>", payload)
                    .WhereIn("c", new[] { payload, "ok" })
                    .WhereNotIn("d", new[] { payload })
                    .WhereBetween("e", payload, payload + "z")
                    .WhereLike("f", payload)
                    .Having("g", payload)
                    .Compile();

                IsInert(compiled.Sql, payload).Should().BeTrue($"{dialect}: {compiled.Sql}");
                compiled.Bindings.Should().Contain(payload, dialect.ToString());
            }
        }
    }

    [Fact]
    public void A_json_value_and_a_full_text_query_are_bindings_where_the_dialect_supports_them()
    {
        foreach (var dialect in AllDialects)
        {
            foreach (var payload in Payloads)
            {
                var json = Query.For(dialect).Table("t").WhereJsonContains("data->tags", payload).Compile();
                IsInert(json.Sql, payload).Should().BeTrue($"{dialect}: {json.Sql}");

                if (dialect == DatabaseDialect.Sqlite)
                {
                    ((Action)(() => Query.For(dialect).Table("t").WhereFullText("body", payload).Compile())).Should().Throw<NotSupportedException>();
                    continue;
                }

                var text = Query.For(dialect).Table("t").WhereFullText("body", payload).Compile();
                IsInert(text.Sql, payload).Should().BeTrue($"{dialect}: {text.Sql}");
            }
        }
    }

    [Fact]
    public void An_operator_that_looks_like_sql_is_rejected_at_the_call_on_every_dialect()
    {
        string[] operators = ["= 1; drop table x --", "or 1=1", "like ' or '1'='1", "= any (select 1)", ")", "=;", "== 1", "= /*x*/"];
        foreach (var dialect in AllDialects)
        {
            foreach (var op in operators)
            {
                ((Action)(() => Query.For(dialect).Table("t").Where("a", op, 1))).Should().Throw<ArgumentException>($"{dialect}: '{op}'");
                ((Action)(() => Query.For(dialect).Table("t").OrWhere("a", op, 1))).Should().Throw<ArgumentException>($"{dialect}: '{op}'");
            }
        }
    }

    [Fact]
    public void An_order_direction_that_looks_like_sql_is_rejected_at_the_call_on_every_dialect()
    {
        string[] directions = ["asc; drop table x", "desc, id", "", " ", "ascending", "asc --", "1", "desc nulls first"];
        foreach (var dialect in AllDialects)
        {
            foreach (var direction in directions)
            {
                ((Action)(() => Query.For(dialect).Table("t").OrderBy("a", direction))).Should().Throw<ArgumentException>($"{dialect}: '{direction}'");
            }
        }
    }

    [Fact]
    public async Task A_hostile_column_name_in_a_write_is_rejected_before_any_statement_reaches_the_database()
    {
        string[] hostile = ["a;drop table x", "a--", "a\"b", "a b", "", "[a]", "a)"];
        foreach (var dialect in AllDialects)
        {
            foreach (var name in hostile)
            {
                var db = new DatabaseFake(dialect);
                var row = new Dictionary<string, object?> { [name] = 1 };

                await ((Func<Task>)(() => db.Table("t").InsertAsync(row))).Should().ThrowAsync<ArgumentException>($"insert {dialect}: '{name}'");
                await ((Func<Task>)(() => db.Table("t").Where("id", 1).UpdateAsync(row))).Should().ThrowAsync<ArgumentException>($"update {dialect}: '{name}'");
                await ((Func<Task>)(() => db.Table("t").UpsertAsync(row, [name]))).Should().ThrowAsync<ArgumentException>($"upsert uniqueBy {dialect}: '{name}'");
                await ((Func<Task>)(() => db.Table("t").UpsertAsync(new Dictionary<string, object?> { ["id"] = 1 }, ["id"], [name]))).Should().ThrowAsync<ArgumentException>($"upsert update {dialect}: '{name}'");
                db.AssertNothingExecuted();
            }
        }
    }

    [Fact]
    public async Task A_value_in_a_write_is_a_binding_and_never_part_of_the_sql()
    {
        foreach (var dialect in AllDialects)
        {
            foreach (var payload in Payloads)
            {
                var db = new DatabaseFake(dialect);
                var row = new Dictionary<string, object?> { ["id"] = 1, ["name"] = payload };

                await db.Table("t").InsertAsync(row);
                await db.Table("t").Where("id", 1).UpdateAsync(new Dictionary<string, object?> { ["name"] = payload });
                await db.Table("t").UpsertAsync(row, ["id"]);

                db.Executed.Should().NotBeEmpty();
                foreach (var executed in db.Executed)
                {
                    IsInert(executed.Sql, payload).Should().BeTrue($"{dialect}: {executed.Sql}");
                    executed.Bindings.Should().Contain(payload, $"{dialect}: {executed.Sql}");
                }
            }
        }
    }

    [Fact]
    public async Task An_update_or_delete_without_a_where_is_refused_on_every_dialect()
    {
        foreach (var dialect in AllDialects)
        {
            var db = new DatabaseFake(dialect);

            await ((Func<Task>)(() => db.Table("t").UpdateAsync(new Dictionary<string, object?> { ["a"] = 1 }))).Should().ThrowAsync<InvalidOperationException>(dialect.ToString());
            await ((Func<Task>)(() => db.Table("t").DeleteAsync())).Should().ThrowAsync<InvalidOperationException>(dialect.ToString());
            db.AssertNothingExecuted();
        }
    }

    [Fact]
    public void Edge_collections_and_nulls_compile_to_defined_SQL_on_every_dialect()
    {
        foreach (var dialect in AllDialects)
        {
            // An empty IN matches nothing and an empty NOT IN matches everything; neither emits invalid "in ()".
            Query.For(dialect).Table("t").WhereIn("a", Array.Empty<int>()).Compile().Sql.Should().NotContain("in ()", dialect.ToString());
            Query.For(dialect).Table("t").WhereNotIn("a", Array.Empty<int>()).Compile().Sql.Should().NotContain("in ()", dialect.ToString());

            // A null value becomes "is null"; a string is not accepted as a collection.
            Query.For(dialect).Table("t").Where("a", null).Compile().Sql.Should().Contain("null", dialect.ToString());
            ((Action)(() => Query.For(dialect).Table("t").WhereIn("a", "abc"))).Should().Throw<ArgumentException>(dialect.ToString());

            // Both ends of a range must be given.
            ((Action)(() => Query.For(dialect).Table("t").WhereBetween("a", null, 1).Compile())).Should().Throw<ArgumentException>(dialect.ToString());
        }
    }
}
