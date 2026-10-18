namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>Adds <c>column between low and high</c> (both bound as parameters). Laravel: <c>whereBetween</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="low">The lower bound; not <see langword="null"/>, because a comparison with NULL never matches.</param>
    /// <param name="high">The upper bound; not <see langword="null"/>.</param>
    public Query WhereBetween(string column, object low, object high) => AddBetween(column, low, high, negate: false, or: false);

    /// <summary>Like <see cref="WhereBetween"/> but joined with <c>or</c>. Laravel: <c>orWhereBetween</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="low">The lower bound.</param>
    /// <param name="high">The upper bound.</param>
    public Query OrWhereBetween(string column, object low, object high) => AddBetween(column, low, high, negate: false, or: true);

    /// <summary>Adds <c>column not between low and high</c>. Laravel: <c>whereNotBetween</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="low">The lower bound.</param>
    /// <param name="high">The upper bound.</param>
    public Query WhereNotBetween(string column, object low, object high) => AddBetween(column, low, high, negate: true, or: false);

    /// <summary>Like <see cref="WhereNotBetween"/> but joined with <c>or</c>. Laravel: <c>orWhereNotBetween</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="low">The lower bound.</param>
    /// <param name="high">The upper bound.</param>
    public Query OrWhereNotBetween(string column, object low, object high) => AddBetween(column, low, high, negate: true, or: true);

    /// <summary>Adds <c>column between lowColumn and highColumn</c>, comparing three columns of the row. Laravel: <c>whereBetweenColumns</c>.</summary>
    /// <param name="column">The column to test.</param>
    /// <param name="lowColumn">The column holding the lower bound.</param>
    /// <param name="highColumn">The column holding the upper bound.</param>
    public Query WhereBetweenColumns(string column, string lowColumn, string highColumn) => AddBetweenColumns(column, lowColumn, highColumn, negate: false, or: false);

    /// <summary>Like <see cref="WhereBetweenColumns"/> but joined with <c>or</c>. Laravel: <c>orWhereBetweenColumns</c>.</summary>
    /// <param name="column">The column to test.</param>
    /// <param name="lowColumn">The column holding the lower bound.</param>
    /// <param name="highColumn">The column holding the upper bound.</param>
    public Query OrWhereBetweenColumns(string column, string lowColumn, string highColumn) => AddBetweenColumns(column, lowColumn, highColumn, negate: false, or: true);

    /// <summary>Adds <c>column not between lowColumn and highColumn</c>. Laravel: <c>whereNotBetweenColumns</c>.</summary>
    /// <param name="column">The column to test.</param>
    /// <param name="lowColumn">The column holding the lower bound.</param>
    /// <param name="highColumn">The column holding the upper bound.</param>
    public Query WhereNotBetweenColumns(string column, string lowColumn, string highColumn) => AddBetweenColumns(column, lowColumn, highColumn, negate: true, or: false);

    /// <summary>Like <see cref="WhereNotBetweenColumns"/> but joined with <c>or</c>. Laravel: <c>orWhereNotBetweenColumns</c>.</summary>
    /// <param name="column">The column to test.</param>
    /// <param name="lowColumn">The column holding the lower bound.</param>
    /// <param name="highColumn">The column holding the upper bound.</param>
    public Query OrWhereNotBetweenColumns(string column, string lowColumn, string highColumn) => AddBetweenColumns(column, lowColumn, highColumn, negate: true, or: true);

    /// <summary>Adds <c>column is null</c>. Laravel: <c>whereNull</c>.</summary>
    /// <param name="column">The column.</param>
    public Query WhereNull(string column) => AddNull(column, isNull: true, or: false);

    /// <summary>Like <see cref="WhereNull"/> but joined with <c>or</c>. Laravel: <c>orWhereNull</c>.</summary>
    /// <param name="column">The column.</param>
    public Query OrWhereNull(string column) => AddNull(column, isNull: true, or: true);

    /// <summary>Adds <c>column is not null</c>. Laravel: <c>whereNotNull</c>.</summary>
    /// <param name="column">The column.</param>
    public Query WhereNotNull(string column) => AddNull(column, isNull: false, or: false);

    /// <summary>Like <see cref="WhereNotNull"/> but joined with <c>or</c>. Laravel: <c>orWhereNotNull</c>.</summary>
    /// <param name="column">The column.</param>
    public Query OrWhereNotNull(string column) => AddNull(column, isNull: false, or: true);

    /// <summary>Adds <c>first = second</c>, comparing two columns of the row. Laravel: <c>whereColumn</c>.</summary>
    /// <param name="first">The left column.</param>
    /// <param name="second">The right column.</param>
    public Query WhereColumn(string first, string second) => AddColumn(first, "=", second, or: false);

    /// <summary>Adds <c>first operator second</c> with an allow-listed operator. Laravel: <c>whereColumn</c>.</summary>
    /// <param name="first">The left column.</param>
    /// <param name="op">An allowed operator such as <c>=</c>, <c>&lt;&gt;</c>, <c>&gt;</c>.</param>
    /// <param name="second">The right column.</param>
    public Query WhereColumn(string first, string op, string second) => AddColumn(first, op, second, or: false);

    /// <summary>Adds a group of <c>left = right</c> column comparisons joined by <c>and</c>, in parentheses. Laravel: <c>whereColumn([...])</c>.</summary>
    /// <param name="pairs">Left and right column names.</param>
    public Query WhereColumn(IEnumerable<KeyValuePair<string, string>> pairs) => AddColumnPairs(pairs, or: false);

    /// <summary>Like <c>WhereColumn(first, second)</c> but joined with <c>or</c>. Laravel: <c>orWhereColumn</c>.</summary>
    /// <param name="first">The left column.</param>
    /// <param name="second">The right column.</param>
    public Query OrWhereColumn(string first, string second) => AddColumn(first, "=", second, or: true);

    /// <summary>Like <c>WhereColumn(first, op, second)</c> but joined with <c>or</c>. Laravel: <c>orWhereColumn</c>.</summary>
    /// <param name="first">The left column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The right column.</param>
    public Query OrWhereColumn(string first, string op, string second) => AddColumn(first, op, second, or: true);

    /// <summary>Like <c>WhereColumn(pairs)</c> but joined with <c>or</c>. Laravel: <c>orWhereColumn([...])</c>.</summary>
    /// <param name="pairs">Left and right column names.</param>
    public Query OrWhereColumn(IEnumerable<KeyValuePair<string, string>> pairs) => AddColumnPairs(pairs, or: true);

    private ParsedIdentifier ColumnName(string column, string parameterName) =>
        Plain(IdentifierGrammar.Parse(column, parameterName), column, parameterName, allowAlias: false);

    private Query AddBetween(string column, object low, object high, bool negate, bool or)
    {
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(high);
        _wheres.Add(new BetweenWhere(ColumnName(column, nameof(column)), low, high, negate, or));
        return this;
    }

    private Query AddBetweenColumns(string column, string lowColumn, string highColumn, bool negate, bool or)
    {
        _wheres.Add(new BetweenColumnsWhere(ColumnName(column, nameof(column)), ColumnName(lowColumn, nameof(lowColumn)), ColumnName(highColumn, nameof(highColumn)), negate, or));
        return this;
    }

    private Query AddNull(string column, bool isNull, bool or)
    {
        _wheres.Add(new NullWhere(ColumnName(column, nameof(column)), isNull, or, not: false));
        return this;
    }

    private Query AddColumn(string first, string op, string second, bool or)
    {
        var normalized = Grammar.NormalizeOperator(op, nameof(op));
        _wheres.Add(new ColumnWhere(ColumnName(first, nameof(first)), normalized, ColumnName(second, nameof(second)), or));
        return this;
    }

    private Query AddColumnPairs(IEnumerable<KeyValuePair<string, string>> pairs, bool or)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var inner = new Query(Grammar);
        foreach (var pair in pairs) inner.AddColumn(pair.Key, "=", pair.Value, or: false);
        if (inner._wheres.Count > 0) _wheres.Add(new NestedWhere(inner._wheres.ToArray(), or, false));
        return this;
    }
}
