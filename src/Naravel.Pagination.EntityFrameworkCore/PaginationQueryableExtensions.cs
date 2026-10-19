using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Naravel.Pagination;

/// <summary>Pages an EF Core query: <c>PaginateAsync</c>, <c>SimplePaginateAsync</c> and <c>CursorPaginateAsync</c>.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>paginate</c>, <c>simplePaginate</c> and <c>cursorPaginate</c> on the query builder.</para>
/// <para><b>Why it is built this way:</b> a page of an unordered query is not stable, so a query without <c>OrderBy</c> is rejected instead of being given a made-up order. The page size is clamped to <see cref="PaginationOptions.MaxPerPage"/>. Mistakes in the arguments (page size, keys, a cursor that cannot be read) throw at the call, before any query runs.</para>
/// <para><b>Not ported on purpose:</b> reading the page number from the current HTTP request (the caller passes it) and rendering links as HTML.</para>
/// </remarks>
public static class PaginationQueryableExtensions
{
    /// <summary>Counts the rows, then reads one page: two statements, so a concurrent write can make the total differ from the page by a row. Laravel: <c>paginate</c>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="query">An ordered query.</param>
    /// <param name="page">The page number; anything below 1 is treated as 1. A page past the end is empty.</param>
    /// <param name="perPage">The page size, at least 1; larger than <see cref="PaginationOptions.MaxPerPage"/> is reduced.</param>
    /// <param name="cancellationToken">Cancels the statements.</param>
    public static Task<Paginated<T>> PaginateAsync<T>(this IQueryable<T> query, int page, int perPage, CancellationToken cancellationToken = default) =>
        PaginateAsync(query, page, perPage, null, cancellationToken);

