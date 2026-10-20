namespace Naravel.Database;

/// <summary>What <see cref="Query"/> needs to run compiled SQL: the real <c>DatabaseExecutor</c> or the <c>DatabaseFake</c>.</summary>
internal interface IQueryRunner
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    Task<IReadOnlyList<T>> RowsAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    Task<T> FirstValueAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> StreamAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    IAsyncEnumerable<T> StreamAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    Task<int> NonQueryAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken);

    /// <summary>Runs several statements on one connection inside one transaction (the ambient one when there is one, otherwise a new one that commits at the end) and returns the rows affected in total.</summary>
    Task<int> NonQueryManyAsync(IReadOnlyList<CompiledQuery> statements, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="first"/> and then <paramref name="scalarSql"/> on the same connection and returns the first value of the second.</summary>
    Task<T> NonQueryThenScalarAsync<T>(CompiledQuery first, string scalarSql, CancellationToken cancellationToken);
}
