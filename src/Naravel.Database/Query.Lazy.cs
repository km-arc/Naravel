using System.Runtime.CompilerServices;

namespace Naravel.Database;

#pragma warning disable RS0026, RS0027 // The dictionary and typed overloads mirror each other, as in Laravel.
public sealed partial class Query
{
    /// <summary>
    /// Streams the rows of <b>one</b> command: the reader stays open and each row is handed out as it is read, so memory holds one row at a time. Laravel: <c>cursor</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the command; <c>WithCancellation</c> on the enumerable does the same.</param>
    /// <exception cref="InvalidOperationException">The query is not connected.</exception>
    /// <remarks>
    /// The connection is borrowed until the enumeration ends, is disposed (a <c>break</c> out of <c>await foreach</c> does that) or is cancelled; both release the reader and the connection.
    /// Do not run other commands on the same connection while you read (SQL Server needs MARS for that; inside a transaction scope they share the connection). When you need to,
    /// use <see cref="LazyAsync(int, CancellationToken)"/>, which reads page by page and holds nothing between pages. The query is compiled now, so a bad query throws at the call.
    /// </remarks>
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CursorAsync(CancellationToken cancellationToken = default)
    {
        var runner = Connected();
        var compiled = Compile();
        return runner.StreamAsync(compiled.Sql, compiled.Bindings, cancellationToken);
    }

    /// <summary>Like <see cref="CursorAsync(CancellationToken)"/> with rows mapped to <typeparamref name="T"/> with Dapper.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="cancellationToken">Cancels the command.</param>
    public IAsyncEnumerable<T> CursorAsync<T>(CancellationToken cancellationToken = default)
    {
        var runner = Connected();
        var compiled = Compile();
        return runner.StreamAsync<T>(compiled.Sql, compiled.Bindings, cancellationToken);
    }

    /// <summary>
    /// Streams the rows while reading them in pages of <paramref name="count"/> with <c>limit</c>/<c>offset</c>: one row is handed out at a time, but no connection or reader is held
    /// between pages. Laravel: <c>lazy</c>.
    /// </summary>
    /// <param name="count">The page size, at least 1; 1000 is Laravel's default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="InvalidOperationException">The query has no <c>OrderBy</c>.</exception>
    /// <remarks>Same ordering requirement and offset caveat as <c>ChunkAsync</c>: rows inserted or deleted while you read can shift the pages. Arguments are checked at the call; the commands run as you enumerate.</remarks>
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> LazyAsync(int count = 1000, CancellationToken cancellationToken = default)
    {
        CheckLazy(count, needsOrder: true);
        return LazyIterateAsync(count, null, descending: false, null, (page, token) => page.GetAsync(token), cancellationToken);
    }

    /// <summary>Like <see cref="LazyAsync(int, CancellationToken)"/> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="count">The page size, at least 1.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public IAsyncEnumerable<T> LazyAsync<T>(int count = 1000, CancellationToken cancellationToken = default)
    {
        CheckLazy(count, needsOrder: true);
        return LazyIterateAsync<T>(count, null, descending: false, null, (page, token) => page.GetAsync<T>(token), cancellationToken);
    }

    /// <summary>Like <see cref="LazyAsync(int, CancellationToken)"/> but follows a unique, increasing column (<c>where id &gt; last</c>), so changes behind the cursor do not shift the pages. Laravel: <c>lazyById</c>.</summary>
    /// <param name="count">The page size, at least 1; 1000 is Laravel's default.</param>
    /// <param name="column">The unique column, <c>id</c> by default; it must be in the selected columns and never null.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <remarks>Existing ordering, <c>Limit</c> and <c>Offset</c> are replaced.</remarks>
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> LazyByIdAsync(int count = 1000, string column = "id", CancellationToken cancellationToken = default)
    {
        var name = KeyName(column);
        CheckLazy(count, needsOrder: false);
        return LazyIterateAsync(count, column, descending: false, row => KeyOf(row, name, column), (page, token) => page.GetAsync(token), cancellationToken);
    }

    /// <summary>Like <see cref="LazyByIdAsync(int, string, CancellationToken)"/> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="key">Reads the column value from a row.</param>
    /// <param name="count">The page size, at least 1.</param>
    /// <param name="column">The unique column, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public IAsyncEnumerable<T> LazyByIdAsync<T>(Func<T, object?> key, int count = 1000, string column = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = KeyName(column);
        CheckLazy(count, needsOrder: false);
        return LazyIterateAsync<T>(count, column, descending: false, row => key(row), (page, token) => page.GetAsync<T>(token), cancellationToken);
    }

    /// <summary>Like <see cref="LazyByIdAsync(int, string, CancellationToken)"/> from the highest key down (<c>where id &lt; last</c>). Laravel: <c>lazyByIdDesc</c>.</summary>
    /// <param name="count">The page size, at least 1; 1000 is Laravel's default.</param>
    /// <param name="column">The unique column, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> LazyByIdDescAsync(int count = 1000, string column = "id", CancellationToken cancellationToken = default)
    {
        var name = KeyName(column);
        CheckLazy(count, needsOrder: false);
        return LazyIterateAsync(count, column, descending: true, row => KeyOf(row, name, column), (page, token) => page.GetAsync(token), cancellationToken);
    }

    /// <summary>Like <see cref="LazyByIdDescAsync(int, string, CancellationToken)"/> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="key">Reads the column value from a row.</param>
    /// <param name="count">The page size, at least 1.</param>
    /// <param name="column">The unique column, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public IAsyncEnumerable<T> LazyByIdDescAsync<T>(Func<T, object?> key, int count = 1000, string column = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = KeyName(column);
        CheckLazy(count, needsOrder: false);
        return LazyIterateAsync<T>(count, column, descending: true, row => key(row), (page, token) => page.GetAsync<T>(token), cancellationToken);
    }

    private void CheckLazy(int count, bool needsOrder)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        _ = Connected();
        if (needsOrder && _orders.Count == 0) throw new InvalidOperationException("Lazy paging with offset needs an OrderBy: pages of an unordered query are not stable. Add OrderBy(<unique column>), or use LazyByIdAsync.");
    }

    // One page is read at a time; the next page is asked for only after every row of this one was taken, so stopping early costs no more commands.
    private async IAsyncEnumerable<TRow> LazyIterateAsync<TRow>(int count, string? idColumn, bool descending, Func<TRow, object?>? key, Func<Query, CancellationToken, Task<IReadOnlyList<TRow>>> read, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        object? last = null;
        for (long pageNumber = 0; ; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Query page;
            if (idColumn is null)
            {
                var offset = pageNumber * count;
                if (offset > int.MaxValue) throw new InvalidOperationException("The offset passed the largest value a query can carry (2 147 483 647 rows). Use LazyByIdAsync.");
                page = Clone().Limit(count).Offset((int)offset);
            }
            else
            {
                page = Clone().Reorder(idColumn);
                if (descending) page.ReorderDesc(idColumn);
                page._offset = null;
                page.Limit(count);
                if (last is not null) page.Where(idColumn, descending ? "<" : ">", last);
            }

            var rows = await read(page, cancellationToken).ConfigureAwait(false);
            foreach (var row in rows) yield return row;
            if (rows.Count < count) yield break;

            if (key is not null) last = key(rows[^1]) ?? throw new InvalidOperationException($"The stream was stopped because the '{idColumn}' column is null in the last row of a page; the column must be unique and never null.");
        }
    }
}
#pragma warning restore RS0026, RS0027