    /// <summary>Like <see cref="PaginateAsync{T}(IQueryable{T}, int, int, CancellationToken)"/> with explicit limits.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="query">An ordered query.</param>
    /// <param name="page">The page number; anything below 1 is treated as 1.</param>
    /// <param name="perPage">The page size, at least 1.</param>
    /// <param name="options">The limits, or <see langword="null"/> for <see cref="PaginationOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the statements.</param>
    public static Task<Paginated<T>> PaginateAsync<T>(this IQueryable<T> query, int page, int perPage, PaginationOptions? options, CancellationToken cancellationToken = default)
    {
        var size = Check(query, perPage, options);
        return RunAsync(query, Math.Max(page, 1), size, cancellationToken);

        static async Task<Paginated<T>> RunAsync(IQueryable<T> query, int page, int size, CancellationToken cancellationToken)
        {
            var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
            var offset = ((long)page - 1) * size;

            // Past the end, or deeper than Skip can express: an empty page with the right totals, and no second statement.
            var items = total == 0 || offset >= total || offset > int.MaxValue
                ? new List<T>()
                : await query.Skip((int)offset).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false);
            return new Paginated<T>(items, total, page, size);
        }
    }

    /// <summary>Reads one page and one more row to learn whether a next page exists; no count. Laravel: <c>simplePaginate</c>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="query">An ordered query.</param>
    /// <param name="page">The page number; anything below 1 is treated as 1.</param>
    /// <param name="perPage">The page size, at least 1; larger than <see cref="PaginationOptions.MaxPerPage"/> is reduced.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    public static Task<SimplePaginated<T>> SimplePaginateAsync<T>(this IQueryable<T> query, int page, int perPage, CancellationToken cancellationToken = default) =>
        SimplePaginateAsync(query, page, perPage, null, cancellationToken);

    /// <summary>Like <see cref="SimplePaginateAsync{T}(IQueryable{T}, int, int, CancellationToken)"/> with explicit limits.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="query">An ordered query.</param>
    /// <param name="page">The page number; anything below 1 is treated as 1.</param>
    /// <param name="perPage">The page size, at least 1.</param>
    /// <param name="options">The limits, or <see langword="null"/> for <see cref="PaginationOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    public static Task<SimplePaginated<T>> SimplePaginateAsync<T>(this IQueryable<T> query, int page, int perPage, PaginationOptions? options, CancellationToken cancellationToken = default)
    {
        var size = Check(query, perPage, options);
        return RunAsync(query, Math.Max(page, 1), size, cancellationToken);

        static async Task<SimplePaginated<T>> RunAsync(IQueryable<T> query, int page, int size, CancellationToken cancellationToken)
        {
            var offset = ((long)page - 1) * size;
            if (offset > int.MaxValue) return new SimplePaginated<T>(new List<T>(), page, size, hasMorePages: false);

            var items = await query.Skip((int)offset).Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            var more = items.Count > size;
            if (more) items.RemoveAt(items.Count - 1);
            return new SimplePaginated<T>(items, page, size, more);
        }
    }

    /// <summary>
    /// Reads one page after or before a cursor with keyset paging: the helper orders by the declared keys and filters on the cursor's values, so deep pages cost the same as
    /// the first and inserts behind the cursor do not shift the pages. Laravel: <c>cursorPaginate</c>.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="query">The query. Its filters are kept; any ordering it has is replaced by the keys as the primary order.</param>
    /// <param name="keys">Declares the keys, for example <c>k =&gt; k.By(p =&gt; p.CreatedAt).Then(p =&gt; p.Id)</c>. Together they must be unique.</param>
    /// <param name="perPage">The page size, at least 1; larger than <see cref="PaginationOptions.MaxPerPage"/> is reduced.</param>
    /// <param name="cursor">The cursor from the previous result, or <see langword="null"/> for the first page.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <exception cref="InvalidCursorException">The cursor cannot be read; map it to HTTP 400.</exception>
    /// <exception cref="InvalidOperationException">A key value of an item on the page boundary is <see langword="null"/>.</exception>
    public static Task<CursorPaginated<T>> CursorPaginateAsync<T>(this IQueryable<T> query, Func<CursorKeys<T>, CursorKeys<T>> keys, int perPage, string? cursor, CancellationToken cancellationToken = default) =>
        CursorPaginateAsync(query, keys, perPage, cursor, null, cancellationToken);

    /// <summary>Like <see cref="CursorPaginateAsync{T}(IQueryable{T}, Func{CursorKeys{T}, CursorKeys{T}}, int, string, CancellationToken)"/> with explicit limits.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="keys">Declares the keys.</param>
    /// <param name="perPage">The page size, at least 1.</param>
    /// <param name="cursor">The cursor from the previous result, or <see langword="null"/> for the first page.</param>
    /// <param name="options">The limits, or <see langword="null"/> for <see cref="PaginationOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    public static Task<CursorPaginated<T>> CursorPaginateAsync<T>(this IQueryable<T> query, Func<CursorKeys<T>, CursorKeys<T>> keys, int perPage, string? cursor, PaginationOptions? options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(keys);
        var size = (options ?? PaginationOptions.Default).ClampPerPage(perPage);

        var declared = new CursorKeys<T>();
        keys(declared);
        var list = declared.Keys;
        if (list.Count == 0) throw new ArgumentException("Declare at least one key with By.", nameof(keys));

        var types = list.Select(key => key.KeyType).ToArray();
        var decoded = cursor is null ? null : CursorCodec.Decode(cursor, types);
        return RunAsync(query, list, types, decoded, size, cancellationToken);

        static async Task<CursorPaginated<T>> RunAsync(IQueryable<T> query, IReadOnlyList<CursorKey<T>> keys, Type[] types, CursorValues? decoded, int size, CancellationToken cancellationToken)
        {
            var backwards = decoded?.Direction == CursorDirection.Previous;
            var source = query;
            if (decoded is not null)
            {
                var item = Expression.Parameter(typeof(T), "x");
                source = source.Where(Expression.Lambda<Func<T, bool>>(After(keys, item, decoded.Values, after: !backwards), item));
            }

            for (var i = 0; i < keys.Count; i++) source = keys[i].Order(source, i == 0, keys[i].Descending != backwards);

            // One extra row tells whether there is more in the direction of travel.
            var items = await source.Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            var more = items.Count > size;
            if (more) items.RemoveAt(items.Count - 1);
            if (backwards) items.Reverse();

            string? next = null;
            string? previous = null;
            if (items.Count > 0)
            {
                if (!backwards)
                {
                    if (more) next = Encode(CursorDirection.Next, keys, types, items[^1]);
                    if (decoded is not null) previous = Encode(CursorDirection.Previous, keys, types, items[0]);
                }
                else
                {
                    next = Encode(CursorDirection.Next, keys, types, items[^1]);
                    if (more) previous = Encode(CursorDirection.Previous, keys, types, items[0]);
                }
            }

            return new CursorPaginated<T>(items, size, next, previous);
        }
    }

    // (k0 > v0) or (k0 = v0 and k1 > v1) or ...; the comparison flips for descending keys and for the backwards direction.
    private static Expression After<T>(IReadOnlyList<CursorKey<T>> keys, ParameterExpression item, IReadOnlyList<object> values, bool after)
    {
        Expression? any = null;
        for (var i = 0; i < keys.Count; i++)
        {
            Expression? term = null;
            for (var j = 0; j < i; j++)
            {
                var equal = keys[j].Equal(item, values[j]);
                term = term is null ? equal : Expression.AndAlso(term, equal);
            }

            var compare = keys[i].Compare(item, values[i], greater: after != keys[i].Descending);
            term = term is null ? compare : Expression.AndAlso(term, compare);
            any = any is null ? term : Expression.OrElse(any, term);
        }

        return any!;
    }

    private static string Encode<T>(CursorDirection direction, IReadOnlyList<CursorKey<T>> keys, Type[] types, T item)
    {
        var values = new object?[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            values[i] = keys[i].Read(item)
                ?? throw new InvalidOperationException($"The cursor key '{keys[i].Name}' is null on an item at the edge of the page; null key values are not supported. Use a key that is never null.");
        }

        return CursorCodec.Encode(direction, values, types);
    }

    private static int Check<T>(IQueryable<T> query, int perPage, PaginationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(query);
        var size = (options ?? PaginationOptions.Default).ClampPerPage(perPage);
        if (!IsOrdered(query)) throw new InvalidOperationException("The query has no OrderBy. A page of an unordered query is not stable; order it, ending with a unique column.");
        return size;
    }

    private static bool IsOrdered<T>(IQueryable<T> query)
    {
        for (var expression = query.Expression; expression is MethodCallExpression call && call.Arguments.Count > 0; expression = call.Arguments[0])
        {
            if (call.Method.DeclaringType == typeof(Queryable) && call.Method.Name is "OrderBy" or "OrderByDescending" or "Order" or "OrderDescending") return true;
        }

        return false;
    }
}
