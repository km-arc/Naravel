using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database.MySql;
using Naravel.Database.Postgres;
using Naravel.Database.SqlServer;
using Naravel.Testing;

namespace Naravel.Database.Tests;

/// <summary>
/// Transactions on the real servers. Each test runs only when its <c>NARAVEL_TEST_*</c> variable holds a connection string (CI <c>database</c> job);
/// skipped means NOT RUN. The deadlock tests make two transactions lock the same two rows in opposite order, so the server must pick a victim.
/// </summary>
public class ServerTransactionTests
{
    private static ServiceProvider Build(string driver, string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Default"] = "main",
            ["Database:Connections:main:Driver"] = driver,
            ["Database:Connections:main:ConnectionString"] = connectionString
        }).Build();
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelMySqlDatabase(configuration);
        services.AddNaravelPostgresDatabase(configuration);
        services.AddNaravelSqlServerDatabase(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task ForceDeadlockAsync(string driver, string environmentVariable)
    {
        await using var provider = Build(driver, Environment.GetEnvironmentVariable(environmentVariable)!);
        var db = provider.GetRequiredService<IDatabase>();
        var table = "nc_dl_" + Guid.NewGuid().ToString("N")[..8];
        await db.UnpreparedAsync($"create table {table} (id int not null primary key, n int not null)");
        try
        {
            await db.StatementAsync($"insert into {table} (id, n) values (?, ?), (?, ?)", [1, 0, 2, 0]);

            var firstHasItsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondHasItsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runs = 0;

            async Task Worker(int first, int second, TaskCompletionSource mine, TaskCompletionSource theirs)
            {
                var started = 0;
                await db.TransactionAsync(async tx =>
                {
                    Interlocked.Increment(ref runs);
                    var firstRun = started++ == 0;
                    await tx.StatementAsync($"update {table} set n = n + 1 where id = ?", [first]);
                    if (firstRun)
                    {
                        // Only the first attempt waits for the other transaction, so the retry cannot deadlock again.
                        mine.TrySetResult();
                        await theirs.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    }

                    await tx.StatementAsync($"update {table} set n = n + 1 where id = ?", [second]);
                }, attempts: 5);
            }

            await Task.WhenAll(
                Task.Run(() => Worker(1, 2, firstHasItsLock, secondHasItsLock)),
                Task.Run(() => Worker(2, 1, secondHasItsLock, firstHasItsLock))).WaitAsync(TimeSpan.FromSeconds(120));

            // Two callbacks plus at least one re-run after the server chose a deadlock victim.
            runs.Should().BeGreaterThan(2);

            // Both transactions finally committed exactly once: each row was incremented by each of them.
            (await db.ScalarAsync<long>($"select n from {table} where id = ?", [1])).Should().Be(2);
            (await db.ScalarAsync<long>($"select n from {table} where id = ?", [2])).Should().Be(2);
        }
        finally
        {
            try { await db.UnpreparedAsync($"drop table {table}"); }
            catch (Exception) { /* best effort: the CI containers are throwaway */ }
        }
    }

    private static async Task SavepointsAndRollbackAsync(string driver, string environmentVariable)
    {
        await using var provider = Build(driver, Environment.GetEnvironmentVariable(environmentVariable)!);
        var db = provider.GetRequiredService<IDatabase>();
        var table = "nc_tx_" + Guid.NewGuid().ToString("N")[..8];
        await db.UnpreparedAsync($"create table {table} (id int not null primary key, n int not null)");
        try
        {
            Task Insert(IDatabase target, int id) => target.StatementAsync($"insert into {table} (id, n) values (?, ?)", [id, 0]);

            // A failed savepoint is undone alone; the rest of the outer transaction commits.
            await db.TransactionAsync(async tx =>
            {
                await Insert(tx, 1);

                var inner = () => tx.TransactionAsync(async nested =>
                {
                    await Insert(nested, 2);
                    throw new InvalidOperationException("inner");
                });
                await inner.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner");

                await Insert(tx, 3);
            });
            (await db.Table(table).OrderBy("id").PluckAsync<long>("id")).Should().Equal(1, 3);

            // Code that only has the injected database is rolled back with the transaction.
            var failing = () => db.TransactionAsync(async _ =>
            {
                await Insert(db, 4);
                throw new InvalidOperationException("outer");
            });
            await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("outer");
            (await db.ScalarAsync<long>($"select count(*) from {table}")).Should().Be(2);
        }
        finally
        {
            try { await db.UnpreparedAsync($"drop table {table}"); }
            catch (Exception) { /* best effort: the CI containers are throwaway */ }
        }
    }

    [ServiceFact("NARAVEL_TEST_POSTGRES")]
    public Task PostgreSql_retries_a_forced_deadlock() => ForceDeadlockAsync("pgsql", "NARAVEL_TEST_POSTGRES");

    [ServiceFact("NARAVEL_TEST_SQLSERVER")]
    public Task SqlServer_retries_a_forced_deadlock() => ForceDeadlockAsync("sqlserver", "NARAVEL_TEST_SQLSERVER");

    [ServiceFact("NARAVEL_TEST_MYSQL")]
    public Task MySql_savepoints_and_rollback() => SavepointsAndRollbackAsync("mysql", "NARAVEL_TEST_MYSQL");

    [ServiceFact("NARAVEL_TEST_POSTGRES")]
    public Task PostgreSql_savepoints_and_rollback() => SavepointsAndRollbackAsync("pgsql", "NARAVEL_TEST_POSTGRES");

    [ServiceFact("NARAVEL_TEST_SQLSERVER")]
    public Task SqlServer_savepoints_and_rollback() => SavepointsAndRollbackAsync("sqlserver", "NARAVEL_TEST_SQLSERVER");
}
