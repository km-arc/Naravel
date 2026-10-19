using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R21.T03: pattern, full-text and JSON where methods on all four grammars. Pure strings: no database.</summary>
public class QueryPatternJsonTests
{
    private sealed record Expect(string Sql, object?[] Bindings);

    private sealed record Case(string Name, Func<Query, Query> Build, Expect Sqlite, Expect MySql, Expect Postgres, Expect SqlServer);

    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static Expect E(string sql, params object?[] bindings) => new("select * from «users» where " + sql, bindings);

    private static void Verify(params Case[] cases)
    {
        foreach (var testCase in cases)
        {
            foreach (var dialect in Enum.GetValues<DatabaseDialect>())
            {
                var expected = dialect switch
                {
                    DatabaseDialect.Sqlite => testCase.Sqlite,
                    DatabaseDialect.MySql => testCase.MySql,
                    DatabaseDialect.Postgres => testCase.Postgres,
                    _ => testCase.SqlServer,
                };
                var compiled = testCase.Build(Query.For(dialect).Table("users")).Compile();

                compiled.Sql.Should().Be(Quoted(expected.Sql, dialect), $"case '{testCase.Name}' on {dialect}");
                compiled.Bindings.Should().Equal(expected.Bindings, $"case '{testCase.Name}' bindings on {dialect}");
            }
        }
    }

    [Fact]
    public void Like_is_case_insensitive_by_default_and_per_dialect_when_case_sensitive()
    {
        Verify(
            new("whereLike", q => q.WhereLike("name", "a%"),
                E("«name» like @p0", "a%"), E("«name» like @p0", "a%"), E("«name» ilike @p0", "a%"), E("«name» like @p0", "a%")),
            new("orWhereLike", q => q.Where("a", 1).OrWhereLike("name", "a%"),
                E("«a» = @p0 or «name» like @p1", 1, "a%"), E("«a» = @p0 or «name» like @p1", 1, "a%"), E("«a» = @p0 or «name» ilike @p1", 1, "a%"), E("«a» = @p0 or «name» like @p1", 1, "a%")),
            new("whereNotLike", q => q.WhereNotLike("name", "a%"),
                E("«name» not like @p0", "a%"), E("«name» not like @p0", "a%"), E("«name» not ilike @p0", "a%"), E("«name» not like @p0", "a%")),
            new("orWhereNotLike", q => q.Where("a", 1).OrWhereNotLike("name", "a%"),
                E("«a» = @p0 or «name» not like @p1", 1, "a%"), E("«a» = @p0 or «name» not like @p1", 1, "a%"), E("«a» = @p0 or «name» not ilike @p1", 1, "a%"), E("«a» = @p0 or «name» not like @p1", 1, "a%")),
            new("whereNotLike with a JSON path",
                q => q.WhereNotLike("data->name", "a%"),
                E("json_extract(«data», @p0) not like @p1", "$.\"name\"", "a%"),
                E("json_unquote(json_extract(«data», @p0)) not like @p1", "$.\"name\"", "a%"),
                E("(cast(«data» as jsonb) #>> cast(@p0 as text[])) not ilike @p1", "{name}", "a%"),
                E("json_value(«data», @p0) not like @p1", "$.\"name\"", "a%")));

        var sensitive = Query.For(DatabaseDialect.MySql).Table("users").WhereLike("name", "A%", caseSensitive: true).Compile();
        sensitive.Sql.Should().Be("select * from `users` where `name` like binary @p0");
        Query.For(DatabaseDialect.MySql).Table("users").WhereNotLike("name", "A%", true).Compile().Sql.Should().EndWith("`name` not like binary @p0");
        Query.For(DatabaseDialect.Postgres).Table("users").WhereLike("name", "A%", true).Compile().Sql.Should().Be("select * from \"users\" where \"name\" like @p0");
        Query.For(DatabaseDialect.Postgres).Table("users").WhereNotLike("name", "A%", true).Compile().Sql.Should().EndWith("\"name\" not like @p0");
        Query.For(DatabaseDialect.SqlServer).Table("users").WhereLike("name", "A%", true).Compile().Sql
            .Should().Be("select * from [users] where [name] like @p0 collate SQL_Latin1_General_CP1_CS_AS");
        Query.For(DatabaseDialect.SqlServer).Table("users").WhereNotLike("name", "A%", true).Compile().Sql
            .Should().EndWith("[name] not like @p0 collate SQL_Latin1_General_CP1_CS_AS");
    }

