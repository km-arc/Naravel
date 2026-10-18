namespace Naravel.Database;

#pragma warning disable RS0026, RS0027 // The generic overloads intentionally mirror the non-generic ones, as in Laravel.
public sealed partial class Query
{
    internal IQueryRunner? Runner { get; set; }

    /// <summary>Runs the query and returns every row as a case-insensitive dictionary. Laravel: <c>get</c>.</summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="InvalidOperationException">The query was created with <see cref="For"/> and is not connected; start it with <c>IDatabase.Table</c>.</exception>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetAsync(CancellationToken cancellationToken = default)
    {
        var compiled = Compile();
        return await Connected().RowsAsync(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the query and maps each row to <typeparamref name="T"/> with Dapper (not trim or AOT safe). Laravel: <c>get</c>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<IReadOnlyList<T>> GetAsync<T>(CancellationToken cancellationToken = default)
    {
        var compiled = Compile();
        return await Connected().RowsAsync<T>(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the first row, or <see langword="null"/> when there is none. Laravel: <c>first</c>.</summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<IReadOnlyDictionary<string, object?>?> FirstAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Clone().Limit(1).GetAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>Returns the first row mapped to <typeparamref name="T"/>, or the default when there is none. Laravel: <c>first</c>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<T> FirstAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await Clone().Limit(1).GetAsync<T>(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? default! : rows[0];
    }

    /// <summary>Returns the only matching row; throws when there are none or more than one. Laravel: <c>sole</c>.</summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="RecordsNotFoundException">No row matched.</exception>
    /// <exception cref="MultipleRecordsFoundException">More than one row matched.</exception>
    public async Task<IReadOnlyDictionary<string, object?>> SoleAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Clone().Limit(2).GetAsync(cancellationToken).ConfigureAwait(false);
        CheckSole(rows.Count);
        return rows[0];
    }

    /// <summary>Returns the only matching row mapped to <typeparamref name="T"/>; throws when there are none or more than one. Laravel: <c>sole</c>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="RecordsNotFoundException">No row matched.</exception>
    /// <exception cref="MultipleRecordsFoundException">More than one row matched.</exception>
    public async Task<T> SoleAsync<T>(CancellationToken cancellationToken = default)
    {
        var rows = await Clone().Limit(2).GetAsync<T>(cancellationToken).ConfigureAwait(false);
        CheckSole(rows.Count);
        return rows[0];
    }

    /// <summary>Returns one column of the first row, or the default of <typeparamref name="T"/> when there is no row. Laravel: <c>value</c>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="column">The column (validated by the identifier grammar).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<T> ValueAsync<T>(string column, CancellationToken cancellationToken = default)
    {
        var compiled = Clone().Select(column).Limit(1).Compile();
        return await Connected().FirstValueAsync<T>(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the first row whose key column equals <paramref name="id"/>, or <see langword="null"/>. Laravel: <c>find</c>.</summary>
    /// <param name="id">The key value, bound as a parameter.</param>
    /// <param name="keyColumn">The key column, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<IReadOnlyDictionary<string, object?>?> FindAsync(object id, string keyColumn = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return await Clone().Where(keyColumn, id).FirstAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the first row whose key column equals <paramref name="id"/> mapped to <typeparamref name="T"/>, or the default. Laravel: <c>find</c>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="id">The key value, bound as a parameter.</param>
    /// <param name="keyColumn">The key column, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<T> FindAsync<T>(object id, string keyColumn = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return await Clone().Where(keyColumn, id).FirstAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns one column of every row as a list of <typeparamref name="T"/>. Laravel: <c>pluck</c> (without the key argument).</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="column">The column (validated by the identifier grammar).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<IReadOnlyList<T>> PluckAsync<T>(string column, CancellationToken cancellationToken = default) =>
        await Clone().Select(column).GetAsync<T>(cancellationToken).ConfigureAwait(false);

    private IQueryRunner Connected() =>
        Runner ?? throw new InvalidOperationException("This query is not connected to a database. Start it with IDatabase.Table(...) instead of Query.For(...), which only compiles SQL.");

    private static void CheckSole(int count)
    {
        if (count == 0) throw new RecordsNotFoundException("No record matched the query.");
        if (count > 1) throw new MultipleRecordsFoundException("More than one record matched the query.");
    }
}
#pragma warning restore RS0026, RS0027
