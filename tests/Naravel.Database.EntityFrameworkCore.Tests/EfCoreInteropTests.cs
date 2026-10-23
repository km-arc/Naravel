using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database.EntityFrameworkCore;

namespace Naravel.Database.EntityFrameworkCore.Tests;

public sealed class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class PeopleContext(DbContextOptions<PeopleContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
}

public sealed class EfCoreInteropTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private PeopleContext _context = null!;
    private ServiceProvider _provider = null!;
    private IDatabase _naravel = null!;

    public async Task InitializeAsync()
    {
        // The EF context owns an in-memory SQLite database through a connection we keep open; the builder must run on that same connection.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _context = new PeopleContext(new DbContextOptionsBuilder<PeopleContext>().UseSqlite(_connection).Options);
        await _context.Database.EnsureCreatedAsync();

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
        _naravel = _provider.GetRequiredService<IDatabase>();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private Task<int> CountAsync() => _context.People.AsNoTracking().CountAsync();

    private static Task InsertAsync(IDatabase db, int id, string name) =>
        db.StatementAsync("insert into People (Id, Name) values (?, ?)", [id, name]);

    [Fact]
    public async Task Query_reads_rows_the_context_wrote()
    {
        _context.People.Add(new Person { Id = 1, Name = "Ada" });
        await _context.SaveChangesAsync();

        var rows = await _context.Query("People").OrderBy("Id").GetAsync();

        rows.Should().HaveCount(1);
        rows[0]["Name"].Should().Be("Ada");
    }

    [Fact]
    public async Task Using_runs_raw_sql_on_the_context_connection()
    {
        var db = _naravel.Using(_context);

        await InsertAsync(db, 1, "Grace");

        (await CountAsync()).Should().Be(1);
        (await db.ScalarAsync<string>("select Name from People where Id = ?", [1])).Should().Be("Grace");
    }

    [Fact]
    public async Task An_EF_write_and_a_builder_write_commit_together()
    {
        var db = _naravel.Using(_context);

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            _context.People.Add(new Person { Id = 1, Name = "Ada" });
            await _context.SaveChangesAsync();
            await InsertAsync(db, 2, "Grace");
            await transaction.CommitAsync();
        }

        (await CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task An_EF_write_and_a_builder_write_roll_back_together()
    {
        var db = _naravel.Using(_context);

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            _context.People.Add(new Person { Id = 1, Name = "Ada" });
            await _context.SaveChangesAsync();
            await InsertAsync(db, 2, "Grace");
            await transaction.RollbackAsync();
        }

        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_adapter_never_commits_a_transaction_it_did_not_begin()
    {
        var db = _naravel.Using(_context);

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await InsertAsync(db, 1, "Grace");
            await _context.Query("People").Where("Id", 1).FirstAsync();
            // EF's transaction is disposed without a commit: if the builder had committed it, the row would survive.
        }

        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_builder_works_without_a_transaction_too()
    {
        await InsertAsync(_naravel.Using(_context), 1, "Grace");

        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_borrowed_database_refuses_to_start_transactions()
    {
        var db = _naravel.Using(_context);

        var begin = () => db.BeginTransactionAsync();
        var callback = () => db.TransactionAsync(_ => Task.CompletedTask);

        await begin.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DbContext*");
        await callback.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DbContext*");
    }

    [Fact]
    public void A_borrowed_database_is_bound_to_the_context_connection()
    {
        var db = _naravel.Using(_context);

        var other = () => db.Connection("other");

        other.Should().Throw<InvalidOperationException>();
        db.Connection(null).Should().BeSameAs(db);
    }
}
