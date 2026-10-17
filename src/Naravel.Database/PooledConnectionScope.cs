using System.Data.Common;

namespace Naravel.Database;

/// <summary>The default scope: a fresh pooled connection per lease, no transaction, closed when the lease is disposed.</summary>
internal sealed class PooledConnectionScope(DatabaseManager manager) : IConnectionScope
{
    public async ValueTask<IConnectionLease> AcquireAsync(string? connectionName, CancellationToken cancellationToken = default)
    {
        var driver = await manager.DriverAsync(connectionName, cancellationToken).ConfigureAwait(false);
        var connection = await driver.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new OwnedLease(connection);
    }

    private sealed class OwnedLease(DbConnection connection) : IConnectionLease
    {
        public DbConnection Connection => connection;

        public DbTransaction? Transaction => null;

        public bool OwnsConnection => true;

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
