using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>R24.T05: row locks (<c>SharedLock</c>, <c>LockForUpdate</c>) on every grammar. Pure strings.</summary>
public class QueryLockTests
{
    private static readonly DatabaseDialect[] WithRowLocks = [DatabaseDialect.MySql, DatabaseDialect.Postgres, DatabaseDialect.SqlServer];

    private const string SharedHint = "with (rowlock, holdlock)";
    private const string UpdateHint = "with (rowlock, updlock, holdlock)";

    private static string Quoted(string template, DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.MySql => template.Replace('«', '`').Replace('»', '`'),
        DatabaseDialect.SqlServer => template.Replace('«', '[').Replace('»', ']'),
        _ => template.Replace('«', '"').Replace('»', '"'),
    };

    private static Query Q(DatabaseDialect d, string table) => Query.For(d).Table(table);

    /// <summary>MySQL and PostgreSQL share one text (a clause at the end), SQL Server has its own (a table hint).</summary>
    private static void Verify(string name, Func<DatabaseDialect, Query> build, string clauseSql, string hintSql, params object?[] bindings)
    {
        foreach (var dialect in WithRowLocks)
        {
            var compiled = build(dialect).Compile();
            var template = dialect == DatabaseDialect.SqlServer ? hintSql : clauseSql;

            compiled.Sql.Should().Be(Quoted(template, dialect), $"case '{name}' on {dialect}");
            compiled.Bindings.Should().Equal(bindings, $"case '{name}' bindings on {dialect}");
        }
    }

    [Fact]
    public void A_shared_lock_is_for_share_or_a_holdlock_hint()
    {
        Verify("shared", d => Q(d, "users").Where("id", 1).SharedLock(),
            "select * from «users» where «id» = @p0 for share",
            "select * from «users» " + SharedHint + " where «id» = @p0", 1);
    }

    [Fact]
    public void A_lock_for_update_is_for_update_or_an_updlock_hint()
    {
        Verify("update", d => Q(d, "users").Where("id", 1).LockForUpdate(),
            "select * from «users» where «id» = @p0 for update",
            "select * from «users» " + UpdateHint + " where «id» = @p0", 1);
    }

    [Fact]
    public void The_last_lock_wins()
    {
        Verify("shared then update", d => Q(d, "users").SharedLock().LockForUpdate(),
            "select * from «users» for update",
            "select * from «users» " + UpdateHint);
        Verify("update then shared", d => Q(d, "users").LockForUpdate().SharedLock(),
            "select * from «users» for share",
            "select * from «users» " + SharedHint);
    }

    [Fact]
    public void The_clause_comes_after_ordering_and_limit_and_the_hint_after_the_table()
    {
        Verify("order and limit", d => Q(d, "users").OrderBy("id").Limit(1).LockForUpdate(),
            "select * from «users» order by «id» asc limit 1 for update",
            "select top (1) * from «users» " + UpdateHint + " order by «id» asc");
        Verify("offset", d => Q(d, "users").OrderBy("id").Limit(5).Offset(10).SharedLock(),
            "select * from «users» order by «id» asc limit 5 offset 10 for share",
            "select * from «users» " + SharedHint + " order by «id» asc offset 10 rows fetch next 5 rows only");
    }

    [Fact]
    public void A_table_alias_comes_before_the_hint()
    {
        Verify("alias", d => Q(d, "users as u").Where("u.id", 1).LockForUpdate(),
            "select * from «users» as «u» where «u».«id» = @p0 for update",
            "select * from «users» as «u» " + UpdateHint + " where «u».«id» = @p0", 1);
    }

    [Fact]
    public void A_join_keeps_the_hint_on_the_base_table_only()
    {
        Verify("join", d => Q(d, "users").Join("posts", "users.id", "posts.user_id").Where("posts.votes", 1).LockForUpdate(),
            "select * from «users» inner join «posts» on «users».«id» = «posts».«user_id» where «posts».«votes» = @p0 for update",
            "select * from «users» " + UpdateHint + " inner join «posts» on «users».«id» = «posts».«user_id» where «posts».«votes» = @p0", 1);
    }

    [Fact]
    public void A_lock_inside_a_subquery_stays_inside_it()
    {
        Verify("derived table", d => Query.For(d).FromSub(Q(d, "u").Where("x", 1).LockForUpdate(), "t").Where("y", 2),
            "select * from (select * from «u» where «x» = @p0 for update) as «t» where «y» = @p1",
            "select * from (select * from «u» " + UpdateHint + " where «x» = @p0) as «t» where «y» = @p1", 1, 2);
        Verify("where in", d => Q(d, "a").WhereIn("id", s => s.Table("b").Select("a_id").SharedLock()),
            "select * from «a» where «id» in (select «a_id» from «b» for share)",
            "select * from «a» where «id» in (select «a_id» from «b» " + SharedHint + ")");
    }

