namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>Adds a condition that the column matches the pattern; the pattern is a bound parameter. Case-insensitive unless <paramref name="caseSensitive"/> is set. Laravel: <c>whereLike</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="pattern">The pattern; <c>%</c> and <c>_</c> are wildcards and are not escaped.</param>
    /// <param name="caseSensitive"><c>true</c> for a case-sensitive match (not available on SQLite).</param>
    public Query WhereLike(string column, string pattern, bool caseSensitive = false) => AddLike(column, pattern, caseSensitive, negate: false, or: false);

    /// <summary>Adds a condition that the column does not match the pattern; the pattern is a bound parameter. Case-insensitive unless <paramref name="caseSensitive"/> is set. Laravel: <c>whereNotLike</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="pattern">The pattern; <c>%</c> and <c>_</c> are wildcards and are not escaped.</param>
    /// <param name="caseSensitive"><c>true</c> for a case-sensitive match (not available on SQLite).</param>
    public Query WhereNotLike(string column, string pattern, bool caseSensitive = false) => AddLike(column, pattern, caseSensitive, negate: true, or: false);

    /// <summary>Adds a condition that the column matches the pattern joined with <c>or</c>; the pattern is a bound parameter. Case-insensitive unless <paramref name="caseSensitive"/> is set. Laravel: <c>orWhereLike</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="pattern">The pattern; <c>%</c> and <c>_</c> are wildcards and are not escaped.</param>
    /// <param name="caseSensitive"><c>true</c> for a case-sensitive match (not available on SQLite).</param>
    public Query OrWhereLike(string column, string pattern, bool caseSensitive = false) => AddLike(column, pattern, caseSensitive, negate: false, or: true);

    /// <summary>Adds a condition that the column does not match the pattern joined with <c>or</c>; the pattern is a bound parameter. Case-insensitive unless <paramref name="caseSensitive"/> is set. Laravel: <c>orWhereNotLike</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="pattern">The pattern; <c>%</c> and <c>_</c> are wildcards and are not escaped.</param>
    /// <param name="caseSensitive"><c>true</c> for a case-sensitive match (not available on SQLite).</param>
    public Query OrWhereNotLike(string column, string pattern, bool caseSensitive = false) => AddLike(column, pattern, caseSensitive, negate: true, or: true);

    /// <summary>Full-text search on one column (MySQL <c>MATCH ... AGAINST</c>, PostgreSQL <c>to_tsvector @@ tsquery</c> with the <c>english</c> configuration, SQL Server <c>FREETEXT</c>/<c>CONTAINS</c>). SQLite throws <see cref="NotSupportedException"/> when the query is compiled. Needs a full-text index on MySQL and SQL Server. Laravel: <c>whereFullText</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The search text, bound as a parameter.</param>
    /// <param name="mode">Natural words or the engine's boolean syntax.</param>
    public Query WhereFullText(string column, string value, FullTextMode mode = FullTextMode.Natural) => AddFullText([column], value, mode, or: false);

    /// <summary>Full-text search over several columns (see the single-column overload). Laravel: <c>whereFullText</c>.</summary>
    /// <param name="columns">The columns, at least one; plain names only.</param>
    /// <param name="value">The search text, bound as a parameter.</param>
    /// <param name="mode">Natural words or the engine's boolean syntax.</param>
    public Query WhereFullText(IEnumerable<string> columns, string value, FullTextMode mode = FullTextMode.Natural) => AddFullText(columns, value, mode, or: false);

    /// <summary>Full-text search on one column joined with <c>or</c> (MySQL <c>MATCH ... AGAINST</c>, PostgreSQL <c>to_tsvector @@ tsquery</c> with the <c>english</c> configuration, SQL Server <c>FREETEXT</c>/<c>CONTAINS</c>). SQLite throws <see cref="NotSupportedException"/> when the query is compiled. Needs a full-text index on MySQL and SQL Server. Laravel: <c>orWhereFullText</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The search text, bound as a parameter.</param>
    /// <param name="mode">Natural words or the engine's boolean syntax.</param>
    public Query OrWhereFullText(string column, string value, FullTextMode mode = FullTextMode.Natural) => AddFullText([column], value, mode, or: true);

    /// <summary>Full-text search over several columns joined with <c>or</c> (see the single-column overload). Laravel: <c>orWhereFullText</c>.</summary>
    /// <param name="columns">The columns, at least one; plain names only.</param>
    /// <param name="value">The search text, bound as a parameter.</param>
    /// <param name="mode">Natural words or the engine's boolean syntax.</param>
    public Query OrWhereFullText(IEnumerable<string> columns, string value, FullTextMode mode = FullTextMode.Natural) => AddFullText(columns, value, mode, or: true);

    /// <summary>Adds a condition that the JSON column contains the value. Laravel: <c>whereJsonContains</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="value">The value: a string, number, boolean, array or object. SQLite and SQL Server accept only a scalar (searched inside a JSON array).</param>
    public Query WhereJsonContains(string column, object value) => AddJsonContains(column, value, not: false, or: false);

    /// <summary>Adds a condition that the JSON column does not contain the value. Laravel: <c>whereJsonDoesntContain</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="value">The value: a string, number, boolean, array or object. SQLite and SQL Server accept only a scalar (searched inside a JSON array).</param>
    public Query WhereJsonDoesntContain(string column, object value) => AddJsonContains(column, value, not: true, or: false);

    /// <summary>Adds a condition that the JSON column contains the value joined with <c>or</c>. Laravel: <c>orWhereJsonContains</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="value">The value: a string, number, boolean, array or object. SQLite and SQL Server accept only a scalar (searched inside a JSON array).</param>
    public Query OrWhereJsonContains(string column, object value) => AddJsonContains(column, value, not: false, or: true);

    /// <summary>Adds a condition that the JSON column does not contain the value joined with <c>or</c>. Laravel: <c>orWhereJsonDoesntContain</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="value">The value: a string, number, boolean, array or object. SQLite and SQL Server accept only a scalar (searched inside a JSON array).</param>
    public Query OrWhereJsonDoesntContain(string column, object value) => AddJsonContains(column, value, not: true, or: true);

    /// <summary>Adds a condition that the JSON path exists; the column must carry a path such as <c>data-&gt;name</c>. Laravel: <c>whereJsonContainsKey</c>.</summary>
    /// <param name="column">The column with a JSON path.</param>
    public Query WhereJsonContainsKey(string column) => AddJsonKey(column, not: false, or: false);

    /// <summary>Adds a condition that the JSON path does not exist; the column must carry a path such as <c>data-&gt;name</c>. Laravel: <c>whereJsonDoesntContainKey</c>.</summary>
    /// <param name="column">The column with a JSON path.</param>
    public Query WhereJsonDoesntContainKey(string column) => AddJsonKey(column, not: true, or: false);

    /// <summary>Adds a condition that the JSON path exists joined with <c>or</c>; the column must carry a path such as <c>data-&gt;name</c>. Laravel: <c>orWhereJsonContainsKey</c>.</summary>
    /// <param name="column">The column with a JSON path.</param>
    public Query OrWhereJsonContainsKey(string column) => AddJsonKey(column, not: false, or: true);

    /// <summary>Adds a condition that the JSON path does not exist joined with <c>or</c>; the column must carry a path such as <c>data-&gt;name</c>. Laravel: <c>orWhereJsonDoesntContainKey</c>.</summary>
    /// <param name="column">The column with a JSON path.</param>
    public Query OrWhereJsonDoesntContainKey(string column) => AddJsonKey(column, not: true, or: true);

    /// <summary>Compares the length of the JSON array with <c>=</c> (SQL Server and MySQL also count object keys). Laravel: <c>whereJsonLength</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="value">The length to compare with, zero or more.</param>
    public Query WhereJsonLength(string column, int value) => AddJsonLength(column, "=", value, or: false);

    /// <summary>Compares the length of the JSON array with an operator. Laravel: <c>whereJsonLength</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The length to compare with, zero or more.</param>
    public Query WhereJsonLength(string column, string op, int value) => AddJsonLength(column, op, value, or: false);

    /// <summary>Compares the length of the JSON array joined with <c>or</c> with <c>=</c> (SQL Server and MySQL also count object keys). Laravel: <c>orWhereJsonLength</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="value">The length to compare with, zero or more.</param>
    public Query OrWhereJsonLength(string column, int value) => AddJsonLength(column, "=", value, or: true);

    /// <summary>Compares the length of the JSON array joined with <c>or</c> with an operator. Laravel: <c>orWhereJsonLength</c>.</summary>
    /// <param name="column">The column; a JSON path such as <c>data-&gt;name</c> is allowed.</param>
    /// <param name="op">One of <c>= &lt; &gt; &lt;= &gt;= &lt;&gt; !=</c>.</param>
    /// <param name="value">The length to compare with, zero or more.</param>
    public Query OrWhereJsonLength(string column, string op, int value) => AddJsonLength(column, op, value, or: true);

    private Query AddLike(string column, string pattern, bool caseSensitive, bool negate, bool or)
    {
        var identifier = Column(column, nameof(column));
        ArgumentNullException.ThrowIfNull(pattern);
        _wheres.Add(new LikeWhere(identifier, pattern, caseSensitive, negate, or));
        return this;
    }

    private Query AddFullText(IEnumerable<string> columns, string value, FullTextMode mode, bool or)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown full-text mode.");
        var parsed = new List<ParsedIdentifier>();
        foreach (var column in columns)
        {
            var identifier = Column(column, nameof(columns));
            if (identifier.JsonPath is not null) throw new NotSupportedException("A full-text search needs plain columns, not JSON paths.");
            parsed.Add(identifier);
        }

        if (parsed.Count == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        _wheres.Add(new FullTextWhere(parsed, value, mode, or));
        return this;
    }

    private Query AddJsonContains(string column, object value, bool not, bool or)
    {
        var identifier = Column(column, nameof(column));
        ArgumentNullException.ThrowIfNull(value);
        _wheres.Add(new JsonContainsWhere(identifier, value, or, not));
        return this;
    }

    private Query AddJsonKey(string column, bool not, bool or)
    {
        var identifier = Column(column, nameof(column));
        if (identifier.JsonPath is null) throw new ArgumentException("A JSON key check needs a path, for example 'data->name'.", nameof(column));
        _wheres.Add(new JsonKeyWhere(identifier, or, not));
        return this;
    }

    private Query AddJsonLength(string column, string op, int value, bool or)
    {
        var identifier = Column(column, nameof(column));
        ArgumentNullException.ThrowIfNull(op);
        var normalized = Grammar.NormalizeOperator(op, nameof(op));
        if (Array.IndexOf(ComparisonOperators, normalized) < 0) throw new ArgumentException($"'{normalized}' cannot compare a JSON length; use one of = < > <= >= <> !=.", nameof(op));
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "A JSON length cannot be negative.");
        _wheres.Add(new JsonLengthWhere(identifier, normalized, value, or));
        return this;
    }
}
