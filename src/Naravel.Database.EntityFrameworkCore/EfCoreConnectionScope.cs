using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Naravel.Database.EntityFrameworkCore;

/// <summary>
/// Leases the connection and the current transaction of a <see cref="DbContext"/> (PDR-022 A1). It never closes, commits or rolls back anything
/// itself: opening and closing go through EF Core's reference-counted <c>OpenConnectionAsync</c>/<c>CloseConnectionAsync</c>, and the transaction
/// is whatever <c>Database.CurrentTransaction</c> is at the moment a command runs (or none, which is autocommit).
/// </summary>
internal sealed class EfCoreConnectionScope(DbContext context) : IConnectionScope
{
    public async ValueTask<IConnectionLease> AcquireAsync(string? connectionName, CancellationToken cancellationToken = default)
    {
        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return new Lease(database, database.GetDbConnection(), database.CurrentTransaction?.GetDbTransaction());
        }
        catch
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Lease(DatabaseFacade database, DbConnection connection, DbTransaction? transaction) : IConnectionLease
    {
        public DbConnection Connection => connection;

        public DbTransaction? Transaction => transaction;

        // EF Core owns the physical connection; disposing the lease only releases our reference count.
        public bool OwnsConnection => false;

        public async ValueTask DisposeAsync() => await database.CloseConnectionAsync().ConfigureAwait(false);
    }
}
