using System.Globalization;
using System.Text.Json;
using Naravel.Pagination;

namespace Naravel.Database;

#pragma warning disable RS0026, RS0027 // The dictionary and typed overloads mirror each other, as in Laravel.
public sealed partial class Query
{
    private const string TextTag = "s:";
    private const string DateTimeTag = "dt:";
    private const string DateTimeOffsetTag = "do:";

    /// <summary>
    /// Runs one count query and one page query and returns a <see cref="Paginated{T}"/> with totals and links. Laravel: <c>paginate</c>.
    /// </summary>
    /// <param name="page">The page number, 1-based; a value below 1 is read as 1.</param>
    /// <param name="perPage">The page size, at least 1; capped by <paramref name="options"/> (100 by default).</param>
    /// <param name="options">Optional limits, for example a different maximum page size.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="InvalidOperationException">The query has no <c>OrderBy</c> (offset pages of an unordered query are not stable), or it is not connected.</exception>
    /// <remarks>Any <c>Limit</c> or <c>Offset</c> already on the query is replaced. A page past the end is empty with the right totals and costs only the count query.</remarks>
    public Task<Paginated<IReadOnlyDictionary<string, object?>>> PaginateAsync(int page, int perPage, PaginationOptions? options = null, CancellationToken cancellationToken = default) =>
        PaginateCoreAsync(page, perPage, options, (query, token) => query.GetAsync(token), cancellationToken);

    /// <summary>Like <c>PaginateAsync</c> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="page">The page number, 1-based; a value below 1 is read as 1.</param>
    /// <param name="perPage">The page size, at least 1; capped by <paramref name="options"/>.</param>
    /// <param name="options">Optional limits.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public Task<Paginated<T>> PaginateAsync<T>(int page, int perPage, PaginationOptions? options = null, CancellationToken cancellationToken = default) =>
        PaginateCoreAsync(page, perPage, options, (query, token) => query.GetAsync<T>(token), cancellationToken);

    /// <summary>
    /// Runs one page query that reads one extra row to learn whether a next page exists, with no count. Laravel: <c>simplePaginate</c>.
    /// </summary>
    /// <param name="page">The page number, 1-based; a value below 1 is read as 1.</param>
    /// <param name="perPage">The page size, at least 1; capped by <paramref name="options"/>.</param>
    /// <param name="options">Optional limits.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="InvalidOperationException">The query has no <c>OrderBy</c>, or it is not connected.</exception>
    public Task<SimplePaginated<IReadOnlyDictionary<string, object?>>> SimplePaginateAsync(int page, int perPage, PaginationOptions? options = null, CancellationToken cancellationToken = default) =>
        SimplePaginateCoreAsync(page, perPage, options, (query, token) => query.GetAsync(token), cancellationToken);

    /// <summary>Like <c>SimplePaginateAsync</c> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="page">The page number, 1-based; a value below 1 is read as 1.</param>
    /// <param name="perPage">The page size, at least 1; capped by <paramref name="options"/>.</param>
    /// <param name="options">Optional limits.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public Task<SimplePaginated<T>> SimplePaginateAsync<T>(int page, int perPage, PaginationOptions? options = null, CancellationToken cancellationToken = default) =>
        SimplePaginateCoreAsync(page, perPage, options, (query, token) => query.GetAsync<T>(token), cancellationToken);

