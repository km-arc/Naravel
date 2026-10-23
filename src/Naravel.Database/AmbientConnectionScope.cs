namespace Naravel.Database;

/// <summary>
/// The scope behind the registered <see cref="IDatabase"/>: inside a <c>TransactionAsync</c> callback it hands out the transaction's connection
/// (for the same container and connection name), otherwise it defers to the normal scope. This is what makes code that only has an injected
/// <see cref="IDatabase"/> join the transaction without being given the transaction.
/// </summary>
internal sealed class AmbientConnectionScope(IConnectionScope inner, DatabaseManager manager) : IConnectionScope
{
    public ValueTask<IConnectionLease> AcquireAsync(string? connectionName, CancellationToken cancellationToken = default)
    {
        var ambient = AmbientTransaction.Find(manager, AmbientTransaction.NameOf(manager, connectionName));
        return ambient is null ? inner.AcquireAsync(connectionName, cancellationToken) : ambient.AcquireAsync(connectionName, cancellationToken);
    }
}
