using Microsoft.EntityFrameworkCore;

namespace Naravel.Database.EntityFrameworkCore;

/// <summary>An <see cref="IDatabase"/> that runs on a <see cref="DbContext"/>'s connection and current transaction.</summary>
internal sealed class EfCoreDatabase(DbContext context, DatabaseDialect dialect, QueryObserver observer) : IDatabase
{
    // The name shows up in telemetry and the query log; there is no Naravel connection definition behind it.
    private readonly DatabaseExecutor _executor = new(new EfCoreConnectionScope(context), "efcore", null, observer, dialect);

    public string? ConnectionName => null;

    public IDatabase Connection(string? name) =>
        name is null ? this : throw new InvalidOperationException("A database borrowed from a DbContext is bound to that DbContext's connection.");

    public Query Table(string table) => new Query(Grammar.For(dialect)) { Runner = _executor }.Table(table);

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

    public Task<T> TransactionAsync<T>(Func<IDatabase, Task<T>> callback, int attempts = 1, CancellationToken cancellationToken = default) =>
        throw NoTransactions();

    public Task TransactionAsync(Func<IDatabase, Task> callback, int attempts = 1, CancellationToken cancellationToken = default) =>
        throw NoTransactions();

    public Task<IDatabaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        throw NoTransactions();

    private static InvalidOperationException NoTransactions() =>
        new("A database borrowed from a DbContext does not start transactions: neither side commits a transaction it did not begin. "
            + "Start it on the DbContext (context.Database.BeginTransactionAsync); the query builder joins it automatically.");
}
