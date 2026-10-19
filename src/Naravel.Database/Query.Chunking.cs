namespace Naravel.Database;

#pragma warning disable RS0026, RS0027 // The dictionary and typed overloads mirror each other, as in Laravel.
public sealed partial class Query
{
    /// <summary>
    /// Reads the rows in pages of <paramref name="count"/> with <c>limit</c>/<c>offset</c> and hands each page to <paramref name="callback"/>; return <see langword="false"/> to stop.
    /// Only the current page is in memory. Laravel: <c>chunk</c>.
    /// </summary>
    /// <param name="count">The page size, at least 1.</param>
    /// <param name="callback">Receives each non-empty page; return <see langword="true"/> to continue.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="InvalidOperationException">The query has no <c>OrderBy</c> (offset pages of an unordered query are not stable), or it is not connected.</exception>
    /// <remarks>Rows inserted or deleted while the loop runs shift the offsets, so a row can be skipped or seen twice. Use <c>ChunkByIdAsync</c> when the table changes during the loop. Any <c>Limit</c> or <c>Offset</c> already on the query is replaced.</remarks>
    public Task ChunkAsync(int count, Func<IReadOnlyList<IReadOnlyDictionary<string, object?>>, Task<bool>> callback, CancellationToken cancellationToken = default) =>
        ChunkCoreAsync(count, callback, (page, token) => page.GetAsync(token), cancellationToken);

    /// <summary>Like <c>ChunkAsync</c> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="count">The page size, at least 1.</param>
    /// <param name="callback">Receives each non-empty page; return <see langword="true"/> to continue.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public Task ChunkAsync<T>(int count, Func<IReadOnlyList<T>, Task<bool>> callback, CancellationToken cancellationToken = default) =>
        ChunkCoreAsync(count, callback, (page, token) => page.GetAsync<T>(token), cancellationToken);

    /// <summary>
    /// Reads the rows in pages ordered by a unique, increasing column, asking for the rows after the last one seen (<c>where id &gt; last</c>), so inserts and deletes during the loop
    /// do not shift the pages and deep pages cost the same as the first. Laravel: <c>chunkById</c>.
    /// </summary>
    /// <param name="count">The page size, at least 1.</param>
    /// <param name="callback">Receives each non-empty page; return <see langword="true"/> to continue.</param>
    /// <param name="column">The unique column the pages follow, <c>id</c> by default; it must be in the selected columns.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="InvalidOperationException">The column is missing from the rows or null.</exception>
    /// <remarks>Existing ordering, <c>Limit</c> and <c>Offset</c> are replaced. Rows updated so their column value moves past the cursor can still be seen twice; that is the same limit Laravel has.</remarks>
    public Task ChunkByIdAsync(int count, Func<IReadOnlyList<IReadOnlyDictionary<string, object?>>, Task<bool>> callback, string column = "id", CancellationToken cancellationToken = default)
    {
        var name = KeyName(column);
        return ChunkByIdCoreAsync(count, callback, column, descending: false, row => KeyOf(row, name, column), (page, token) => page.GetAsync(token), cancellationToken);
    }

