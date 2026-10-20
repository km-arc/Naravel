using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R23.T01: insert SQL on every grammar, plus SQLite execution. Pure strings for the compile cases.</summary>
public class QueryInsertTests
{
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    private static void Verify(string name, Func<Query, IReadOnlyList<CompiledQuery>> compile, string sql, params object?[] bindings)
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = compile(Query.For(dialect).Table("users"));

            compiled.Should().ContainSingle($"case '{name}' on {dialect}");
            compiled[0].Sql.Should().Be(Quoted(sql, dialect), $"case '{name}' on {dialect}");
            compiled[0].Bindings.Should().Equal(bindings, $"case '{name}' bindings on {dialect}");
        }
    }

    [Fact]
    public void Insert_compiles_a_single_row_and_a_batch_with_bound_values()
    {
        Verify("single", q => q.CompileInsert([Row(("a", 1), ("b", "x"))], false), "insert into «users» («a», «b») values (@p0, @p1)", 1, "x");
        Verify("null value is a binding", q => q.CompileInsert([Row(("a", null))], false), "insert into «users» («a») values (@p0)", (object?)null);
        Verify("batch", q => q.CompileInsert([Row(("a", 1), ("b", 2)), Row(("a", 3), ("b", 4))], false),
            "insert into «users» («a», «b») values (@p0, @p1), (@p2, @p3)", 1, 2, 3, 4);
        Verify("later rows may list the keys in another order", q => q.CompileInsert([Row(("a", 1), ("b", 2)), Row(("b", 4), ("a", 3))], false),
            "insert into «users» («a», «b») values (@p0, @p1), (@p2, @p3)", 1, 2, 3, 4);
    }

    [Fact]
    public void An_empty_row_inserts_the_defaults_in_the_dialects_own_way()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var statements = Query.For(dialect).Table("users").CompileInsert([Row()], false);

            statements.Should().ContainSingle();
            statements[0].Sql.Should().Be(Quoted(dialect == DatabaseDialect.MySql ? "insert into «users» () values ()" : "insert into «users» default values", dialect), dialect.ToString());
        }
    }

    [Fact]
    public void No_rows_compile_to_no_statements()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>()) Query.For(dialect).Table("users").CompileInsert([], false).Should().BeEmpty(dialect.ToString());
    }

    [Fact]
    public void Insert_or_ignore_uses_each_dialects_syntax_and_sql_server_refuses()
    {
        var row = Row(("a", 1));
        Query.For(DatabaseDialect.Sqlite).Table("users").CompileInsert([row], true)[0].Sql.Should().Be("insert or ignore into \"users\" (\"a\") values (@p0)");
        Query.For(DatabaseDialect.MySql).Table("users").CompileInsert([row], true)[0].Sql.Should().Be("insert ignore into `users` (`a`) values (@p0)");
        Query.For(DatabaseDialect.Postgres).Table("users").CompileInsert([row], true)[0].Sql.Should().Be("insert into \"users\" (\"a\") values (@p0) on conflict do nothing");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.SqlServer).Table("users").CompileInsert([row], true)).Should().Throw<NotSupportedException>().WithMessage("*SqlServer*");
    }

    [Fact]
    public void A_batch_is_split_at_the_parameter_and_row_limits()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var grammar = Grammar.For(dialect);
            var perStatement = Math.Min(grammar.MaxInsertRows, grammar.MaxParameters);
            var rows = Enumerable.Range(0, perStatement + 1).Select(i => (IReadOnlyDictionary<string, object?>)Row(("a", i))).ToArray();

            var statements = Query.For(dialect).Table("users").CompileInsert(rows, false);

            statements.Should().HaveCount(2, dialect.ToString());
            statements[0].Bindings.Should().HaveCount(perStatement);
            statements[1].Bindings.Should().HaveCount(1);
            statements.All(s => s.Bindings.Count <= grammar.MaxParameters).Should().BeTrue();
        }
    }

    [Fact]
    public void Two_columns_halve_the_rows_per_statement_on_sql_server()
    {
        var rows = Enumerable.Range(0, 1200).Select(i => (IReadOnlyDictionary<string, object?>)Row(("a", i), ("b", i))).ToArray();

        var statements = Query.For(DatabaseDialect.SqlServer).Table("users").CompileInsert(rows, false);

        statements.Select(s => s.Bindings.Count).Should().Equal(2000, 400);
    }

    [Fact]
    public void Insert_get_id_asks_for_the_key_the_way_each_engine_can()
    {
        var row = Row(("a", 1));
        Query.For(DatabaseDialect.Sqlite).Table("users").CompileInsertGetId(row, "id").Sql.Should().Be("insert into \"users\" (\"a\") values (@p0) returning \"id\"");
        Query.For(DatabaseDialect.Postgres).Table("users").CompileInsertGetId(row, "id").Sql.Should().Be("insert into \"users\" (\"a\") values (@p0) returning \"id\"");
        Query.For(DatabaseDialect.SqlServer).Table("users").CompileInsertGetId(row, "id").Sql.Should().Be("insert into [users] ([a]) output inserted.[id] values (@p0)");
        Query.For(DatabaseDialect.MySql).Table("users").CompileInsertGetId(row, "id").Sql.Should().Be("insert into `users` (`a`) values (@p0)");
        Grammar.For(DatabaseDialect.MySql).LastInsertIdSql.Should().Be("select last_insert_id()");
        Query.For(DatabaseDialect.SqlServer).Table("users").CompileInsertGetId(Row(), "id").Sql.Should().Be("insert into [users] output inserted.[id] default values");
        Query.For(DatabaseDialect.Sqlite).Table("users").CompileInsertGetId(row, "user_id").Sql.Should().EndWith("returning \"user_id\"");
    }

    [Fact]
    public void Insert_using_compiles_the_select_with_one_numbering_of_parameters()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var source = Query.For(dialect).Table("other").Select("x", "y").Where("z", ">", 5);
            var compiled = Query.For(dialect).Table("users").CompileInsertUsing(["a", "b"], source);

            compiled.Sql.Should().Be(Quoted("insert into «users» («a», «b») select «x», «y» from «other» where «z» > @p0", dialect), dialect.ToString());
            compiled.Bindings.Should().Equal(5);
        }
    }

    [Fact]
    public void Bad_input_fails_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        FluentActions.Invoking(() => query.CompileInsert([Row(("a", 1)), Row(("b", 2))], false)).Should().Throw<ArgumentException>().WithMessage("*exactly the columns*");
        FluentActions.Invoking(() => query.CompileInsert([Row(("a", 1)), Row(("a", 1), ("b", 2))], false)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsert([Row(("a b", 1))], false)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsert([Row(("users.a", 1))], false)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsert([Row(("a", 1)), null!], false)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsert([Row(), Row()], false)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsertGetId(Row(("a", 1)), "t.id")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsertUsing([], Query.For(DatabaseDialect.Sqlite).Table("o"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsertUsing(["a"], Query.For(DatabaseDialect.MySql).Table("o"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.CompileInsertUsing(["u.a"], Query.For(DatabaseDialect.Sqlite).Table("o"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).CompileInsert([Row(("a", 1))], false)).Should().Throw<InvalidOperationException>().WithMessage("*no table*");
    }

    [Fact]
    public void An_insert_query_may_only_have_a_table()
    {
        var row = Row(("a", 1));
        foreach (var query in new[]
        {
            Query.For(DatabaseDialect.Sqlite).Table("users").Where("a", 1),
            Query.For(DatabaseDialect.Sqlite).Table("users").Select("a"),
            Query.For(DatabaseDialect.Sqlite).Table("users").OrderBy("a"),
            Query.For(DatabaseDialect.Sqlite).Table("users").Limit(1),
            Query.For(DatabaseDialect.Sqlite).Table("users").Join("o", "o.a", "users.a"),
            Query.For(DatabaseDialect.Sqlite).Table("users").Distinct(),
        })
        {
            FluentActions.Invoking(() => query.CompileInsert([row], false)).Should().Throw<InvalidOperationException>().WithMessage("*only have a table*");
        }
    }

    [Fact]
    public async Task Unconnected_inserts_fail_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users");

        await FluentActions.Awaiting(() => query.InsertAsync(Row(("a", 1)))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => query.InsertGetIdAsync<long>(Row(("a", 1)))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => query.InsertAsync((IReadOnlyDictionary<string, object?>)null!)).Should().ThrowAsync<ArgumentNullException>();
    }
}
