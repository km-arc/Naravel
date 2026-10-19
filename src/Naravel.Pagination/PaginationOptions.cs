namespace Naravel.Pagination;

/// <summary>Limits that keep a client from asking for too much (PDR-020).</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> none; Laravel leaves <c>perPage</c> to the caller. This type exists because the page size usually comes from a query string, and a million-row page is a denial of service.</para>
/// </remarks>
public sealed class PaginationOptions
{
    private readonly int _maxPerPage = 100;

    /// <summary>The options used when a call does not pass any: at most 100 items per page.</summary>
    public static PaginationOptions Default { get; } = new();

    /// <summary>The largest page size that is honoured; a larger request is reduced to it. Must be at least 1.</summary>
    public int MaxPerPage
    {
        get => _maxPerPage;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxPerPage = value;
        }
    }

    /// <summary>Checks a requested page size and reduces it to <see cref="MaxPerPage"/>.</summary>
    /// <param name="perPage">The requested page size.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="perPage"/> is less than 1.</exception>
    public int ClampPerPage(int perPage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        return Math.Min(perPage, _maxPerPage);
    }
}
