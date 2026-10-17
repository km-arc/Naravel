using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>Hostile-input tables for the identifier grammar frozen by PDR-022 A3, run against every dialect and every caller.</summary>
public class IdentifierSafetyTests
{
    private static readonly DatabaseDialect[] AllDialects = Enum.GetValues<DatabaseDialect>();

    private static readonly string[] Hostile =
    [
        "", " ", "a b", "a;drop table x", "a--", "a\"b", "a`b", "a]b", "[a]", "a'b", "a\n", "a\r\nb", "a\0b", "a.b.c.d",
        "1abc", "a..b", ".a", "a.", "a as", "a as b c", "a as 1", "a as b as c", "a)", "a(", "a/*x*/", "a,b", "a=1", "a or 1=1",
        "users.*.id", "a->", "->a", "a->b c", "a->b;drop", "a-b", "a\tb", "name​", new string('a', 129), "a." + new string('b', 129),
    ];

    [Fact]
    public void Every_hostile_identifier_is_rejected_at_the_call_by_every_caller_on_every_dialect()
    {
        foreach (var dialect in AllDialects)
        {
            var grammar = Grammar.For(dialect);
            foreach (var text in Hostile)
            {
                var label = $"{dialect}: '{text.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\0", "\\0", StringComparison.Ordinal)}'";
                var query = Query.For(dialect);

                ((Action)(() => grammar.Wrap(text))).Should().Throw<ArgumentException>(label);
                ((Action)(() => query.Table(text))).Should().Throw<ArgumentException>(label);
                ((Action)(() => query.Select(text))).Should().Throw<ArgumentException>(label);
                ((Action)(() => query.Where(text, 1))).Should().Throw<ArgumentException>(label);
                ((Action)(() => query.WhereIn(text, new[] { 1 }))).Should().Throw<ArgumentException>(label);
                ((Action)(() => query.OrderBy(text))).Should().Throw<ArgumentException>(label);
            }
        }
    }

    [Fact]
    public void Star_and_alias_are_only_accepted_where_the_grammar_allows_them()
    {
        foreach (var dialect in AllDialects)
        {
            var query = Query.For(dialect).Table("t");

            query.Select("*", "t.*").ToSql().Should().NotBeNullOrEmpty();
            ((Action)(() => query.Where("*", 1))).Should().Throw<ArgumentException>();
            ((Action)(() => query.Where("t.*", 1))).Should().Throw<ArgumentException>();
            ((Action)(() => query.Where("a as b", 1))).Should().Throw<ArgumentException>();
            ((Action)(() => query.OrderBy("a as b"))).Should().Throw<ArgumentException>();
            ((Action)(() => query.Table("*"))).Should().Throw<ArgumentException>();
            ((Action)(() => query.Select("* as x"))).Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void Valid_json_paths_are_recognised_but_not_supported_yet_and_never_reach_the_sql()
    {
        foreach (var dialect in AllDialects)
        {
            var query = Query.For(dialect).Table("t");

            ((Action)(() => query.Where("data->name", 1))).Should().Throw<NotSupportedException>();
            ((Action)(() => query.Select("data->list[0]"))).Should().Throw<NotSupportedException>();
        }
    }

    [Fact]
    public void Quoting_is_per_dialect_and_unicode_letters_and_dollar_are_allowed()
    {
        Grammar.For(DatabaseDialect.Sqlite).Wrap("schema.users.id as user_id").Should().Be("\"schema\".\"users\".\"id\" as \"user_id\"");
        Grammar.For(DatabaseDialect.Postgres).Wrap("users.*").Should().Be("\"users\".*");
        Grammar.For(DatabaseDialect.MySql).Wrap("_a$1").Should().Be("`_a$1`");
        Grammar.For(DatabaseDialect.SqlServer).Wrap("نام").Should().Be("[نام]");
        Grammar.For(DatabaseDialect.SqlServer).Wrap(new string('a', 128)).Should().Be("[" + new string('a', 128) + "]");
    }

    [Fact]
    public void A_value_is_never_written_into_the_sql_even_when_it_looks_like_sql()
    {
        string[] payloads = ["'; drop table users; --", "\" or 1=1 --", "`x`", "]; drop", "a\\'"];
        foreach (var dialect in AllDialects)
        {
            foreach (var payload in payloads)
            {
                var compiled = Query.For(dialect).Table("t").Where("a", payload).WhereIn("b", new[] { payload }).Compile();

                compiled.Sql.Should().NotContain("drop").And.NotContain("1=1").And.NotContain(payload);
                compiled.Bindings.Should().Equal(payload, payload);
            }
        }
    }
}
