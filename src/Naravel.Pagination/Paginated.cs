using System.Globalization;
using System.Text.Json.Serialization;

namespace Naravel.Pagination;

/// <summary>A length-aware page: the items, the total and the page numbers, serialized as <c>{ "data", "links", "meta" }</c>.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>LengthAwarePaginator</c>, shaped like a Laravel API resource collection so a front end written for Laravel works unchanged (PDR-020).</para>
/// <para><b>Why it is built this way:</b> immutable, with no I/O and no ASP.NET Core or EF Core reference. Property names are fixed with <c>[JsonPropertyName]</c>, so a camel-case policy cannot change them, and absent links and numbers are always written as <c>null</c>, even when the application ignores nulls globally.</para>
/// <para><b>Not ported on purpose:</b> <c>ArrayAccess</c>, <c>Collection</c> and <c>Arrayable</c> (no .NET meaning), and the page-link list inside <c>meta</c> (it is view markup, not data).</para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
public sealed class Paginated<T>
{
    private readonly string? _path;
    private readonly IReadOnlyList<KeyValuePair<string, string?>> _query;
    private readonly string _pageName;

    /// <summary>Creates a page. Links stay <see langword="null"/> until <see cref="WithPath"/> is called.</summary>
    /// <param name="items">The items of this page.</param>
    /// <param name="total">The number of items on all pages.</param>
    /// <param name="currentPage">The page number, starting at 1.</param>
    /// <param name="perPage">The page size, at least 1.</param>
    public Paginated(IReadOnlyList<T> items, long total, int currentPage, int perPage)
        : this(items, total, currentPage, perPage, null, Array.Empty<KeyValuePair<string, string?>>(), "page")
    {
    }

    private Paginated(IReadOnlyList<T> items, long total, int currentPage, int perPage, string? path, IReadOnlyList<KeyValuePair<string, string?>> query, string pageName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        Items = items;
        Total = total;
        CurrentPage = currentPage;
        PerPage = perPage;
        _path = path;
        _query = query;
        _pageName = pageName;
    }

    /// <summary>The items of this page. JSON: <c>data</c>.</summary>
    [JsonPropertyName("data")]
    [JsonPropertyOrder(0)]
    public IReadOnlyList<T> Items { get; }

    /// <summary>The links to the first, last, previous and next page.</summary>
    [JsonPropertyName("links")]
    [JsonPropertyOrder(1)]
    public PageLinks Links => new(
        Url(1),
        Url(LastPage),
        CurrentPage > 1 ? Url(CurrentPage - 1) : null,
        HasMorePages ? Url(CurrentPage + 1) : null);

    /// <summary>The numbers that describe the page.</summary>
    [JsonPropertyName("meta")]
    [JsonPropertyOrder(2)]
    public PageMeta Meta => new(CurrentPage, From, LastPage, _path, PerPage, To, Total);

    /// <summary>The number of items on all pages.</summary>
    [JsonIgnore]
    public long Total { get; }

    /// <summary>The page number, starting at 1.</summary>
    [JsonIgnore]
    public int CurrentPage { get; }

    /// <summary>The page size.</summary>
    [JsonIgnore]
    public int PerPage { get; }

    /// <summary>The last page number. An empty set has one (empty) page.</summary>
    [JsonIgnore]
    public int LastPage => (int)Math.Max(1, Math.Min(int.MaxValue, (Total / PerPage) + (Total % PerPage == 0 ? 0 : 1)));

    /// <summary>The 1-based position of the first item of this page, or <see langword="null"/> when the page is empty.</summary>
    [JsonIgnore]
    public long? From => Items.Count == 0 ? null : ((long)CurrentPage - 1) * PerPage + 1;

    /// <summary>The 1-based position of the last item of this page, or <see langword="null"/> when the page is empty.</summary>
    [JsonIgnore]
    public long? To => From is { } from ? from + Items.Count - 1 : null;

    /// <summary>Whether a page after this one exists.</summary>
    [JsonIgnore]
    public bool HasMorePages => CurrentPage < LastPage;

