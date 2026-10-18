namespace Naravel.Database;

internal sealed class DatabaseService(DatabaseManager manager, IConnectionScope scope, string? connectionName, QueryObserver observer) : IDatabase
{
    private readonly DatabaseExecutor _executor = new(scope, connectionName, manager, observer);

    public string? ConnectionName => connectionName;

    public IDatabase Connection(string? name) => new DatabaseService(manager, scope, name, observer);

    public Query Table(string table)
    {
        var dialect = manager.Connection(connectionName).Dialect;
        return new Query(Grammar.For(dialect)) { Runner = _executor }.Table(table);
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
