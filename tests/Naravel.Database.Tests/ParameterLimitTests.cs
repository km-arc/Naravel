using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>MaxParameters boundary values (PDR-022 A4): just below, at and just above the limit, per dialect. Pure strings, no database.</summary>
public class ParameterLimitTests
{
    [Fact]
    public void Each_dialect_exposes_the_documented_limit()
    {
        Grammar.For(DatabaseDialect.SqlServer).MaxParameters.Should().Be(2098);
        Grammar.For(DatabaseDialect.Sqlite).MaxParameters.Should().Be(32766);
        Grammar.For(DatabaseDialect.MySql).MaxParameters.Should().Be(65535);
        Grammar.For(DatabaseDialect.Postgres).MaxParameters.Should().Be(65535);
    }

    [Fact]
    public void Below_and_at_the_limit_compile_and_above_it_fails_with_dialect_limit_and_count()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var limit = Grammar.For(dialect).MaxParameters;
            foreach (var count in new[] { limit - 1, limit })
            {
                var compiled = Query.For(dialect).Table("t").WhereIn("id", Enumerable.Range(1, count)).Compile();
                compiled.Bindings.Should().HaveCount(count, $"{dialect} with {count} values");
            }

            var act = () => Query.For(dialect).Table("t").WhereIn("id", Enumerable.Range(1, limit + 1)).Compile();
            act.Should().Throw<InvalidOperationException>().WithMessage($"*{dialect}*{limit}*{limit + 1}*");
        }
    }

    [Fact]
    public void Parameters_from_other_clauses_count_toward_the_same_limit()
    {
        var limit = Grammar.For(DatabaseDialect.SqlServer).MaxParameters;

        var atLimit = Query.For(DatabaseDialect.SqlServer).Table("t").Where("a", 1).WhereIn("id", Enumerable.Range(1, limit - 1));
        var over = Query.For(DatabaseDialect.SqlServer).Table("t").Where("a", 1).Where("b", 2).WhereIn("id", Enumerable.Range(1, limit - 1));

        atLimit.Compile().Bindings.Should().HaveCount(limit);
        ((Action)(() => over.Compile())).Should().Throw<InvalidOperationException>();
    }
}
