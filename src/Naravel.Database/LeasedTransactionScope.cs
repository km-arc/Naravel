using System.Data.Common;
using System.Globalization;

namespace Naravel.Database;

/// <summary>
/// The ambient-transaction scope (PDR-022 A1): every lease it hands out is the same connection and transaction, borrowed, so disposing a lease
/// never closes, commits or rolls back anything. The transaction itself is owned by <see cref="DatabaseTransaction"/>.
/// </summary>
internal sealed class LeasedTransactionScope(DbConnection connection, DbTransaction transaction, DatabaseDialect dialect) : IConnectionScope
{
    private int _savepoints;

    public DbTransaction Transaction => transaction;

    public DatabaseDialect Dialect => dialect;

    public ValueTask<IConnectionLease> AcquireAsync(string? connectionName, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IConnectionLease>(new BorrowedLease(connection, transaction));

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
