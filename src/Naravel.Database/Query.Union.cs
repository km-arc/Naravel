namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>
    /// Adds <c>union</c>: the rows of this query and of <paramref name="other"/>, without duplicates. Laravel: <c>union</c>.
    /// </summary>
    /// <param name="other">The query to unite with, built for the same dialect. It is copied when added, so later changes to it are not seen.</param>
    /// <exception cref="ArgumentException"><paramref name="other"/> uses another dialect.</exception>
    /// <remarks>
    /// <b>What comes after <c>Union</c> applies to the whole union.</b> <c>OrderBy</c>, <c>Limit</c> and <c>Offset</c> called after the first <c>Union</c> order and cut the combined rows (as in Laravel);
    /// the same calls made <b>before</b> it stay with the first select. Order by an output column name (<c>OrderBy("name")</c>), not a table-qualified one. Columns, <c>Where</c>, joins and groups
    /// called later still belong to the first select. Every term must return the same number of compatible columns: the database checks that.
    /// A term that has its own ordering, limit or offset, and the whole union when it has any, is written as a derived table (<c>select * from (...) as naravel_union</c>), because SQLite and SQL Server
    /// accept no parentheses or ordering inside a compound select; the result is the same on all four databases. On SQL Server a limit on the union needs no ordering, an offset does.
    /// A union query can be read (<c>GetAsync</c>, <c>CountAsync</c> over the combined rows, <c>ChunkAsync</c> and <c>LazyAsync</c> with an ordering) but not written to, and <c>ChunkByIdAsync</c> and <c>LazyByIdAsync</c> refuse it.
    /// </remarks>
    public Query Union(Query other) => AddUnion(other, all: false);

    /// <summary>Adds <c>union all</c>: like <see cref="Union"/> but keeping duplicate rows. Laravel: <c>unionAll</c>.</summary>
    /// <param name="other">The query to unite with, built for the same dialect.</param>
    public Query UnionAll(Query other) => AddUnion(other, all: true);

    private Query AddUnion(Query other, bool all)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Grammar.Dialect != Grammar.Dialect) throw new ArgumentException("The query to union must use the same dialect as this one.", nameof(other));

        if (_unions.Count == 0)
        {
            // Ordering and limits set so far belong to the first select; from here on they belong to the union as a whole.
            _headOrders.AddRange(_orders);
            _headLimit = _limit;
            _headOffset = _offset;
            _orders.Clear();
            _limit = null;
            _offset = null;
        }

        _unions.Add(new UnionItem(other.Clone(), all));
        return this;
    }

    /// <summary>The first select of a union, with the ordering and limit it had before the first <c>Union</c>.</summary>
    internal Query UnionHead()
    {
        var head = Clone();
        head._unions.Clear();
        head._ctes.Clear();
        head._orders.Clear();
        head._orders.AddRange(_headOrders);
        head._limit = _headLimit;
        head._offset = _headOffset;
        head._headOrders.Clear();
        head._headLimit = null;
        head._headOffset = null;
        return head;
    }

    /// <summary>A shell that carries only the ordering and limit of the whole union.</summary>
    internal Query UnionTail()
    {
        var tail = new Query(Grammar) { _limit = _limit, _offset = _offset };
        tail._orders.AddRange(_orders);
        return tail;
    }

    private void RefuseUnion(string what)
    {
        if (_unions.Count > 0) throw new InvalidOperationException($"{what} cannot be used on a query with Union or UnionAll.");
    }
}