    /// <summary>
    /// Reads one page after (or before) a cursor on a unique, never-null column: <c>where id &gt; last</c> instead of an offset, so inserts and deletes between requests do not shift the pages
    /// and deep pages cost the same as the first. Laravel: <c>cursorPaginate</c>.
    /// </summary>
    /// <param name="perPage">The page size, at least 1; capped by <paramref name="options"/>.</param>
    /// <param name="cursor">The <c>NextCursor</c> or <c>PreviousCursor</c> of an earlier page, or <see langword="null"/> for the first page.</param>
    /// <param name="column">The unique column the pages follow, <c>id</c> by default; it must be in the selected columns.</param>
    /// <param name="descending"><see langword="true"/> to page from the largest value down.</param>
    /// <param name="options">Optional limits.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="InvalidCursorException">The cursor is malformed, was altered, or holds another kind of value than the column.</exception>
    /// <exception cref="InvalidOperationException">The column is missing from the rows or null, or the query is not connected.</exception>
    /// <exception cref="NotSupportedException">The column holds a value type with no portable ordering; use a number, string, <see cref="DateTime"/> or <see cref="DateTimeOffset"/>.</exception>
    /// <remarks>Existing ordering, <c>Limit</c> and <c>Offset</c> are replaced. Rows whose column value is changed to move past the cursor can still be seen twice or skipped.</remarks>
    public Task<CursorPaginated<IReadOnlyDictionary<string, object?>>> CursorPaginateAsync(int perPage, string? cursor, string column = "id", bool descending = false, PaginationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var name = KeyName(column);
        return CursorPaginateCoreAsync(perPage, cursor, column, descending, options, row => KeyOf(row, name, column), (query, token) => query.GetAsync(token), cancellationToken);
    }

