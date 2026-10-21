using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R24.T04: raw expressions, the Raw value and the injection rules. Pure strings.</summary>
public class QueryRawTests
{
    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static Query Users(DatabaseDialect d) => Query.For(d).Table("users");

    private static void Verify(string name, Func<DatabaseDialect, CompiledQuery> compile, string sql, params object?[] bindings)
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = compile(dialect);

            compiled.Sql.Should().Be(Quoted(sql, dialect), $"case '{name}' on {dialect}");
            compiled.Bindings.Should().Equal(bindings, $"case '{name}' bindings on {dialect}");
        }
    }

    [Fact]
    public void GroupByRaw_and_HavingRaw_bind_their_values()
    {
        Verify("group by raw", d => Users(d).GroupBy("a").GroupByRaw("lower(b)").Compile(), "select * from «users» group by «a», lower(b)");
        Verify("group by raw binding", d => Users(d).GroupByRaw("substr(b, ?, 1)", 2).Compile(), "select * from «users» group by substr(b, @p0, 1)", 2);
        Verify("having raw", d => Users(d).GroupBy("a").HavingRaw("sum(x) > ?", 5).Compile(), "select * from «users» group by «a» having sum(x) > @p0", 5);
        Verify("or having raw", d => Users(d).GroupBy("a").Having("a", 1).OrHavingRaw("sum(x) > ?", 5).Compile(), "select * from «users» group by «a» having «a» = @p0 or sum(x) > @p1", 1, 5);
    }

    [Fact]
    public void FromRaw_replaces_the_table_and_binds_in_text_order()
    {
        Verify("from raw", d => Query.For(d).FromRaw("generate_series(?, ?) as g", 1, 3).Compile(), "select * from generate_series(@p0, @p1) as g", 1, 3);
        Verify("every raw part in text order", d => Query.For(d)
                .FromRaw("generate_series(?, ?) as g", 1, 3)
                .SelectRaw("g * ?", 2)
                .WhereRaw("g > ?", 0)
                .GroupByRaw("g % ?", 4)
                .HavingRaw("count(*) > ?", 6)
                .OrderByRaw("g % ?", 7).Compile(),
            "select g * @p0 from generate_series(@p1, @p2) as g where g > @p3 group by g % @p4 having count(*) > @p5 order by g % @p6", 2, 1, 3, 0, 4, 6, 7);
        Verify("table after from raw", d => Query.For(d).FromRaw("x as y").Table("users").Compile(), "select * from «users»");
        Verify("from raw after table", d => Users(d).FromRaw("x as y").Compile(), "select * from x as y");
    }

    [Fact]
    public void Raw_as_a_value_is_written_as_text_with_its_own_bindings()
    {
        Verify("where value", d => Users(d).Where("votes", ">", new Raw("avg_votes + ?", 1)).Compile(), "select * from «users» where «votes» > avg_votes + @p0", 1);
        Verify("literal question mark", d => Users(d).Where("a", new Raw("b ?? c")).Compile(), "select * from «users» where «a» = b ? c");
        Verify("update value", d => Users(d).Where("id", 7).CompileUpdate(new Dictionary<string, object?> { ["votes"] = new Raw("votes + ?", 1) }), "update «users» set «votes» = votes + @p0 where «id» = @p1", 1, 7);
        Verify("insert value", d => Users(d).CompileInsert([new Dictionary<string, object?> { ["name"] = "x", ["made"] = new Raw("2 * 3") }], false).Single(), "insert into «users» («name», «made») values (@p0, 2 * 3)", "x");
        Verify("bindings keep text order around a raw value", d => Users(d).Where("a", 1).Where("b", new Raw("? + ?", 2, 3)).Where("c", 4).Compile(),
            "select * from «users» where «a» = @p0 and «b» = @p1 + @p2 and «c» = @p3", 1, 2, 3, 4);
    }

    [Fact]
    public void A_raw_value_that_pushes_a_statement_past_the_limit_is_reported()
    {
        var rows = Enumerable.Range(0, 11_000).Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["v"] = new Raw("? + ? + ?", i, 1, 2) }).ToArray();

        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).CompileInsert(rows, false)).Should().Throw<InvalidOperationException>().WithMessage("*Raw value*");
    }

    [Fact]
    public void Raw_checks_its_placeholders()
    {
        FluentActions.Invoking(() => new Raw("a = ?")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Raw("a = 1", 5)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Raw(" ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Raw(null!)).Should().Throw<ArgumentException>();
        new Raw("a = ? and b = ?", 1, "x").Bindings.Should().Equal(1, "x");
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).HavingRaw("a = ?")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).GroupByRaw("a", 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Users(DatabaseDialect.Sqlite).FromRaw("a = ?")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_query_built_on_FromRaw_is_read_only()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var raw = Query.For(dialect).FromRaw("x as y").Where("a", 1);
            FluentActions.Invoking(() => raw.CompileUpdate(new Dictionary<string, object?> { ["v"] = 1 })).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => raw.CompileDelete()).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void A_value_is_never_written_into_the_sql_text()
    {
        const string attack = "x'; drop table users; --";
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var compiled = Users(dialect).Where("name", attack).WhereRaw("note = ?", attack).Having("name", attack).GroupBy("name").Compile();

            compiled.Sql.Should().NotContain("drop table", dialect.ToString());
            compiled.Bindings.Should().Contain(attack);
        }

        Users(DatabaseDialect.Sqlite).Where("name", attack).ToRawSql().Should().Contain("'x''; drop table users; --'", "the display form doubles the quote");
    }

    [Theory]
    [InlineData("a; drop table x")]
    [InlineData("a b")]
    [InlineData("a--")]
    [InlineData("a\" or 1=1 --")]
    [InlineData("a`")]
    public void An_identifier_is_never_taken_as_sql(string text)
    {
        var query = Users(DatabaseDialect.Sqlite);

        FluentActions.Invoking(() => query.Where(text, 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.OrderBy(text)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.GroupBy(text)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.Having(text, 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.Select(text)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.Join(text, "a.x", "b.y")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.Table(text)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("= 1 or 1 =")]
    [InlineData("like; drop")]
    public void An_operator_and_a_direction_come_from_a_fixed_list(string text)
    {
        var query = Users(DatabaseDialect.Sqlite);

        FluentActions.Invoking(() => query.Where("a", text, 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.OrderBy("a", text)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => query.Join("b", "a.x", text, "b.y")).Should().Throw<ArgumentException>();
    }

    /// <summary>The R24 decision: a public string parameter is a value or an identifier, and SQL text enters only through a parameter named <c>sql</c> of a <c>*Raw</c> method.</summary>
    [Fact]
    public void The_public_api_takes_sql_text_only_through_Raw_methods()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "alias", "column", "columns", "direction", "first", "highColumn", "idColumn", "keyColumn", "lowColumn", "name", "op", "pattern", "returning", "second", "table", "uniqueBy", "update", "value",
        };
        var offenders = new List<string>();
        var rawMethods = new List<MethodInfo>();

        foreach (var type in new[] { typeof(Query), typeof(JoinClause) })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in method.GetParameters().Where(p => IsText(p.ParameterType)))
                {
                    if (parameter.Name == "sql")
                    {
                        if (method.Name.EndsWith("Raw", StringComparison.Ordinal)) rawMethods.Add(method);
                        else offenders.Add($"{type.Name}.{method.Name}({parameter.Name}) takes SQL text but is not a *Raw method");
                    }
                    else if (!allowed.Contains(parameter.Name!))
                    {
                        offenders.Add($"{type.Name}.{method.Name}({parameter.Name}) is a string parameter this test does not know: is it a value, an identifier, or SQL text?");
                    }
                }
            }
        }

        offenders.Should().BeEmpty();
        rawMethods.Select(m => m.Name).Distinct().Should().BeEquivalentTo("SelectRaw", "WhereRaw", "OrWhereRaw", "HavingRaw", "OrHavingRaw", "OrderByRaw", "GroupByRaw", "FromRaw");
        foreach (var method in rawMethods) method.GetParameters().Select(p => p.Name).Should().Contain("bindings", $"{method.Name} must take bindings");
    }

    private static bool IsText(Type type) =>
        type == typeof(string) || type == typeof(string[]) || type == typeof(IEnumerable<string>) || type == typeof(IReadOnlyList<string>);
}