    /// <summary>Whether this is the first page.</summary>
    [JsonIgnore]
    public bool OnFirstPage => CurrentPage == 1;

    /// <summary>Whether this is the last page (or past it).</summary>
    [JsonIgnore]
    public bool OnLastPage => !HasMorePages;

    /// <summary>Returns a copy whose links start at <paramref name="path"/>, for example <c>/posts</c> or <c>https://example.test/posts</c>.</summary>
    /// <param name="path">The base path; the page parameter is added to it.</param>
    public Paginated<T> WithPath(string? path) => new(Items, Total, CurrentPage, PerPage, PageUrl.CheckPath(path), _query, _pageName);

    /// <summary>Returns a copy whose links carry these query-string values too (Laravel: <c>withQueryString</c>). A value named like the page parameter is ignored.</summary>
    /// <param name="query">The values to repeat in every link.</param>
    public Paginated<T> WithQuery(IEnumerable<KeyValuePair<string, string?>> query) => new(Items, Total, CurrentPage, PerPage, _path, PageUrl.Copy(query), _pageName);

    /// <summary>Returns a copy that names the page parameter differently (Laravel: <c>setPageName</c>).</summary>
    /// <param name="pageName">The parameter name, <c>page</c> by default.</param>
    public Paginated<T> WithPageName(string pageName) => new(Items, Total, CurrentPage, PerPage, _path, _query, PageUrl.CheckName(pageName));

    /// <summary>Returns a page with the same numbers and links but other items (Laravel: <c>through</c>).</summary>
    /// <typeparam name="TResult">The new item type.</typeparam>
    /// <param name="selector">Maps each item.</param>
    public Paginated<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var mapped = new TResult[Items.Count];
        for (var i = 0; i < mapped.Length; i++) mapped[i] = selector(Items[i]);
        return new Paginated<TResult>(mapped, Total, CurrentPage, PerPage, _path, _query, _pageName);
    }

    private string? Url(int page) => PageUrl.Build(_path, _query, _pageName, page.ToString(CultureInfo.InvariantCulture));
}

/// <summary>The <c>links</c> object of a length-aware page.</summary>
public sealed class PageLinks
{
    internal PageLinks(string? first, string? last, string? previous, string? next)
    {
        First = first;
        Last = last;
        Previous = previous;
        Next = next;
    }

    /// <summary>The first page, or <see langword="null"/> when no path was set.</summary>
    [JsonPropertyName("first")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? First { get; }

    /// <summary>The last page, or <see langword="null"/> when no path was set.</summary>
    [JsonPropertyName("last")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Last { get; }

    /// <summary>The previous page, or <see langword="null"/>.</summary>
    [JsonPropertyName("prev")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Previous { get; }

    /// <summary>The next page, or <see langword="null"/>.</summary>
    [JsonPropertyName("next")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Next { get; }
}

/// <summary>The <c>meta</c> object of a length-aware page.</summary>
public sealed class PageMeta
{
    internal PageMeta(int currentPage, long? from, int lastPage, string? path, int perPage, long? to, long total)
    {
        CurrentPage = currentPage;
        From = from;
        LastPage = lastPage;
        Path = path;
        PerPage = perPage;
        To = to;
        Total = total;
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

    /// <summary>The last page number.</summary>
    [JsonPropertyName("last_page")]
    [JsonPropertyOrder(2)]
    public int LastPage { get; }

    /// <summary>The base path, or <see langword="null"/>.</summary>
    [JsonPropertyName("path")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Path { get; }

    /// <summary>The page size.</summary>
    [JsonPropertyName("per_page")]
    [JsonPropertyOrder(4)]
    public int PerPage { get; }

    /// <summary>The position of the last item, or <see langword="null"/> for an empty page.</summary>
    [JsonPropertyName("to")]
    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? To { get; }

    /// <summary>The number of items on all pages.</summary>
    [JsonPropertyName("total")]
    [JsonPropertyOrder(6)]
    public long Total { get; }
}