    [Fact]
    public void A_case_sensitive_like_on_sqlite_throws_instead_of_guessing()
    {
        var query = Query.For(DatabaseDialect.Sqlite).Table("users").WhereLike("name", "A%", caseSensitive: true);

        var act = () => query.Compile();

        act.Should().Throw<NotSupportedException>().WithMessage("*Sqlite*");
    }

    [Fact]
    public void Full_text_uses_each_engines_own_syntax_and_sqlite_throws_at_compile()
    {
        Query Natural(Query q) => q.WhereFullText("name", "ada");
        Query Boolean(Query q) => q.Where("z", 1).OrWhereFullText(["a", "b"], "+ada", FullTextMode.Boolean);

        Query.For(DatabaseDialect.MySql).Table("users").WhereFullText("name", "ada").Compile().Sql
            .Should().Be("select * from `users` where match(`name`) against (@p0 in natural language mode)");
        Query.For(DatabaseDialect.Postgres).Table("users").WhereFullText("name", "ada").Compile().Sql
            .Should().Be("select * from \"users\" where (to_tsvector('english', \"name\")) @@ plainto_tsquery('english', @p0)");
        Query.For(DatabaseDialect.SqlServer).Table("users").WhereFullText("name", "ada").Compile().Sql
            .Should().Be("select * from [users] where freetext([name], @p0)");

        var mysql = Boolean(Query.For(DatabaseDialect.MySql).Table("users")).Compile();
        mysql.Sql.Should().Be("select * from `users` where `z` = @p0 or match(`a`, `b`) against (@p1 in boolean mode)");
        mysql.Bindings.Should().Equal(1, "+ada");
        Boolean(Query.For(DatabaseDialect.Postgres).Table("users")).Compile().Sql
            .Should().Be("select * from \"users\" where \"z\" = @p0 or (to_tsvector('english', \"a\") || to_tsvector('english', \"b\")) @@ to_tsquery('english', @p1)");
        Boolean(Query.For(DatabaseDialect.SqlServer).Table("users")).Compile().Sql
            .Should().Be("select * from [users] where [z] = @p0 or contains(([a], [b]), @p1)");

        foreach (var build in new Func<Query, Query>[] { Natural, Boolean })
        {
            var query = build(Query.For(DatabaseDialect.Sqlite).Table("users"));
            var act = () => query.Compile();
            act.Should().Throw<NotSupportedException>().WithMessage("*WhereFullText*Sqlite*");
        }
    }

    [Fact]
    public void Json_paths_in_ordinary_where_methods_bind_the_path_and_cast_for_postgres()
    {
        Verify(
            new("where on a path", q => q.Where("data->name", "Ada"),
                E("json_extract(«data», @p0) = @p1", "$.\"name\"", "Ada"),
                E("json_unquote(json_extract(«data», @p0)) = @p1", "$.\"name\"", "Ada"),
                E("(cast(«data» as jsonb) #>> cast(@p0 as text[])) = @p1", "{name}", "Ada"),
                E("json_value(«data», @p0) = @p1", "$.\"name\"", "Ada")),
            new("nested path with an index and a number", q => q.Where("users.data->list[0]->id", ">", 5),
                E("json_extract(«users».«data», @p0) > @p1", "$.\"list\"[0].\"id\"", 5),
                E("json_unquote(json_extract(«users».«data», @p0)) > @p1", "$.\"list\"[0].\"id\"", 5),
                E("cast((cast(«users».«data» as jsonb) #>> cast(@p0 as text[])) as numeric) > @p1", "{list,0,id}", 5),
                E("json_value(«users».«data», @p0) > @p1", "$.\"list\"[0].\"id\"", 5)),
            new("boolean value casts on postgres", q => q.Where("data->active", true),
                E("json_extract(«data», @p0) = @p1", "$.\"active\"", true),
                E("json_unquote(json_extract(«data», @p0)) = @p1", "$.\"active\"", true),
                E("cast((cast(«data» as jsonb) #>> cast(@p0 as text[])) as boolean) = @p1", "{active}", true),
                E("json_value(«data», @p0) = @p1", "$.\"active\"", true)),
            new("null check on a path", q => q.WhereNull("data->nick"),
                E("json_extract(«data», @p0) is null", "$.\"nick\""),
                E("json_unquote(json_extract(«data», @p0)) is null", "$.\"nick\""),
                E("(cast(«data» as jsonb) #>> cast(@p0 as text[])) is null", "{nick}"),
                E("json_value(«data», @p0) is null", "$.\"nick\"")),
            new("in on a path", q => q.WhereIn("data->kind", new[] { "a", "b" }),
                E("json_extract(«data», @p0) in (@p1, @p2)", "$.\"kind\"", "a", "b"),
                E("json_unquote(json_extract(«data», @p0)) in (@p1, @p2)", "$.\"kind\"", "a", "b"),
                E("(cast(«data» as jsonb) #>> cast(@p0 as text[])) in (@p1, @p2)", "{kind}", "a", "b"),
                E("json_value(«data», @p0) in (@p1, @p2)", "$.\"kind\"", "a", "b")));
    }

