namespace Naravel.Database;

#pragma warning disable RS0026, RS0027 // The overloads with and without a column mirror Laravel's count($column).
public sealed partial class Query
{
    /// <summary>Counts the matching rows. Laravel: <c>count</c>.</summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <remarks>Existing <c>order by</c> is ignored (some engines reject it in an aggregate). A query with <c>group by</c>, <c>having</c>, <c>distinct</c> without a column, <c>limit</c> or <c>offset</c> is counted as a derived table, so the result is the number of rows the query would return.</remarks>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default) => await AggregateAsync<long>("count", null, cancellationToken).ConfigureAwait(false);

    /// <summary>Counts the rows where <paramref name="column"/> is not null; after <see cref="Distinct"/> it counts the distinct values. Laravel: <c>count($column)</c>.</summary>
    /// <param name="column">The column, or <c>*</c>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<long> CountAsync(string column, CancellationToken cancellationToken = default) => await AggregateAsync<long>("count", column, cancellationToken).ConfigureAwait(false);

    /// <summary>The smallest value of a column, or the default of <typeparamref name="T"/> when there are no rows or every value is null. Laravel: <c>min</c>.</summary>
    /// <typeparam name="T">The value type; use a nullable type to tell "none" from a real zero.</typeparam>
    /// <param name="column">The column.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<T> MinAsync<T>(string column, CancellationToken cancellationToken = default) => await AggregateAsync<T>("min", Required(column), cancellationToken).ConfigureAwait(false);

    /// <summary>The largest value of a column, or the default of <typeparamref name="T"/> when there are no rows or every value is null. Laravel: <c>max</c>.</summary>
    /// <typeparam name="T">The value type; use a nullable type to tell "none" from a real zero.</typeparam>
    /// <param name="column">The column.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<T> MaxAsync<T>(string column, CancellationToken cancellationToken = default) => await AggregateAsync<T>("max", Required(column), cancellationToken).ConfigureAwait(false);

    /// <summary>The sum of a column; <c>0</c> when there are no rows or every value is null (Laravel returns <c>0</c> there too). Laravel: <c>sum</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<decimal> SumAsync(string column, CancellationToken cancellationToken = default) => await AggregateAsync<decimal>("sum", Required(column), cancellationToken).ConfigureAwait(false);

    /// <summary>The average of a column, or <see langword="null"/> when there are no rows or every value is null. Laravel: <c>avg</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<double?> AvgAsync(string column, CancellationToken cancellationToken = default) => await AggregateAsync<double?>("avg", Required(column), cancellationToken).ConfigureAwait(false);

    /// <summary>Whether at least one row matches; runs <c>select case when exists (...) then 1 else 0 end</c>, which all four engines accept. Laravel: <c>exists</c>.</summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        if (_unions.Count > 0) return await CountAsync(cancellationToken).ConfigureAwait(false) > 0;
        var compiled = CompileExists();
        return await Connected().FirstValueAsync<long>(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false) != 0;
    }

    /// <summary>Whether no row matches. Laravel: <c>doesntExist</c>.</summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    public async Task<bool> DoesntExistAsync(CancellationToken cancellationToken = default) => !await ExistsAsync(cancellationToken).ConfigureAwait(false);

    internal CompiledQuery CompileAggregate(string function, string? column)
    {
        var identifier = column is null or "*" ? null : Plain(IdentifierGrammar.Parse(column, nameof(column)), column, nameof(column), allowAlias: false);
        if (identifier is null && function != "count") throw new ArgumentException($"{function} needs a column.", nameof(column));

        var distinctColumn = _distinct && identifier is not null;
        var derived = _unions.Count > 0 || _groups.Count > 0 || _havings.Count > 0 || (_distinct && !distinctColumn) || _limit.HasValue || _offset.HasValue;
        var alias = Grammar.QuoteName("aggregate");

        if (!derived)
        {
            var plain = Clone();
            plain._orders.Clear();
            plain._columns.Clear();
            plain._distinct = false;
            var operand = identifier is null ? "*" : (distinctColumn ? "distinct " : string.Empty) + Grammar.Wrap(identifier);
            plain._columns.Add(new SelectItem(null, $"{function}({operand}) as {alias}", []));
            return Grammar.CompileSelect(plain);
        }

        // The query decides which rows exist; the aggregate is then taken over them. The column is read by its last segment, so it must be selected.
        var inner = Clone();
        if (!_limit.HasValue && !_offset.HasValue) inner._orders.Clear();
        var outer = identifier is null ? "*" : Grammar.Wrap(new ParsedIdentifier([identifier.Segments[^1]], false, null));
        return Grammar.CompileWrapped($"select {function}({outer}) as {alias} from ", inner, " as " + Grammar.QuoteName("naravel_aggregate"));
    }

    internal CompiledQuery CompileExists()
    {
        RefuseUnion("A single-statement exists");
        var inner = Clone();
        inner._columns.Clear();
        inner._columns.Add(new SelectItem(null, "1", []));
        inner._distinct = false;
        if (!_limit.HasValue && !_offset.HasValue) inner._orders.Clear();
        return Grammar.CompileWrapped("select case when exists ", inner, " then 1 else 0 end as " + Grammar.QuoteName("exists"));
    }

    private async Task<T> AggregateAsync<T>(string function, string? column, CancellationToken cancellationToken)
    {
        var compiled = CompileAggregate(function, column);
        return await Connected().FirstValueAsync<T>(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    private static string Required(string column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column;
    }
}
#pragma warning restore RS0026, RS0027