    /// <summary>Like <c>ChunkByIdAsync</c> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="count">The page size, at least 1.</param>
    /// <param name="key">Reads the column value from a row.</param>
    /// <param name="callback">Receives each non-empty page; return <see langword="true"/> to continue.</param>
    /// <param name="column">The unique column the pages follow, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public Task ChunkByIdAsync<T>(int count, Func<T, object?> key, Func<IReadOnlyList<T>, Task<bool>> callback, string column = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = KeyName(column);
        return ChunkByIdCoreAsync(count, callback, column, descending: false, row => key(row), (page, token) => page.GetAsync<T>(token), cancellationToken);
    }

    /// <summary>Runs <c>ChunkAsync</c> and collects what <paramref name="map"/> returns for every row. Laravel: <c>chunkMap</c>.</summary>
    /// <typeparam name="TResult">The mapped type.</typeparam>
    /// <param name="map">Maps one row.</param>
    /// <param name="count">The page size, at least 1; 1000 is Laravel's default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <remarks>The mapped values of <b>all</b> rows are kept, so memory grows with the result; use <c>ChunkAsync</c> to process rows without keeping them.</remarks>
    public async Task<IReadOnlyList<TResult>> ChunkMapAsync<TResult>(Func<IReadOnlyDictionary<string, object?>, TResult> map, int count = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var results = new List<TResult>();
        await ChunkAsync(count, page =>
        {
            foreach (var row in page) results.Add(map(row));
            return Task.FromResult(true);
        }, cancellationToken).ConfigureAwait(false);
        return results;
    }

    /// <summary>Calls <paramref name="callback"/> for every row, reading in pages with <c>limit</c>/<c>offset</c>; return <see langword="false"/> to stop. Laravel: <c>each</c>.</summary>
    /// <param name="callback">Receives one row; return <see langword="true"/> to continue.</param>
    /// <param name="count">The page size, at least 1; 1000 is Laravel's default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <remarks>Same ordering requirement and offset caveat as <c>ChunkAsync</c>.</remarks>
    public Task EachAsync(Func<IReadOnlyDictionary<string, object?>, Task<bool>> callback, int count = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ChunkAsync(count, page => EachRowAsync(page, callback), cancellationToken);
    }

    /// <summary>Calls <paramref name="callback"/> for every row, reading with <c>ChunkByIdAsync</c>. Laravel: <c>eachById</c>.</summary>
    /// <param name="callback">Receives one row; return <see langword="true"/> to continue.</param>
    /// <param name="count">The page size, at least 1; 1000 is Laravel's default.</param>
    /// <param name="column">The unique column the pages follow, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public Task EachByIdAsync(Func<IReadOnlyDictionary<string, object?>, Task<bool>> callback, int count = 1000, string column = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ChunkByIdAsync(count, page => EachRowAsync(page, callback), column, cancellationToken);
    }

    private static async Task<bool> EachRowAsync<TRow>(IReadOnlyList<TRow> page, Func<TRow, Task<bool>> callback)
    {
        foreach (var row in page)
        {
            if (!await callback(row).ConfigureAwait(false)) return false;
        }

        return true;
    }

    private async Task ChunkCoreAsync<TRow>(int count, Func<IReadOnlyList<TRow>, Task<bool>> callback, Func<Query, CancellationToken, Task<IReadOnlyList<TRow>>> read, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentNullException.ThrowIfNull(callback);
        _ = Connected();
        if (_orders.Count == 0) throw new InvalidOperationException("Chunking with offset needs an OrderBy: pages of an unordered query are not stable. Add OrderBy(<unique column>), or use ChunkByIdAsync.");

        for (long page = 0; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = page * count;
            if (offset > int.MaxValue) throw new InvalidOperationException("The offset passed the largest value a query can carry (2 147 483 647 rows). Use ChunkByIdAsync.");

            var rows = await read(Clone().Limit(count).Offset((int)offset), cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0) return;
            if (!await callback(rows).ConfigureAwait(false)) return;
            if (rows.Count < count) return;
        }
    }

    internal async Task ChunkByIdCoreAsync<TRow>(int count, Func<IReadOnlyList<TRow>, Task<bool>> callback, string column, bool descending, Func<TRow, object?> key, Func<Query, CancellationToken, Task<IReadOnlyList<TRow>>> read, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentNullException.ThrowIfNull(callback);
        _ = Connected();
        _ = KeyName(column);

        object? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = Clone().Reorder(column);
            if (descending) page.ReorderDesc(column);
            page._offset = null;
            page.Limit(count);
            if (last is not null) page.Where(column, descending ? "<" : ">", last);

            var rows = await read(page, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0) return;
            if (!await callback(rows).ConfigureAwait(false)) return;
            if (rows.Count < count) return;

            last = key(rows[^1]) ?? throw new InvalidOperationException($"The chunk was stopped because the '{column}' column is null in the last row of a page; the column must be unique and never null.");
        }
    }

    private static string KeyName(string column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return Plain(IdentifierGrammar.Parse(column, nameof(column)), column, nameof(column), allowAlias: false).Segments[^1];
    }

    private static object? KeyOf(IReadOnlyDictionary<string, object?> row, string name, string column) =>
        row.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"The chunk was stopped because the '{column}' column is not in the selected columns; select it, or pass the column the rows carry.");
}
#pragma warning restore RS0026, RS0027
