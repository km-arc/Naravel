using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>
/// The input contract of <c>WhereJsonContains</c> / <c>WhereJsonDoesntContain</c> (and the Or variants) per dialect, offline: which values each
/// engine accepts, which it refuses with <see cref="NotSupportedException"/>, and which are rejected on every engine. Executed behaviour on real
/// servers is in the shared contract suite (<c>where_json_contains_*</c>); the table is in docs/en/database-dialects.md.
/// </summary>
public class JsonContainsContractTests
{
    private enum Color { Red }

    private static readonly DatabaseDialect[] ScalarOnly = [DatabaseDialect.Sqlite, DatabaseDialect.SqlServer];

    public static IEnumerable<object[]> Dialects() => Enum.GetValues<DatabaseDialect>().Select(d => new object[] { d });

    public static IEnumerable<object[]> Scalars()
    {
        object[] values = ["text", 1, 1L, 2.5d, 2.5f, 2.5m, true, (byte)1, (short)1];
        foreach (var d in Enum.GetValues<DatabaseDialect>())
            foreach (var v in values) yield return new object[] { d, v };
    }

    public static IEnumerable<object[]> NonScalars()
    {
        object[] values =
        [
            new[] { "a", "b" }, new List<int> { 1 }, new { a = 1 }, new Dictionary<string, object> { ["a"] = 1 },
            Guid.Empty, new DateTime(2026, 1, 1), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 'c', Color.Red,
        ];
        foreach (var d in Enum.GetValues<DatabaseDialect>())
            foreach (var v in values) yield return new object[] { d, v };
    }

    private static CompiledQuery Compile(DatabaseDialect d, Func<Query, Query> build) => build(Query.For(d).Table("docs")).Compile();

    [Theory]
    [MemberData(nameof(Scalars))]
    public void A_scalar_compiles_on_every_dialect_and_is_a_binding(DatabaseDialect d, object value)
    {
        var compiled = Compile(d, q => q.WhereJsonContains("payload->tags", value));

        compiled.Bindings.Should().NotBeEmpty();
        compiled.Sql.Should().NotContain("2.5");
    }

    [Theory]
    [MemberData(nameof(NonScalars))]
    public void A_non_scalar_is_accepted_by_MySQL_and_PostgreSQL_and_refused_by_SQLite_and_SQL_Server(DatabaseDialect d, object value)
    {
        var act = () => Compile(d, q => q.WhereJsonContains("payload->tags", value));

        if (ScalarOnly.Contains(d)) act.Should().Throw<NotSupportedException>().WithMessage($"*{d}*");
        else act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(NonScalars))]
    public void The_negated_and_or_forms_follow_the_same_input_rules(DatabaseDialect d, object value)
    {
        Func<Query, Query>[] forms =
        [
            q => q.WhereJsonDoesntContain("payload->tags", value),
            q => q.OrWhereJsonContains("payload->tags", value),
            q => q.OrWhereJsonDoesntContain("payload->tags", value),
        ];

        foreach (var form in forms)
        {
            var act = () => Compile(d, form);
            if (ScalarOnly.Contains(d)) act.Should().Throw<NotSupportedException>();
            else act.Should().NotThrow();
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_null_value_is_rejected_at_the_call_on_every_dialect(DatabaseDialect d)
    {
        var act = () => Query.For(d).Table("docs").WhereJsonContains("payload->tags", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_column_without_a_path_searches_the_whole_document_and_compiles(DatabaseDialect d)
    {
        var compiled = Compile(d, q => q.WhereJsonContains("payload", "php"));

        compiled.Sql.Should().Contain("payload");
        compiled.Bindings.Should().HaveCount(1, "no path means only the value is bound");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_path_is_bound_as_well_as_the_value(DatabaseDialect d)
    {
        Compile(d, q => q.WhereJsonContains("payload->tags", "php")).Bindings.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_hostile_value_or_path_never_reaches_the_sql_text(DatabaseDialect d)
    {
        const string payload = "x'); drop table docs; --";

        Compile(d, q => q.WhereJsonContains("payload->tags", payload)).Sql.Should().NotContain("drop table");

        var act = () => Query.For(d).Table("docs").WhereJsonContains("payload->tags'); drop table docs; --", "x");
        act.Should().Throw<ArgumentException>("a path segment may only contain letters, digits and underscores");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void A_json_key_check_needs_a_path_and_a_length_cannot_be_negative(DatabaseDialect d)
    {
        var key = () => Query.For(d).Table("docs").WhereJsonContainsKey("payload");
        var length = () => Query.For(d).Table("docs").WhereJsonLength("payload->tags", -1);

        key.Should().Throw<ArgumentException>();
        length.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_engine_function_used_is_the_one_the_docs_name()
    {
        Compile(DatabaseDialect.Sqlite, q => q.WhereJsonContains("p->t", "x")).Sql.Should().Contain("json_each");
        Compile(DatabaseDialect.MySql, q => q.WhereJsonContains("p->t", "x")).Sql.Should().Contain("json_contains(");
        Compile(DatabaseDialect.Postgres, q => q.WhereJsonContains("p->t", "x")).Sql.Should().Contain("@>");
        Compile(DatabaseDialect.SqlServer, q => q.WhereJsonContains("p->t", "x")).Sql.Should().Contain("openjson(");
    }
}
