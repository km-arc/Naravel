using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database.Testing;
using Naravel.Foundation;

namespace Naravel.Database.Tests;

// Runs the quickstart, the Testing examples and the messages of docs/en/database.md.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    // The service of the "DatabaseFake" example: it only needs IDatabase, so a fake or a real database can stand in.
    public sealed class LeaderboardService(IDatabase db)
    {
        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TopAsync(int minVotes, CancellationToken ct = default) =>
            db.Table("users").Where("votes", ">=", minVotes).OrderByDesc("votes").Limit(10).GetAsync(ct);
    }

    // The registration of the quickstart, with the settings of its JSON passed as keys.
    private static ServiceProvider Build(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value)).Build();
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider InMemory() => Build(
        ("Database:Default", "main"),
        ("Database:Connections:main:Driver", "sqlite"),
        ("Database:Connections:main:Path", ":memory:"));

    private static async Task<IDatabase> WithUsersAsync(ServiceProvider provider)
    {
        var db = provider.GetRequiredService<IDatabase>();
        await db.UnpreparedAsync("create table users (id integer primary key, name text not null, votes integer not null)");
        await db.StatementAsync("insert into users (id, name, votes) values (?, ?, ?), (?, ?, ?)", [1, "Ada", 10, 2, "Grace", 10]);
        return db;
    }

    [Fact]
    public async Task The_quickstart_creates_a_table_inserts_a_row_and_reads_it_back()
    {
        await using var provider = InMemory();
        var db = provider.GetRequiredService<IDatabase>();

        await db.UnpreparedAsync("create table if not exists notes (id integer primary key autoincrement, title text not null, done integer not null default 0)");
        var inserted = await db.Table("notes").InsertAsync(new Dictionary<string, object?> { ["title"] = "Buy milk" });
        var open = await db.Table("notes").Where("done", false).OrderBy("id").GetAsync();

        inserted.Should().Be(1);
        open.Should().ContainSingle();
        open[0]["title"].Should().Be("Buy milk");
        open[0]["id"].Should().Be(1L);
    }

    [Fact]
    public async Task The_fake_example_asserts_on_the_sql_and_the_bindings()
    {
        var fake = new DatabaseFake().ReturnRows([new Dictionary<string, object?> { ["id"] = 1 }], sqlContains: "from \"users\"");

        var rows = await new LeaderboardService(fake).TopAsync(10);

        rows.Should().ContainSingle();
        fake.AssertExecuted(q => q.Sql.Contains("\"votes\" >=") && (int)q.Bindings[0]! == 10);
    }

    [Fact]
    public async Task The_fake_assertions_fail_with_the_messages_in_the_guide()
    {
        var fake = new DatabaseFake();

        FluentActions.Invoking(() => fake.AssertExecuted())
            .Should().Throw<InvalidOperationException>().WithMessage("Expected a database command to run, but none did.");

        await new LeaderboardService(fake).TopAsync(10);

        FluentActions.Invoking(() => fake.AssertExecutedCount(2))
            .Should().Throw<InvalidOperationException>().WithMessage("Expected 2 database command(s), but found 1: *");
        FluentActions.Invoking(() => fake.AssertNothingExecuted())
            .Should().Throw<InvalidOperationException>().WithMessage("Expected no database command, but 1 ran: *");
        FluentActions.Invoking(() => fake.AssertExecuted(q => q.Sql.Contains("posts")))
            .Should().Throw<InvalidOperationException>().WithMessage("No database command matched the predicate; 1 command(s) ran: *");
    }

    [Fact]
    public void Setup_errors_carry_the_messages_in_the_guide()
    {
        using var noDefault = Build(("Database:Connections:main:Driver", "sqlite"), ("Database:Connections:main:Path", ":memory:"));
        FluentActions.Invoking(() => noDefault.GetRequiredService<IDatabase>().Table("users"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("No default driver is configured for IDatabaseConnection. Set the 'Default' option or pass a driver name explicitly.");

        using var provider = InMemory();
        FluentActions.Invoking(() => provider.GetRequiredService<IDatabase>().Connection("reports").Table("users"))
            .Should().Throw<DriverNotRegisteredException>()
            .WithMessage("Driver 'reports' is not registered for IDatabaseConnection. Available drivers: main.");

        using var noPath = Build(("Database:Default", "main"), ("Database:Connections:main:Driver", "sqlite"));
        FluentActions.Invoking(() => noPath.GetRequiredService<IDatabase>().Table("users"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The sqlite database connection 'main' needs either 'ConnectionString' or 'Path' in its configuration section.");
    }

    [Fact]
    public void Compile_errors_carry_the_messages_in_the_guide()
    {
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").Where("name", "ilike", "a%").ToSql())
            .Should().Throw<NotSupportedException>().WithMessage("The operator 'ilike' is not supported by the Sqlite dialect.");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").Where("name", "drop", "x").ToSql())
            .Should().Throw<ArgumentException>().WithMessage("'drop' is not an allowed operator.*");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users; drop table x").ToSql())
            .Should().Throw<ArgumentException>()
            .WithMessage("'users; drop table x' is not a valid identifier. Use letters, digits and underscores in 'name', 'table.name', " +
                         "'schema.table.name' or 'source as alias'; use a Raw method for anything else.*");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.SqlServer).Table("users").Offset(5).ToSql())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("SQL Server needs OrderBy(...) when Offset or Skip is used, because OFFSET ... FETCH requires ORDER BY.");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.SqlServer).Table("t").WhereIn("id", Enumerable.Range(1, 2099)).ToSql())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The SqlServer dialect allows at most 2098 parameters per statement, but this query has 2099.");

        // A name longer than 64 characters is cut in the message.
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table(new string('a', 70) + "-x").ToSql())
            .Should().Throw<ArgumentException>().WithMessage($"'{new string('a', 64)}...' is not a valid identifier.*");
        FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").WhereRaw("votes > ?"))
            .Should().Throw<ArgumentException>()
            .WithMessage("The raw SQL has 1 '?' placeholders but 0 bindings were given. Write '??' for a literal question mark.*")
            .WithParameterName("bindings");
    }

    [Fact]
    public async Task Running_errors_carry_the_messages_in_the_guide()
    {
        await using var provider = InMemory();
        var db = await WithUsersAsync(provider);

        await FluentActions.Invoking(() => db.Table("users").Where("votes", 99).SoleAsync())
            .Should().ThrowAsync<RecordsNotFoundException>().WithMessage("No record matched the query.");
        await FluentActions.Invoking(() => db.Table("users").Where("votes", 10).SoleAsync())
            .Should().ThrowAsync<MultipleRecordsFoundException>().WithMessage("More than one record matched the query.");

        await FluentActions.Invoking(() => db.Table("users").UpdateAsync(new Dictionary<string, object?> { ["votes"] = 0 }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This update has no where, so it would change every row. Add a Where(...), or call AllRows() or UpdateAllAsync to confirm that you mean every row.");
        await FluentActions.Invoking(() => db.Table("users").DeleteAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This delete has no where, so it would remove every row. Add a Where(...), or call AllRows() or DeleteAllAsync to confirm that you mean " +
                         "every row, or TruncateAsync to empty the table and reset its counter.");

        await FluentActions.Invoking(() => db.Table("users").ChunkAsync(1, _ => Task.FromResult(true)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Chunking with offset needs an OrderBy: pages of an unordered query are not stable. Add OrderBy(<unique column>), or use ChunkByIdAsync.");

        IReadOnlyDictionary<string, object?>[] uneven =
        [
            new Dictionary<string, object?> { ["id"] = 3, ["name"] = "Linus" },
            new Dictionary<string, object?> { ["id"] = 4 },
        ];
        await FluentActions.Invoking(() => db.Table("users").InsertAsync(uneven))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("Every row must have exactly the columns of the first row (id, name).*");

        await FluentActions.Invoking(() => db.SelectAsync("select * from users where id = ?", Array.Empty<object?>()))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("The SQL has 1 placeholder(s) but 0 binding(s) were given. Use '??' for a literal question mark.*");

        await FluentActions.Invoking(() => Query.For(DatabaseDialect.Sqlite).Table("users").GetAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This query is not connected to a database. Start it with IDatabase.Table(...) instead of Query.For(...), which only compiles SQL.");
    }

    [Fact]
    public async Task Transaction_errors_carry_the_messages_in_the_guide()
    {
        await using var provider = Build(
            ("Database:Default", "main"),
            ("Database:Connections:main:Driver", "sqlite"),
            ("Database:Connections:main:Path", ":memory:"),
            ("Database:Connections:other:Driver", "sqlite"),
            ("Database:Connections:other:Path", ":memory:"));
        var db = provider.GetRequiredService<IDatabase>();

        await FluentActions.Invoking(() => db.TransactionAsync(async tx => { await tx.BeginTransactionAsync(); }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Already inside a transaction. Use TransactionAsync on this database to open a savepoint.");

        await FluentActions.Invoking(() => db.TransactionAsync(async tx =>
            {
                tx.Connection("other");
                await Task.CompletedTask;
            }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A transaction is bound to one connection; start a new transaction on the other connection instead.");

        var attempts = await FluentActions.Invoking(() => db.TransactionAsync(_ => Task.CompletedTask, attempts: 0))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        attempts.Which.ParamName.Should().Be("attempts");

        await using var manual = await db.BeginTransactionAsync();
        await manual.CommitAsync();
        await FluentActions.Invoking(() => manual.CommitAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("The transaction was already committed or rolled back.");
    }
}
