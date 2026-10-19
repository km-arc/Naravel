using System.Text.Json.Serialization;

namespace Naravel.Pagination;

/// <summary>A cursor page: the items and opaque cursors for the previous and next page, serialized as <c>{ "data", "links", "meta" }</c>.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>CursorPaginator</c> (<c>cursorPaginate</c>): no page numbers and no count, only "after this position" and "before this position".</para>
/// <para><b>Why it exists:</b> a cursor filters on the last seen key instead of skipping rows, so deep pages cost the same as the first and inserts behind the cursor do not shift the pages. A cursor is a <b>position</b>, not a secret and not a permission; the adapter that makes and reads cursors validates them and binds their values as parameters (PDR-020).</para>
/// <para><b>Shape:</b> Laravel documents no JSON for this paginator, so the shape is Naravel's own: <c>links</c> has <c>prev</c> and <c>next</c>; <c>meta</c> has <c>path</c>, <c>per_page</c>, <c>next_cursor</c> and <c>prev_cursor</c>.</para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
public sealed class CursorPaginated<T>
{
    private readonly string? _path;
    private readonly IReadOnlyList<KeyValuePair<string, string?>> _query;
    private readonly string _cursorName;

    /// <summary>Creates a page. Links stay <see langword="null"/> until <see cref="WithPath"/> is called.</summary>
    /// <param name="items">The items of this page, at most <paramref name="perPage"/>.</param>
    /// <param name="perPage">The page size, at least 1.</param>
    /// <param name="nextCursor">The cursor of the next page, or <see langword="null"/> when this is the last page.</param>
    /// <param name="previousCursor">The cursor of the previous page, or <see langword="null"/> when this is the first page.</param>
    public CursorPaginated(IReadOnlyList<T> items, int perPage, string? nextCursor, string? previousCursor)
        : this(items, perPage, nextCursor, previousCursor, null, Array.Empty<KeyValuePair<string, string?>>(), "cursor")
    {
    }

    private CursorPaginated(IReadOnlyList<T> items, int perPage, string? nextCursor, string? previousCursor, string? path, IReadOnlyList<KeyValuePair<string, string?>> query, string cursorName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        if (items.Count > perPage) throw new ArgumentException("A page cannot hold more items than the page size.", nameof(items));
        Items = items;
        PerPage = perPage;
        NextCursor = nextCursor;
        PreviousCursor = previousCursor;
        _path = path;
        _query = query;
        _cursorName = cursorName;
    }

    /// <summary>The items of this page. JSON: <c>data</c>.</summary>
    [JsonPropertyName("data")]
    [JsonPropertyOrder(0)]
    public IReadOnlyList<T> Items { get; }

    /// <summary>The links to the previous and next page.</summary>
    [JsonPropertyName("links")]
    [JsonPropertyOrder(1)]
    public CursorLinks Links => new(Url(PreviousCursor), Url(NextCursor));

    /// <summary>The numbers and cursors that describe the page.</summary>
    [JsonPropertyName("meta")]
    [JsonPropertyOrder(2)]
    public CursorMeta Meta => new(_path, PerPage, NextCursor, PreviousCursor);

    /// <summary>The page size.</summary>
    [JsonIgnore]
    public int PerPage { get; }

    /// <summary>The cursor of the next page, or <see langword="null"/>.</summary>
    [JsonIgnore]
    public string? NextCursor { get; }

    /// <summary>The cursor of the previous page, or <see langword="null"/>.</summary>
    [JsonIgnore]
    public string? PreviousCursor { get; }

    /// <summary>Whether a page after this one exists.</summary>
    [JsonIgnore]
    public bool HasMorePages => NextCursor is not null;

    /// <summary>Whether this is the first page.</summary>
    [JsonIgnore]
    public bool OnFirstPage => PreviousCursor is null;

    /// <summary>Returns a copy whose links start at <paramref name="path"/>.</summary>
    /// <param name="path">The base path; the cursor parameter is added to it.</param>
    public CursorPaginated<T> WithPath(string? path) => new(Items, PerPage, NextCursor, PreviousCursor, PageUrl.CheckPath(path), _query, _cursorName);

    /// <summary>Returns a copy whose links carry these query-string values too.</summary>
    /// <param name="query">The values to repeat in every link.</param>
    public CursorPaginated<T> WithQuery(IEnumerable<KeyValuePair<string, string?>> query) => new(Items, PerPage, NextCursor, PreviousCursor, _path, PageUrl.Copy(query), _cursorName);

    /// <summary>Returns a copy that names the cursor parameter differently.</summary>
    /// <param name="cursorName">The parameter name, <c>cursor</c> by default.</param>
    public CursorPaginated<T> WithCursorName(string cursorName) => new(Items, PerPage, NextCursor, PreviousCursor, _path, _query, PageUrl.CheckName(cursorName));

    /// <summary>Returns a page with the same cursors and links but other items.</summary>
    /// <typeparam name="TResult">The new item type.</typeparam>
    /// <param name="selector">Maps each item.</param>
    public CursorPaginated<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var mapped = new TResult[Items.Count];
        for (var i = 0; i < mapped.Length; i++) mapped[i] = selector(Items[i]);
        return new CursorPaginated<TResult>(mapped, PerPage, NextCursor, PreviousCursor, _path, _query, _cursorName);
    }

    private string? Url(string? cursor) => cursor is null ? null : PageUrl.Build(_path, _query, _cursorName, cursor);
}

/// <summary>The <c>links</c> object of a cursor page.</summary>
public sealed class CursorLinks
{
    internal CursorLinks(string? previous, string? next)
    {
        Previous = previous;
        Next = next;
    }

    /// <summary>The previous page, or <see langword="null"/>.</summary>
    [JsonPropertyName("prev")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Previous { get; }

    /// <summary>The next page, or <see langword="null"/>.</summary>
    [JsonPropertyName("next")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Next { get; }
}

/// <summary>The <c>meta</c> object of a cursor page.</summary>
public sealed class CursorMeta
{
    internal CursorMeta(string? path, int perPage, string? nextCursor, string? previousCursor)
    {
        Path = path;
        PerPage = perPage;
        NextCursor = nextCursor;
        PreviousCursor = previousCursor;
    }

    /// <summary>The base path, or <see langword="null"/>.</summary>
    [JsonPropertyName("path")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Path { get; }

    /// <summary>The page size.</summary>
    [JsonPropertyName("per_page")]
    [JsonPropertyOrder(1)]
    public int PerPage { get; }

    /// <summary>The cursor of the next page, or <see langword="null"/>.</summary>
    [JsonPropertyName("next_cursor")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? NextCursor { get; }

    /// <summary>The cursor of the previous page, or <see langword="null"/>.</summary>
    [JsonPropertyName("prev_cursor")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PreviousCursor { get; }
}