    [Fact]
    public void Json_contains_uses_each_engines_own_search()
    {
        Verify(
            new("whereJsonContains with a path", q => q.WhereJsonContains("data->tags", "php"),
                E("exists (select 1 from json_each(«data», @p0) where \"json_each\".\"value\" is @p1)", "$.\"tags\"", "php"),
                E("json_contains(«data», @p0, @p1)", "\"php\"", "$.\"tags\""),
                E("(cast(«data» as jsonb) #> cast(@p0 as text[])) @> cast(@p1 as jsonb)", "{tags}", "\"php\""),
                E("@p0 in (select [value] from openjson(«data», @p1))", "php", "$.\"tags\"")),
            new("whereJsonDoesntContain without a path", q => q.WhereJsonDoesntContain("data", 5),
                E("not exists (select 1 from json_each(«data») where \"json_each\".\"value\" is @p0)", 5),
                E("not json_contains(«data», @p0)", "5"),
                E("not cast(«data» as jsonb) @> cast(@p0 as jsonb)", "5"),
                E("not @p0 in (select [value] from openjson(«data»))", "5")),
            new("orWhereJsonContains boolean", q => q.Where("a", 1).OrWhereJsonContains("data", true),
                E("«a» = @p0 or exists (select 1 from json_each(«data») where \"json_each\".\"value\" is @p1)", 1, true),
                E("«a» = @p0 or json_contains(«data», @p1)", 1, "true"),
                E("«a» = @p0 or cast(«data» as jsonb) @> cast(@p1 as jsonb)", 1, "true"),
                E("«a» = @p0 or @p1 in (select [value] from openjson(«data»))", 1, "true")),
            new("orWhereJsonDoesntContain", q => q.Where("a", 1).OrWhereJsonDoesntContain("data", "x"),
                E("«a» = @p0 or not exists (select 1 from json_each(«data») where \"json_each\".\"value\" is @p1)", 1, "x"),
                E("«a» = @p0 or not json_contains(«data», @p1)", 1, "\"x\""),
                E("«a» = @p0 or not cast(«data» as jsonb) @> cast(@p1 as jsonb)", 1, "\"x\""),
                E("«a» = @p0 or not @p1 in (select [value] from openjson(«data»))", 1, "x")));

        var array = new[] { "a", "b" };
        Query.For(DatabaseDialect.MySql).Table("users").WhereJsonContains("data", array).Compile().Bindings.Should().Equal("[\"a\",\"b\"]");
        Query.For(DatabaseDialect.Postgres).Table("users").WhereJsonContains("data", array).Compile().Bindings.Should().Equal("[\"a\",\"b\"]");
        foreach (var dialect in new[] { DatabaseDialect.Sqlite, DatabaseDialect.SqlServer })
        {
            var query = Query.For(dialect).Table("users").WhereJsonContains("data", array);
            var act = () => query.Compile();
            act.Should().Throw<NotSupportedException>().WithMessage($"*{dialect}*");
        }
    }

