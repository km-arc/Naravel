using System.Data.Common;
using System.Globalization;

namespace Naravel.Database;

/// <summary>
/// The ambient-transaction scope (PDR-022 A1): every lease it hands out is the same connection and transaction, borrowed, so disposing a lease
/// never closes, commits or rolls back anything. The transaction itself is owned by <see cref="DatabaseTransaction"/>.
/// The scope is active until that transaction commits, rolls back or is disposed; after that it hands out no lease (AD-022, AV-017).
/// </summary>
internal sealed class LeasedTransactionScope(DbConnection connection, DbTransaction transaction, DatabaseDialect dialect) : IConnectionScope
{
    /// <summary>The message of the exception that work gets when it uses a transaction that has already ended.</summary>
    internal const string EndedMessage =
        "The transaction has already ended (it was committed, rolled back or disposed), so its database cannot be used any more. "
        + "Work that should take part in a transaction must be awaited before the TransactionAsync callback returns.";

    private int _savepoints;
    private int _ended;

    public DbTransaction Transaction => transaction;

    public DatabaseDialect Dialect => dialect;

    /// <summary>
    /// Marks the scope as ended. The owner calls it when commit, rollback or disposal begins, before it touches the provider transaction.
    /// A task that was started inside the callback and outlives it still holds this scope through its captured <c>ExecutionContext</c>
    /// (restoring the parent's <c>AsyncLocal</c> value does not clear a child's copy), so every later use is refused by
    /// <see cref="ThrowIfEnded"/> before it can reach the finished transaction or the connection the owner has released.
    /// </summary>
    public void End() => Volatile.Write(ref _ended, 1);

    /// <summary>Throws <see cref="InvalidOperationException"/> with <see cref="EndedMessage"/> when the owning transaction has ended.</summary>
    public void ThrowIfEnded()
    {
        if (Volatile.Read(ref _ended) != 0) throw new InvalidOperationException(EndedMessage);
    }

    public ValueTask<IConnectionLease> AcquireAsync(string? connectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfEnded();
        return ValueTask.FromResult<IConnectionLease>(new BorrowedLease(connection, transaction));
    }

    public string NextSavepointName() =>
        "naravel_sp_" + Interlocked.Increment(ref _savepoints).ToString(CultureInfo.InvariantCulture);

    private sealed class BorrowedLease(DbConnection connection, DbTransaction transaction) : IConnectionLease
    {
        public DbConnection Connection => connection;

        public DbTransaction? Transaction => transaction;

        public bool OwnsConnection => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