    /// <summary>Like <c>CursorPaginateAsync</c> with rows mapped to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="perPage">The page size, at least 1; capped by <paramref name="options"/>.</param>
    /// <param name="cursor">The cursor of an earlier page, or <see langword="null"/> for the first page.</param>
    /// <param name="key">Reads the column value from a row.</param>
    /// <param name="column">The unique column the pages follow, <c>id</c> by default.</param>
    /// <param name="descending"><see langword="true"/> to page from the largest value down.</param>
    /// <param name="options">Optional limits.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public Task<CursorPaginated<T>> CursorPaginateAsync<T>(int perPage, string? cursor, Func<T, object?> key, string column = "id", bool descending = false, PaginationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = KeyName(column);
        return CursorPaginateCoreAsync(perPage, cursor, column, descending, options, row => key(row), (query, token) => query.GetAsync<T>(token), cancellationToken);
    }

    private async Task<Paginated<TRow>> PaginateCoreAsync<TRow>(int page, int perPage, PaginationOptions? options, Func<Query, CancellationToken, Task<IReadOnlyList<TRow>>> read, CancellationToken cancellationToken)
    {
        var size = (options ?? PaginationOptions.Default).ClampPerPage(perPage);
        _ = Connected();
        RequireOrder("Paginating");
        page = Math.Max(page, 1);

        var counting = Clone();
        counting._limit = null;
        counting._offset = null;
        var total = await counting.CountAsync(cancellationToken).ConfigureAwait(false);

        var offset = ((long)page - 1) * size;

        // Past the end, or deeper than an offset can express: an empty page with the right totals, and no second statement.
        IReadOnlyList<TRow> rows = total == 0 || offset >= total || offset > int.MaxValue
            ? Array.Empty<TRow>()
            : await read(Clone().Limit(size).Offset((int)offset), cancellationToken).ConfigureAwait(false);
        return new Paginated<TRow>(rows, total, page, size);
    }

    private async Task<SimplePaginated<TRow>> SimplePaginateCoreAsync<TRow>(int page, int perPage, PaginationOptions? options, Func<Query, CancellationToken, Task<IReadOnlyList<TRow>>> read, CancellationToken cancellationToken)
    {
        var size = (options ?? PaginationOptions.Default).ClampPerPage(perPage);
        _ = Connected();
        RequireOrder("Paginating");
        page = Math.Max(page, 1);

        var offset = ((long)page - 1) * size;
        if (offset > int.MaxValue) return new SimplePaginated<TRow>(Array.Empty<TRow>(), page, size, hasMorePages: false);

        var rows = await read(Clone().Limit(size + 1).Offset((int)offset), cancellationToken).ConfigureAwait(false);
        var more = rows.Count > size;
        IReadOnlyList<TRow> items = more ? rows.Take(size).ToList() : rows;
        return new SimplePaginated<TRow>(items, page, size, more);
    }

    private async Task<CursorPaginated<TRow>> CursorPaginateCoreAsync<TRow>(
        int perPage, string? cursor, string column, bool descending, PaginationOptions? options, Func<TRow, object?> key, Func<Query, CancellationToken, Task<IReadOnlyList<TRow>>> read, CancellationToken cancellationToken)
    {
        var size = (options ?? PaginationOptions.Default).ClampPerPage(perPage);
        _ = Connected();
        _ = KeyName(column);
        RefuseUnion("Cursor pagination");

        var decoded = cursor is null ? null : DecodeCursor(cursor);
        var backwards = decoded?.Direction == CursorDirection.Previous;

        // Walking backwards flips the order and the comparison, then the rows are put back in order below.
        var effectiveDescending = descending != backwards;
        var query = Clone();
        if (effectiveDescending) query.ReorderDesc(column); else query.Reorder(column);
        query._offset = null;
        query.Limit(size + 1);
        if (decoded is not null) query.Where(column, effectiveDescending ? "<" : ">", decoded.Value);

        var rows = await read(query, cancellationToken).ConfigureAwait(false);
        var more = rows.Count > size;
        var items = more ? rows.Take(size).ToList() : rows.ToList();
        if (backwards) items.Reverse();

        string? next = null;
        string? previous = null;
        if (items.Count > 0)
        {
            if (!backwards)
            {
                if (more) next = EncodeCursor(CursorDirection.Next, key, items[^1], column);
                if (decoded is not null) previous = EncodeCursor(CursorDirection.Previous, key, items[0], column);
            }
            else
            {
                next = EncodeCursor(CursorDirection.Next, key, items[^1], column);
                if (more) previous = EncodeCursor(CursorDirection.Previous, key, items[0], column);
            }
        }

        return new CursorPaginated<TRow>(items, size, next, previous);
    }

    private void RequireOrder(string what)
    {
        if (_orders.Count == 0)
        {
            throw new InvalidOperationException($"{what} with offset needs an OrderBy: pages of an unordered query are not stable. Add OrderBy(<unique column>), or use CursorPaginateAsync.");
        }
    }

    private static string EncodeCursor<TRow>(CursorDirection direction, Func<TRow, object?> key, TRow row, string column)
    {
        var value = key(row) ?? throw new InvalidOperationException($"The cursor cannot be built because the '{column}' column is null in an edge row of the page; the column must be unique and never null.");
        return CursorCodec.Encode(direction, new object?[] { Tag(value, column) }, new[] { typeof(object) });
    }

    // A cursor carries one value. Text and dates travel as tagged strings so a string key that looks like a date stays a string, and the database receives the type it stored.
    private static object Tag(object value, string column) => value switch
    {
        sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        decimal or double or float => value,
        string text => TextTag + text,
        DateTime dateTime => DateTimeTag + dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset offset => DateTimeOffsetTag + offset.ToString("O", CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"The cursor column '{column}' holds a {value.GetType().Name}, which has no ordering the databases agree on. Use a number, string, DateTime or DateTimeOffset column."),
    };

    private static CursorPosition DecodeCursor(string cursor)
    {
        var decoded = CursorCodec.Decode(cursor, new[] { typeof(JsonElement) });
        var element = (JsonElement)decoded.Values[0];
        try
        {
            object value = element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDecimal(),
                JsonValueKind.String => Untag(element.GetString()!),
                _ => throw new InvalidCursorException("The cursor does not hold the expected position."),
            };
            return new CursorPosition(decoded.Direction, value);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidOperationException)
        {
            throw new InvalidCursorException("The cursor does not hold the expected position.", exception);
        }
    }

    private static object Untag(string text)
    {
        if (text.StartsWith(TextTag, StringComparison.Ordinal)) return text[TextTag.Length..];
        if (text.StartsWith(DateTimeTag, StringComparison.Ordinal)) return DateTime.Parse(text[DateTimeTag.Length..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (text.StartsWith(DateTimeOffsetTag, StringComparison.Ordinal)) return DateTimeOffset.Parse(text[DateTimeOffsetTag.Length..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        throw new InvalidCursorException("The cursor does not hold the expected position.");
    }

    private sealed record CursorPosition(CursorDirection Direction, object Value);
}
#pragma warning restore RS0026, RS0027
