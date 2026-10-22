using System.Data.Common;

namespace Naravel.Database;

/// <summary>Owns one real transaction and the pooled connection lease it runs on.</summary>
internal sealed class DatabaseTransaction(IConnectionLease lease, DbTransaction transaction, IDatabase database) : IDatabaseTransaction
{
    private int _completed;

    public IDatabase Database => database;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Complete();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RollBackAsync(CancellationToken cancellationToken = default)
    {
        Complete();
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
#pragma warning disable CA1031 // A broken connection cannot roll back; closing it discards the transaction, and the caller's own exception matters more.
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Intentionally ignored, see the pragma above.
            }
#pragma warning restore CA1031
        }

        await transaction.DisposeAsync().ConfigureAwait(false);
        await lease.DisposeAsync().ConfigureAwait(false);
    }

    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            throw new InvalidOperationException("The transaction was already committed or rolled back.");
        }
    }
}
