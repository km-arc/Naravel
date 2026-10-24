using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database.Testing;

namespace Naravel.Database.Tests;

public sealed class TransactionTests : IAsyncLifetime
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

        await _db.UnpreparedAsync("create table accounts (id integer primary key, balance integer not null)");
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private Task<long> CountAsync() => _db.ScalarAsync<long>("select count(*) from accounts");

    private static Task InsertAsync(IDatabase db, long id) =>
        db.StatementAsync("insert into accounts (id, balance) values (?, ?)", [id, 100]);

    [Fact]
    public async Task TransactionAsync_commits_when_the_callback_completes()
    {
        await _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);
            await InsertAsync(tx, 2);
        });

        (await CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task TransactionAsync_returns_the_callback_result()
    {
        var result = await _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);
            return 42;
        });

        result.Should().Be(42);
        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TransactionAsync_rolls_back_and_rethrows_when_the_callback_throws()
    {
        var act = () => _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);
            throw new InvalidOperationException("boom");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Two_builders_in_one_callback_share_the_connection_and_roll_back_together()
    {
        var act = () => _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);
            await InsertAsync(tx, 2);

            // Uncommitted rows are visible to both builders only if they run on the same connection and transaction.
            var first = await tx.Table("accounts").Where("id", 1).FirstAsync();
            var second = await tx.Table("accounts").Where("id", 2).FirstAsync();
            first.Should().NotBeNull();
            second.Should().NotBeNull();

            throw new InvalidOperationException("abort");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_nested_TransactionAsync_is_a_savepoint_that_rolls_back_alone()
    {
        await _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);

            var inner = () => tx.TransactionAsync(async nested =>
            {
                await InsertAsync(nested, 2);
                throw new InvalidOperationException("inner");
            });
            await inner.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner");

            await InsertAsync(tx, 3);
        });

        (await _db.Table("accounts").OrderBy("id").PluckAsync<long>("id")).Should().Equal(1, 3);
    }

    [Fact]
    public async Task A_nested_savepoint_that_succeeds_is_still_undone_when_the_outer_transaction_rolls_back()
    {
        var act = () => _db.TransactionAsync(async tx =>
        {
            await tx.TransactionAsync(nested => InsertAsync(nested, 1));
            throw new InvalidOperationException("outer");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outer");
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TransactionAsync_rolls_back_when_cancelled()
    {
        using var cts = new CancellationTokenSource();

        var act = () => _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
        }, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_deadlock_is_retried_up_to_the_attempt_count()
    {
        var runs = 0;

        await _db.TransactionAsync(async tx =>
        {
            runs++;
            await InsertAsync(tx, 1);
            if (runs < 3) throw new FakeDeadlock();
        }, attempts: 3);

        runs.Should().Be(3);
        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_deadlock_is_rethrown_when_the_attempts_run_out()
    {
        var runs = 0;

        var act = () => _db.TransactionAsync(async tx =>
        {
            runs++;
            await InsertAsync(tx, 1);
            throw new FakeDeadlock();
        }, attempts: 2);

        await act.Should().ThrowAsync<FakeDeadlock>();
        runs.Should().Be(2);
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Other_exceptions_are_never_retried()
    {
        var runs = 0;

        var act = () => _db.TransactionAsync(async tx =>
        {
            runs++;
            await InsertAsync(tx, 1);
            throw new InvalidOperationException("not a deadlock");
        }, attempts: 5);

        await act.Should().ThrowAsync<InvalidOperationException>();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task The_injected_database_joins_the_transaction_and_rolls_back_with_it()
    {
        var failing = () => _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);
            await InsertAsync(_db, 2); // the injected database, not the one passed to the callback
            await _db.Table("accounts").InsertAsync(new Dictionary<string, object?> { ["id"] = 3L, ["balance"] = 100L });
            throw new InvalidOperationException("boom");
        });

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_injected_database_commits_with_the_transaction()
    {
        await _db.TransactionAsync(async _ =>
        {
            await InsertAsync(_db, 1);
            await InsertAsync(_db, 2);
        });

        (await CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task TransactionAsync_on_the_injected_database_inside_a_callback_is_a_savepoint()
    {
        await _db.TransactionAsync(async tx =>
        {
            await InsertAsync(tx, 1);

            var inner = () => _db.TransactionAsync(async _ =>
            {
                await InsertAsync(_db, 2);
                throw new InvalidOperationException("inner");
            });
            await inner.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner");

            await InsertAsync(_db, 3);
        });

        (await _db.Table("accounts").OrderBy("id").PluckAsync<long>("id")).Should().Equal(1, 3);
    }

    [Fact]
    public async Task The_ambient_transaction_ends_with_the_callback()
    {
        await _db.TransactionAsync(async tx => await InsertAsync(tx, 1));

        // The injected database is no longer inside a transaction: beginning a new one must not be refused as "already inside".
        await using var next = await _db.BeginTransactionAsync();
        await InsertAsync(next.Database, 2);
        await next.CommitAsync();

        (await CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BeginTransactionAsync_commits_on_CommitAsync()
    {
        await using (var tx = await _db.BeginTransactionAsync())
        {
            await InsertAsync(tx.Database, 1);
            await tx.CommitAsync();
        }

        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task BeginTransactionAsync_rolls_back_when_disposed_without_commit()
    {
        await using (var tx = await _db.BeginTransactionAsync())
        {
            await InsertAsync(tx.Database, 1);
        }

        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BeginTransactionAsync_rolls_back_on_RollBackAsync_and_refuses_a_second_completion()
    {
        await using var tx = await _db.BeginTransactionAsync();
        await InsertAsync(tx.Database, 1);
        await tx.RollBackAsync();

        var again = () => tx.CommitAsync();
        await again.Should().ThrowAsync<InvalidOperationException>();
        (await CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_transaction_database_cannot_switch_connection_or_begin_a_second_real_transaction()
    {
        await using var tx = await _db.BeginTransactionAsync();

        var other = () => tx.Database.Connection("other");
        other.Should().Throw<InvalidOperationException>();

        var begin = () => tx.Database.BeginTransactionAsync();
        await begin.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DatabaseFake_counts_commits_and_rollbacks_and_retries_deadlocks()
    {
        var fake = new DatabaseFake();

        await fake.TransactionAsync(_ => Task.CompletedTask);
        fake.TransactionsCommitted.Should().Be(1);

        var act = () => fake.TransactionAsync(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("x");
        });
        await act.Should().ThrowAsync<InvalidOperationException>();
        fake.TransactionsRolledBack.Should().Be(1);

        var runs = 0;
        await fake.TransactionAsync(async _ =>
        {
            await Task.Yield();
            if (++runs < 2) throw new FakeDeadlock();
        }, attempts: 2);
        runs.Should().Be(2);
        fake.TransactionsCommitted.Should().Be(2);
        fake.TransactionsRolledBack.Should().Be(2);
    }

    private sealed class FakeDeadlock : DbException
    {
        public override string? SqlState => "40001";
    }
}