    [Fact]
    public void A_lock_adds_no_bindings_and_survives_clone_without_leaking_back()
    {
        var plain = Q(DatabaseDialect.Postgres, "users").Where("id", 1);
        var locked = plain.Clone().LockForUpdate();

        plain.ToSql().Should().Be("select * from \"users\" where \"id\" = @p0");
        locked.ToSql().Should().Be("select * from \"users\" where \"id\" = @p0 for update");
        locked.Clone().Where("x", 2).ToSql().Should().EndWith("for update");
        locked.GetBindings().Should().Equal(1);
    }

    [Fact]
    public void ToRawSql_shows_the_lock()
    {
        Q(DatabaseDialect.MySql, "users").Where("id", 7).LockForUpdate().ToRawSql().Should().Be("select * from `users` where `id` = 7 for update");
        Q(DatabaseDialect.SqlServer, "users").Where("id", 7).SharedLock().ToRawSql().Should().Be("select * from [users] with (rowlock, holdlock) where [id] = 7");
    }

    [Fact]
    public void SQLite_refuses_a_lock_when_the_query_is_compiled()
    {
        var shared = Q(DatabaseDialect.Sqlite, "users").SharedLock();
        var update = Q(DatabaseDialect.Sqlite, "users").LockForUpdate();
        var inner = Query.For(DatabaseDialect.Sqlite).FromSub(Q(DatabaseDialect.Sqlite, "u").LockForUpdate(), "t");

        FluentActions.Invoking(() => shared.Compile()).Should().Throw<NotSupportedException>().WithMessage("*Sqlite*");
        FluentActions.Invoking(() => update.ToSql()).Should().Throw<NotSupportedException>().WithMessage("*Sqlite*");
        FluentActions.Invoking(() => inner.Compile()).Should().Throw<NotSupportedException>().WithMessage("*Sqlite*");
        Q(DatabaseDialect.Sqlite, "users").Compile().Sql.Should().Be("select * from \"users\"");
    }

    [Fact]
    public void SQL_Server_cannot_hint_a_derived_table_or_raw_source()
    {
        var derived = Query.For(DatabaseDialect.SqlServer).FromSub(Q(DatabaseDialect.SqlServer, "u"), "t").LockForUpdate();
        var raw = Query.For(DatabaseDialect.SqlServer).FromRaw("users as u").SharedLock();

        FluentActions.Invoking(() => derived.Compile()).Should().Throw<NotSupportedException>().WithMessage("*SqlServer*");
        FluentActions.Invoking(() => raw.Compile()).Should().Throw<NotSupportedException>().WithMessage("*SqlServer*");

        foreach (var dialect in new[] { DatabaseDialect.MySql, DatabaseDialect.Postgres })
        {
            Query.For(dialect).FromSub(Q(dialect, "u"), "t").LockForUpdate().Compile().Sql
                .Should().Be(Quoted("select * from (select * from «u») as «t» for update", dialect), dialect.ToString());
            Query.For(dialect).FromRaw("users as u").SharedLock().Compile().Sql
                .Should().Be("select * from users as u for share", dialect.ToString());
        }
    }

    [Fact]
    public void A_lock_on_a_union_or_one_of_its_terms_is_refused_on_every_dialect()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var before = Q(dialect, "a").LockForUpdate().Union(Q(dialect, "b"));
            var after = Q(dialect, "a").Union(Q(dialect, "b")).SharedLock();
            var term = Q(dialect, "a").UnionAll(Q(dialect, "b").LockForUpdate());

            FluentActions.Invoking(() => before.Compile()).Should().Throw<InvalidOperationException>().WithMessage("*Union*", dialect.ToString());
            FluentActions.Invoking(() => after.Compile()).Should().Throw<InvalidOperationException>().WithMessage("*Union*", dialect.ToString());
            FluentActions.Invoking(() => term.Compile()).Should().Throw<InvalidOperationException>().WithMessage("*Union*", dialect.ToString());
        }
    }

    [Fact]
    public void A_write_with_a_lock_is_refused_instead_of_ignoring_it()
    {
        foreach (var dialect in Enum.GetValues<DatabaseDialect>())
        {
            var values = new Dictionary<string, object?> { ["v"] = 1 };
            var locked = Q(dialect, "t").Where("x", 1).LockForUpdate();

            FluentActions.Invoking(() => locked.CompileUpdate(values)).Should().Throw<InvalidOperationException>().WithMessage("*lock*", dialect.ToString());
            FluentActions.Invoking(() => locked.CompileDelete()).Should().Throw<InvalidOperationException>().WithMessage("*lock*", dialect.ToString());
            FluentActions.Invoking(() => Q(dialect, "t").SharedLock().CompileInsert([values], false)).Should().Throw<InvalidOperationException>().WithMessage("*lock*", dialect.ToString());
            FluentActions.Invoking(() => Q(dialect, "t").LockForUpdate().CompileTruncate(false)).Should().Throw<InvalidOperationException>().WithMessage("*lock*", dialect.ToString());
        }
    }
}
