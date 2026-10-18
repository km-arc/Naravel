namespace Naravel.Database;

/// <summary>What <see cref="Query"/> needs to run compiled SQL: the real <c>DatabaseExecutor</c> or the <c>DatabaseFake</c>.</summary>
internal interface IQueryRunner
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    Task<IReadOnlyList<T>> RowsAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    Task<T> FirstValueAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    Task<int> NonQueryAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);
}
