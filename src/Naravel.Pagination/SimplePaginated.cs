using System.Globalization;
using System.Text.Json.Serialization;

namespace Naravel.Pagination;

/// <summary>A page without a total: the items and whether a next page exists, serialized as <c>{ "data", "links", "meta" }</c>.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Paginator</c> (<c>simplePaginate</c>): one query, no count, only previous and next.</para>
/// <para><b>Why it exists:</b> counting a large table on every request is the expensive part of paging. The adapter reads <c>perPage + 1</c> rows, keeps <c>perPage</c> and passes whether the extra row existed.</para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
public sealed class SimplePaginated<T>
{
    private readonly string? _path;
    private readonly IReadOnlyList<KeyValuePair<string, string?>> _query;
    private readonly string _pageName;

    /// <summary>Creates a page. Links stay <see langword="null"/> until <see cref="WithPath"/> is called.</summary>
    /// <param name="items">The items of this page, at most <paramref name="perPage"/>.</param>
    /// <param name="currentPage">The page number, starting at 1.</param>
    /// <param name="perPage">The page size, at least 1.</param>
    /// <param name="hasMorePages">Whether a page after this one exists.</param>
    public SimplePaginated(IReadOnlyList<T> items, int currentPage, int perPage, bool hasMorePages)
        : this(items, currentPage, perPage, hasMorePages, null, Array.Empty<KeyValuePair<string, string?>>(), "page")
    {
    }

    private SimplePaginated(IReadOnlyList<T> items, int currentPage, int perPage, bool hasMorePages, string? path, IReadOnlyList<KeyValuePair<string, string?>> query, string pageName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        if (items.Count > perPage) throw new ArgumentException("A page cannot hold more items than the page size.", nameof(items));
        Items = items;
        CurrentPage = currentPage;
        PerPage = perPage;
        HasMorePages = hasMorePages;
        _path = path;
        _query = query;
        _pageName = pageName;
    }

    /// <summary>The items of this page. JSON: <c>data</c>.</summary>
    [JsonPropertyName("data")]
    [JsonPropertyOrder(0)]
    public IReadOnlyList<T> Items { get; }

    /// <summary>The links to the first, previous and next page.</summary>
    [JsonPropertyName("links")]
    [JsonPropertyOrder(1)]
    public SimplePageLinks Links => new(Url(1), CurrentPage > 1 ? Url(CurrentPage - 1) : null, HasMorePages ? Url(CurrentPage + 1) : null);

    /// <summary>The numbers that describe the page.</summary>
    [JsonPropertyName("meta")]
    [JsonPropertyOrder(2)]
    public SimplePageMeta Meta => new(CurrentPage, From, _path, PerPage, To);

    /// <summary>The page number, starting at 1.</summary>
    [JsonIgnore]
    public int CurrentPage { get; }

    /// <summary>The page size.</summary>
    [JsonIgnore]
    public int PerPage { get; }

    /// <summary>Whether a page after this one exists.</summary>
    [JsonIgnore]
    public bool HasMorePages { get; }

    /// <summary>Whether this is the first page.</summary>
    [JsonIgnore]
    public bool OnFirstPage => CurrentPage == 1;

    /// <summary>The 1-based position of the first item, or <see langword="null"/> when the page is empty.</summary>
    [JsonIgnore]
    public long? From => Items.Count == 0 ? null : ((long)CurrentPage - 1) * PerPage + 1;

    /// <summary>The 1-based position of the last item, or <see langword="null"/> when the page is empty.</summary>
    [JsonIgnore]
    public long? To => From is { } from ? from + Items.Count - 1 : null;

    /// <summary>Returns a copy whose links start at <paramref name="path"/>.</summary>
    /// <param name="path">The base path; the page parameter is added to it.</param>
    public SimplePaginated<T> WithPath(string? path) => new(Items, CurrentPage, PerPage, HasMorePages, PageUrl.CheckPath(path), _query, _pageName);

    /// <summary>Returns a copy whose links carry these query-string values too.</summary>
    /// <param name="query">The values to repeat in every link.</param>
    public SimplePaginated<T> WithQuery(IEnumerable<KeyValuePair<string, string?>> query) => new(Items, CurrentPage, PerPage, HasMorePages, _path, PageUrl.Copy(query), _pageName);

    /// <summary>Returns a copy that names the page parameter differently.</summary>
    /// <param name="pageName">The parameter name, <c>page</c> by default.</param>
    public SimplePaginated<T> WithPageName(string pageName) => new(Items, CurrentPage, PerPage, HasMorePages, _path, _query, PageUrl.CheckName(pageName));

    /// <summary>Returns a page with the same numbers and links but other items.</summary>
    /// <typeparam name="TResult">The new item type.</typeparam>
    /// <param name="selector">Maps each item.</param>
    public SimplePaginated<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var mapped = new TResult[Items.Count];
        for (var i = 0; i < mapped.Length; i++) mapped[i] = selector(Items[i]);
        return new SimplePaginated<TResult>(mapped, CurrentPage, PerPage, HasMorePages, _path, _query, _pageName);
    }

    private string? Url(int page) => PageUrl.Build(_path, _query, _pageName, page.ToString(CultureInfo.InvariantCulture));
}

/// <summary>The <c>links</c> object of a simple page.</summary>
public sealed class SimplePageLinks
{
    internal SimplePageLinks(string? first, string? previous, string? next)
    {
        First = first;
        Previous = previous;
        Next = next;
    }

    /// <summary>The first page, or <see langword="null"/> when no path was set.</summary>
    [JsonPropertyName("first")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? First { get; }

    /// <summary>The previous page, or <see langword="null"/>.</summary>
    [JsonPropertyName("prev")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Previous { get; }

    /// <summary>The next page, or <see langword="null"/>.</summary>
    [JsonPropertyName("next")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Next { get; }
}

/// <summary>The <c>meta</c> object of a simple page.</summary>
public sealed class SimplePageMeta
{
    internal SimplePageMeta(int currentPage, long? from, string? path, int perPage, long? to)
    {
        CurrentPage = currentPage;
        From = from;
        Path = path;
        PerPage = perPage;
        To = to;
    }

    /// <summary>The page number.</summary>
    [JsonPropertyName("current_page")]
    [JsonPropertyOrder(0)]
    public int CurrentPage { get; }

    /// <summary>The position of the first item, or <see langword="null"/> for an empty page.</summary>
    [JsonPropertyName("from")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? From { get; }

    /// <summary>The base path, or <see langword="null"/>.</summary>
    [JsonPropertyName("path")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Path { get; }

    /// <summary>The page size.</summary>
    [JsonPropertyName("per_page")]
    [JsonPropertyOrder(3)]
    public int PerPage { get; }

    /// <summary>The position of the last item, or <see langword="null"/> for an empty page.</summary>
    [JsonPropertyName("to")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? To { get; }
}
