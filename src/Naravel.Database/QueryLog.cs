namespace Naravel.Database;

/// <summary>An opt-in, bounded, in-memory record of executed queries. Add it with <c>AddNaravelDatabaseQueryLog</c>.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>DB::enableQueryLog()</c>, <c>DB::getQueryLog()</c>, <c>DB::flushQueryLog()</c>.</para>
/// <para><b>Why it exists:</b> to see what a request ran while debugging. Unlike Laravel it is bounded (default 1000 entries, oldest dropped) so a
/// forgotten log cannot grow without limit, and it holds bound values, so enable it in development and tests, not in production.</para>
/// </remarks>
public sealed class QueryLog : IQueryListener
{
    private readonly object _gate = new();
    private readonly Queue<QueryExecuted> _entries = new();
    private readonly int _maxEntries;
    private volatile bool _enabled = true;

    /// <summary>Creates the log.</summary>
    /// <param name="maxEntries">The most entries kept; the oldest are dropped first. Must be at least 1.</param>
    public QueryLog(int maxEntries = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _maxEntries = maxEntries;
    }

    /// <summary>Whether new queries are recorded. <see langword="true"/> once the log is registered. Laravel: <c>enableQueryLog</c> / <c>disableQueryLog</c>.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>A snapshot of the recorded queries, oldest first. Laravel: <c>getQueryLog</c>.</summary>
    public IReadOnlyList<QueryExecuted> Entries
    {
        get
        {
            lock (_gate) return _entries.ToArray();
        }
    }

    /// <summary>Removes every recorded query. Laravel: <c>flushQueryLog</c>.</summary>
    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    /// <inheritdoc />
    public void OnQueryExecuted(QueryExecuted query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_enabled) return;
        lock (_gate)
        {
            _entries.Enqueue(query);
            while (_entries.Count > _maxEntries) _entries.Dequeue();
        }
    }
}
