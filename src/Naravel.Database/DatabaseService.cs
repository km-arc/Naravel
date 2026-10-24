namespace Naravel.Database;

internal sealed class DatabaseService(DatabaseManager manager, IConnectionScope scope, string? connectionName, QueryObserver observer) : IDatabase
{
    private readonly DatabaseExecutor _executor = new(scope, connectionName, manager, observer);

    public string? ConnectionName => connectionName;

    internal QueryObserver Observer => observer;

    internal IConnectionScope Scope => scope;

    /// <summary>The transaction this database already runs in: its own (inside a callback) or, for the injected database, the ambient one.</summary>
    private LeasedTransactionScope? CurrentTransaction =>
        scope as LeasedTransactionScope
        ?? (scope is AmbientConnectionScope ? AmbientTransaction.Find(manager, AmbientTransaction.NameOf(manager, connectionName)) : null);

    public IDatabase Connection(string? name)
    {
        if (scope is LeasedTransactionScope && !string.Equals(name, connectionName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A transaction is bound to one connection; start a new transaction on the other connection instead.");
        }

        return new DatabaseService(manager, scope, name, observer);
    }

    public Query Table(string table)
    {
        var dialect = manager.Connection(connectionName).Dialect;
        return new Query(Grammar.For(dialect)) { Runner = _executor }.Table(table);
    }

    public async Task<T> TransactionAsync<T>(Func<IDatabase, Task<T>> callback, int attempts = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        if (CurrentTransaction is { } nested)
        {
            return await RunSavepointAsync(nested, callback, cancellationToken).ConfigureAwait(false);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await BeginCoreAsync(cancellationToken).ConfigureAwait(false);

                // Set here, in this async method, so it flows into the callback and everything it awaits; the injected IDatabase
                // then joins the transaction (PDR-022 A1). Restored before the commit so the commit itself is not "inside".
                var leased = (LeasedTransactionScope)((DatabaseService)transaction.Database).Scope;
                var previous = AmbientTransaction.Push(manager, AmbientTransaction.NameOf(manager, connectionName), leased);
                T result;
                try
                {
                    result = await callback(transaction.Database).ConfigureAwait(false);
                }
                finally
                {
                    AmbientTransaction.Restore(previous);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception) when (attempt < attempts && DeadlockDetector.IsDeadlock(exception))
            {
                // The transaction was rolled back when it was disposed; run the callback again.
            }
        }
    }

    public Task TransactionAsync(Func<IDatabase, Task> callback, int attempts = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return TransactionAsync<bool>(async database =>
        {
            await callback(database).ConfigureAwait(false);
            return true;
        }, attempts, cancellationToken);
    }

    public async Task<IDatabaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Already inside a transaction. Use TransactionAsync on this database to open a savepoint.");
        }

        return await BeginCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<DatabaseTransaction> BeginCoreAsync(CancellationToken cancellationToken)
    {
        var lease = await scope.AcquireAsync(connectionName, cancellationToken).ConfigureAwait(false);
        try
        {
            if (lease.Transaction is not null)
            {
                throw new InvalidOperationException("This connection is already in a transaction that Naravel did not start (for example from EF Core); neither side commits a transaction it did not begin.");
            }

            var transaction = await lease.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var dialect = manager.Connection(connectionName).Dialect;
            var leased = new LeasedTransactionScope(lease.Connection, transaction, dialect);
            return new DatabaseTransaction(lease, transaction, new DatabaseService(manager, leased, connectionName, observer));
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<T> RunSavepointAsync<T>(LeasedTransactionScope nested, Func<IDatabase, Task<T>> callback, CancellationToken cancellationToken)
    {
        var name = nested.NextSavepointName();
        await nested.Transaction.SaveAsync(name, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await callback(this).ConfigureAwait(false);
            // SQL Server has no RELEASE SAVEPOINT; the savepoint simply stays until the transaction ends.
            if (nested.Dialect != DatabaseDialect.SqlServer)
            {
                await nested.Transaction.ReleaseAsync(name, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch
        {
#pragma warning disable CA1031 // If rolling back to the savepoint fails too, the caller's original exception is the useful one.
            try
            {
                await nested.Transaction.RollbackAsync(name, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Intentionally ignored, see the pragma above.
            }
#pragma warning restore CA1031

            throw;
        }
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SelectAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return await _executor.RowsAsync(text, values, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> SelectAsync<T>(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return await _executor.RowsAsync<T>(text, values, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> ScalarAsync<T>(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return await _executor.FirstValueAsync<T>(text, values, cancellationToken).ConfigureAwait(false);
    }

    public async Task StatementAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default) =>
        await AffectingStatementAsync(sql, bindings, cancellationToken).ConfigureAwait(false);

    public async Task<int> AffectingStatementAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return await _executor.NonQueryAsync(text, values, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnpreparedAsync(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sql);
        await _executor.NonQueryAsync(sql, Array.Empty<object?>(), cancellationToken).ConfigureAwait(false);
    }
}