/// <summary>R24.T04: raw expressions run on in-memory SQLite.</summary>
public sealed class QueryRawExecutionTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private IDatabase _db = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Default"] = "main",
            ["Database:Connections:main:Driver"] = "sqlite",
            ["Database:Connections:main:Path"] = ":memory:"
        }).Build();
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<IDatabase>();

        await _db.UnpreparedAsync("create table scores (id integer primary key, team text not null, points integer not null)");
        await _db.UnpreparedAsync("insert into scores (id, team, points) values (1, 'red', 5), (2, 'red', 7), (3, 'blue', 1), (4, 'green', 9), (5, 'green', 9)");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task GroupByRaw_and_HavingRaw_run()
    {
        var teams = await _db.Table("scores").Select("team").GroupByRaw("team").HavingRaw("sum(points) > ?", 10).OrderBy("team").PluckAsync<string>("team");

        teams.Should().Equal("green", "red");
    }

    [Fact]
    public async Task FromRaw_reads_a_derived_table_with_bound_values()
    {
        var values = await _db.Table("x").FromRaw("(select ? as n union all select ?) as t", 1, 2).Select("n").OrderBy("n").PluckAsync<long>("n");

        values.Should().Equal(1L, 2L);
    }

    [Fact]
    public async Task A_raw_value_updates_a_column_from_itself()
    {
        var changed = await _db.Table("scores").Where("team", "red").UpdateAsync(new Dictionary<string, object?> { ["points"] = new Raw("points + ?", 10) });

        changed.Should().Be(2);
        (await _db.Table("scores").Where("team", "red").OrderBy("id").PluckAsync<long>("points")).Should().Equal(15L, 17L);
    }

    [Fact]
    public async Task A_raw_value_compares_in_where_and_inserts_a_computed_value()
    {
        var teams = await _db.Table("scores").Where("points", ">", new Raw("? + ?", 2, 3)).OrderBy("id").PluckAsync<string>("team");
        await _db.Table("scores").InsertAsync(new Dictionary<string, object?> { ["team"] = "pink", ["points"] = new Raw("2 * 3") });

        teams.Should().Equal("red", "green", "green");
        (await _db.Table("scores").Where("team", "pink").ValueAsync<long>("points")).Should().Be(6);
    }

    [Fact]
    public async Task An_attack_string_is_stored_as_data()
    {
        const string attack = "x'; drop table scores; --";
        await _db.Table("scores").InsertAsync(new Dictionary<string, object?> { ["team"] = attack, ["points"] = 0 });

        (await _db.Table("scores").WhereRaw("team = ?", attack).CountAsync()).Should().Be(1);
        (await _db.Table("scores").CountAsync()).Should().Be(6, "the table is still there");
    }
}