    [Fact]
    public void Json_key_and_length_use_each_engines_own_functions()
    {
        Verify(
            new("whereJsonContainsKey", q => q.WhereJsonContainsKey("data->name"),
                E("json_type(«data», @p0) is not null", "$.\"name\""),
                E("ifnull(json_contains_path(«data», 'one', @p0), 0)", "$.\"name\""),
                E("coalesce(jsonb_path_exists(cast(«data» as jsonb), cast(@p0 as jsonpath)), false)", "$.\"name\""),
                E("(json_value(«data», @p0) is not null or json_query(«data», @p1) is not null)", "$.\"name\"", "$.\"name\"")),
            new("whereJsonDoesntContainKey nested", q => q.WhereJsonDoesntContainKey("data->a->list[1]"),
                E("not json_type(«data», @p0) is not null", "$.\"a\".\"list\"[1]"),
                E("not ifnull(json_contains_path(«data», 'one', @p0), 0)", "$.\"a\".\"list\"[1]"),
                E("not coalesce(jsonb_path_exists(cast(«data» as jsonb), cast(@p0 as jsonpath)), false)", "$.\"a\".\"list\"[1]"),
                E("not (json_value(«data», @p0) is not null or json_query(«data», @p1) is not null)", "$.\"a\".\"list\"[1]", "$.\"a\".\"list\"[1]")),
            new("orWhereJsonContainsKey", q => q.Where("a", 1).OrWhereJsonContainsKey("data->k"),
                E("«a» = @p0 or json_type(«data», @p1) is not null", 1, "$.\"k\""),
                E("«a» = @p0 or ifnull(json_contains_path(«data», 'one', @p1), 0)", 1, "$.\"k\""),
                E("«a» = @p0 or coalesce(jsonb_path_exists(cast(«data» as jsonb), cast(@p1 as jsonpath)), false)", 1, "$.\"k\""),
                E("«a» = @p0 or (json_value(«data», @p1) is not null or json_query(«data», @p2) is not null)", 1, "$.\"k\"", "$.\"k\"")),
            new("whereJsonLength with a path", q => q.WhereJsonLength("data->tags", ">", 2),
                E("json_array_length(«data», @p0) > @p1", "$.\"tags\"", 2),
                E("json_length(«data», @p0) > @p1", "$.\"tags\"", 2),
                E("jsonb_array_length((cast(«data» as jsonb) #> cast(@p0 as text[]))) > @p1", "{tags}", 2),
                E("(select count(*) from openjson(«data», @p0)) > @p1", "$.\"tags\"", 2)),
            new("whereJsonLength without a path", q => q.WhereJsonLength("data", 3),
                E("json_array_length(«data») = @p0", 3),
                E("json_length(«data») = @p0", 3),
                E("jsonb_array_length(cast(«data» as jsonb)) = @p0", 3),
                E("(select count(*) from openjson(«data»)) = @p0", 3)),
            new("orWhereJsonLength operator", q => q.Where("a", 1).OrWhereJsonLength("data", "<=", 0),
                E("«a» = @p0 or json_array_length(«data») <= @p1", 1, 0),
                E("«a» = @p0 or json_length(«data») <= @p1", 1, 0),
                E("«a» = @p0 or jsonb_array_length(cast(«data» as jsonb)) <= @p1", 1, 0),
                E("«a» = @p0 or (select count(*) from openjson(«data»)) <= @p1", 1, 0)));
    }

    [Fact]
    public void Pattern_and_json_methods_reject_bad_input_at_the_call()
    {
        var query = Query.For(DatabaseDialect.Postgres).Table("users");

        ((Action)(() => query.WhereLike("name; drop", "a%"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.WhereLike("name", null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.WhereLike("data->a b", "x"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.WhereFullText(Array.Empty<string>(), "x"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.WhereFullText("name", null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.WhereFullText("data->a", "x"))).Should().Throw<NotSupportedException>();
        ((Action)(() => query.WhereFullText("name", "x", (FullTextMode)9))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => query.WhereJsonContains("data", null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => query.WhereJsonContainsKey("data"))).Should().Throw<ArgumentException>();
        ((Action)(() => query.WhereJsonLength("data", -1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => query.WhereJsonLength("data", "like", 1))).Should().Throw<ArgumentException>();
        ((Action)(() => query.WhereDate("data->day", "2024-01-01"))).Should().Throw<NotSupportedException>();
        ((Action)(() => query.Select("data->name"))).Should().Throw<NotSupportedException>();
        ((Action)(() => query.OrderBy("data->name"))).Should().Throw<NotSupportedException>();
        ((Action)(() => Query.For(DatabaseDialect.Postgres).Table("data->x"))).Should().Throw<NotSupportedException>();
        ((Action)(() => query.Where("data->name as n", 1))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_json_path_value_never_reaches_the_sql_text()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = Query.For(dialect).Table("users").Where("data->name", "x").WhereJsonContainsKey("data->secret").Compile();

            compiled.Sql.Should().NotContain("name\"").And.NotContain("secret\"").And.NotContain("$.");
        }
    }
}
