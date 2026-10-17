using System.Collections;

namespace Naravel.Database;

/// <summary>A fluent, mutable query builder (select, basic where, ordering, limit/offset) that compiles to parameterised SQL for one dialect.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\Query\Builder</c>. Like Laravel's it is mutable and chained; use <see cref="Clone"/> to branch.</para>
/// <para><b>Why it is built this way:</b> every value is bound as a parameter, identifiers are validated by the frozen grammar of PDR-022 A3 and quoted per
/// dialect, and the only way to write SQL text is a method whose name ends in <c>Raw</c>, so the unsafe path is explicit and greppable. Validation happens
/// <b>at the call</b>, not when the query runs. Instances are not thread-safe.</para>
/// <para><b>Not ported on purpose:</b> <c>where($column, $operator)</c> with an operator guessed from the value type, and magic <c>whereColumnName</c> methods.</para>
/// </remarks>
public sealed class Query
{
    private readonly List<SelectItem> _columns = new();
    private readonly List<WhereNode> _wheres = new();
    private readonly List<OrderItem> _orders = new();
    private ParsedIdentifier? _table;
    private bool _distinct;
    private int? _limit;
    private int? _offset;

    /// <summary>Creates an empty query for a grammar.</summary>
    /// <param name="grammar">The dialect grammar, from <see cref="Naravel.Database.Grammar.For"/>.</param>
    public Query(Grammar grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        Grammar = grammar;
    }

    /// <summary>The grammar this query compiles with.</summary>
    public Grammar Grammar { get; }

    internal ParsedIdentifier? TableName => _table;

    internal IReadOnlyList<SelectItem> Columns => _columns;

    internal IReadOnlyList<WhereNode> Wheres => _wheres;

    internal IReadOnlyList<OrderItem> Orders => _orders;

    internal bool IsDistinct => _distinct;

    internal int? LimitValue => _limit;

    internal int? OffsetValue => _offset;

    /// <summary>Creates an empty query for a dialect.</summary>
    /// <param name="dialect">The dialect.</param>
    public static Query For(DatabaseDialect dialect) => new(Grammar.For(dialect));

    /// <summary>Sets the table (<c>users</c>, <c>app.users</c> or <c>users as u</c>). Laravel: <c>DB::table</c> / <c>from</c>.</summary>
    /// <param name="table">The table name.</param>
    public Query Table(string table)
    {
        _table = Plain(IdentifierGrammar.Parse(table, nameof(table)), table, nameof(table), allowAlias: true);
        return this;
    }

    /// <summary>Replaces the selected columns. <c>*</c>, <c>users.*</c> and <c>col as alias</c> are accepted. Laravel: <c>select</c>.</summary>
    /// <param name="columns">The columns.</param>
    public Query Select(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _columns.Clear();
        return AddSelect(columns);
    }

    /// <summary>Adds columns to the selection. Laravel: <c>addSelect</c>.</summary>
    /// <param name="columns">The columns.</param>
    public Query AddSelect(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        foreach (var column in columns) _columns.Add(new SelectItem(IdentifierGrammar.Parse(column, nameof(columns)), null, []));
        return this;
    }

    /// <summary>Adds a raw select expression; each <c>?</c> is replaced by a bound parameter (write <c>??</c> for a literal question mark). Laravel: <c>selectRaw</c>.</summary>
    /// <param name="sql">The SQL text. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>.</param>
    public Query SelectRaw(string sql, params object?[] bindings)
    {
        CheckRaw(sql, bindings);
        _columns.Add(new SelectItem(null, sql, bindings));
        return this;
    }

    /// <summary>Returns only distinct rows. Laravel: <c>distinct</c>.</summary>
    public Query Distinct()
    {
        _distinct = true;
        return this;
    }

    /// <summary>Adds <c>column = value</c> (<c>is null</c> when the value is <see langword="null"/>). Laravel: <c>where</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value to bind.</param>
    public Query Where(string column, object? value) => AddBasic(column, "=", value, or: false, not: false);

    /// <summary>Adds <c>column operator value</c> with an allow-listed operator. Laravel: <c>where</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="op">An allowed operator such as <c>=</c>, <c>&lt;&gt;</c>, <c>like</c>.</param>
    /// <param name="value">The value to bind.</param>
    public Query Where(string column, string op, object? value) => AddBasic(column, op, value, or: false, not: false);

    /// <summary>Adds a group of <c>column = value</c> conditions joined by <c>and</c>, in parentheses. Laravel: <c>where([...])</c>.</summary>
    /// <param name="conditions">Column and value pairs.</param>
    public Query Where(IEnumerable<KeyValuePair<string, object?>> conditions) => AddPairs(conditions, or: false);

    /// <summary>Adds a parenthesised group built by the callback. Laravel: <c>where(function ($q) {...})</c>.</summary>
    /// <param name="group">Receives a fresh query whose where clauses become the group.</param>
    public Query Where(Action<Query> group) => AddGroup(group, or: false, not: false);

    /// <summary>Adds a raw where condition (each <c>?</c> becomes a parameter). Laravel: <c>whereRaw</c>.</summary>
    /// <param name="sql">The SQL text. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>.</param>
    public Query WhereRaw(string sql, params object?[] bindings) => AddRaw(sql, bindings, or: false);

    /// <summary>Like <c>Where(column, value)</c> but joined with <c>or</c>. Laravel: <c>orWhere</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value to bind.</param>
    public Query OrWhere(string column, object? value) => AddBasic(column, "=", value, or: true, not: false);

    /// <summary>Like <c>Where(column, op, value)</c> but joined with <c>or</c>. Laravel: <c>orWhere</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value to bind.</param>
    public Query OrWhere(string column, string op, object? value) => AddBasic(column, op, value, or: true, not: false);

    /// <summary>Like <c>Where(pairs)</c> but joined with <c>or</c>. Laravel: <c>orWhere([...])</c>.</summary>
    /// <param name="conditions">Column and value pairs.</param>
    public Query OrWhere(IEnumerable<KeyValuePair<string, object?>> conditions) => AddPairs(conditions, or: true);

    /// <summary>Like <c>Where(group)</c> but joined with <c>or</c>. Laravel: <c>orWhere(function ($q) {...})</c>.</summary>
    /// <param name="group">Receives a fresh query whose where clauses become the group.</param>
    public Query OrWhere(Action<Query> group) => AddGroup(group, or: true, not: false);

    /// <summary>Like <see cref="WhereRaw"/> but joined with <c>or</c>. Laravel: <c>orWhereRaw</c>.</summary>
    /// <param name="sql">The SQL text. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>.</param>
    public Query OrWhereRaw(string sql, params object?[] bindings) => AddRaw(sql, bindings, or: true);

    /// <summary>Adds <c>not column = value</c>. Laravel: <c>whereNot</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value to bind.</param>
    public Query WhereNot(string column, object? value) => AddBasic(column, "=", value, or: false, not: true);

    /// <summary>Adds <c>not column operator value</c>. Laravel: <c>whereNot</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value to bind.</param>
    public Query WhereNot(string column, string op, object? value) => AddBasic(column, op, value, or: false, not: true);

    /// <summary>Adds <c>not (group)</c>. Laravel: <c>whereNot(function ($q) {...})</c>.</summary>
    /// <param name="group">Receives a fresh query whose where clauses become the negated group.</param>
    public Query WhereNot(Action<Query> group) => AddGroup(group, or: false, not: true);

    /// <summary>Like <c>WhereNot(column, value)</c> but joined with <c>or</c>. Laravel: <c>orWhereNot</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value to bind.</param>
    public Query OrWhereNot(string column, object? value) => AddBasic(column, "=", value, or: true, not: true);

    /// <summary>Like <c>WhereNot(column, op, value)</c> but joined with <c>or</c>. Laravel: <c>orWhereNot</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value to bind.</param>
    public Query OrWhereNot(string column, string op, object? value) => AddBasic(column, op, value, or: true, not: true);

    /// <summary>Like <c>WhereNot(group)</c> but joined with <c>or</c>. Laravel: <c>orWhereNot(function ($q) {...})</c>.</summary>
    /// <param name="group">Receives a fresh query whose where clauses become the negated group.</param>
    public Query OrWhereNot(Action<Query> group) => AddGroup(group, or: true, not: true);

    /// <summary>
    /// Adds <c>column in (...)</c>. An empty list matches nothing (<c>0 = 1</c>). Every value is a parameter, so a list that makes the statement exceed
    /// <see cref="Naravel.Database.Grammar.MaxParameters"/> throws <see cref="InvalidOperationException"/> when compiled: splitting the list into several
    /// <c>in</c> groups would not reduce the parameter count, so no split is attempted. Laravel: <c>whereIn</c>.
    /// </summary>
    /// <param name="column">The column.</param>
    /// <param name="values">The values; a string is rejected because it would be read as a list of characters.</param>
    public Query WhereIn(string column, IEnumerable values) => AddIn(column, values, negate: false, or: false);

    /// <summary>Adds <c>column not in (...)</c>; an empty list matches everything. Laravel: <c>whereNotIn</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="values">The values.</param>
    public Query WhereNotIn(string column, IEnumerable values) => AddIn(column, values, negate: true, or: false);

    /// <summary>Like <see cref="WhereIn"/> but joined with <c>or</c>. Laravel: <c>orWhereIn</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="values">The values.</param>
    public Query OrWhereIn(string column, IEnumerable values) => AddIn(column, values, negate: false, or: true);

    /// <summary>Like <see cref="WhereNotIn"/> but joined with <c>or</c>. Laravel: <c>orWhereNotIn</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="values">The values.</param>
    public Query OrWhereNotIn(string column, IEnumerable values) => AddIn(column, values, negate: true, or: true);

    /// <summary>Adds an <c>order by</c> term. Laravel: <c>orderBy</c>.</summary>
    /// <param name="column">The column.</param>
    /// <param name="direction"><c>asc</c> or <c>desc</c>, in any letter case.</param>
    public Query OrderBy(string column, string direction = "asc")
    {
        ArgumentNullException.ThrowIfNull(direction);
        bool descending;
        if (string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase)) descending = false;
        else if (string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)) descending = true;
        else throw new ArgumentException("The order direction must be 'asc' or 'desc'.", nameof(direction));

        _orders.Add(new OrderItem(Plain(IdentifierGrammar.Parse(column, nameof(column)), column, nameof(column), allowAlias: false), descending, null, []));
        return this;
    }

    /// <summary>Adds a descending <c>order by</c> term. Laravel: <c>orderByDesc</c>.</summary>
    /// <param name="column">The column.</param>
    public Query OrderByDesc(string column) => OrderBy(column, "desc");

    /// <summary>Adds a raw <c>order by</c> term (each <c>?</c> becomes a parameter). Laravel: <c>orderByRaw</c>.</summary>
    /// <param name="sql">The SQL text. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>.</param>
    public Query OrderByRaw(string sql, params object?[] bindings)
    {
        CheckRaw(sql, bindings);
        _orders.Add(new OrderItem(null, false, sql, bindings));
        return this;
    }

    /// <summary>Limits the number of rows. Laravel: <c>limit</c>.</summary>
    /// <param name="count">Zero or more.</param>
    public Query Limit(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _limit = count;
        return this;
    }

    /// <summary>Alias of <see cref="Limit"/>. Laravel: <c>take</c>.</summary>
    /// <param name="count">Zero or more.</param>
    public Query Take(int count) => Limit(count);

    /// <summary>Skips rows. Laravel: <c>offset</c>.</summary>
    /// <param name="count">Zero or more.</param>
    public Query Offset(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _offset = count;
        return this;
    }

    /// <summary>Alias of <see cref="Offset"/>. Laravel: <c>skip</c>.</summary>
    /// <param name="count">Zero or more.</param>
    public Query Skip(int count) => Offset(count);

    /// <summary>Copies the query so it can be changed without touching the original. Laravel: <c>clone</c>.</summary>
    public Query Clone()
    {
        var copy = new Query(Grammar) { _table = _table, _distinct = _distinct, _limit = _limit, _offset = _offset };
        copy._columns.AddRange(_columns);
        copy._wheres.AddRange(_wheres);
        copy._orders.AddRange(_orders);
        return copy;
    }

    /// <summary>Compiles the query for this grammar.</summary>
    /// <exception cref="InvalidOperationException">No table was set, SQL Server <c>Offset</c> has no <c>OrderBy</c>, or the parameter limit is exceeded.</exception>
    public CompiledQuery Compile() => Grammar.CompileSelect(this);

    /// <summary>The SQL text with <c>@p0</c>, <c>@p1</c>... markers. Laravel: <c>toSql</c>.</summary>
    public string ToSql() => Compile().Sql;

    /// <summary>The values to bind, in marker order. Laravel: <c>getBindings</c>.</summary>
    public IReadOnlyList<object?> GetBindings() => Compile().Bindings;

    /// <summary>The SQL with the values written in, <b>for logging and debugging only</b>: never execute it. Laravel: <c>toRawSql</c>.</summary>
    public string ToRawSql() => Grammar.Substitute(Compile());

    private static ParsedIdentifier Plain(ParsedIdentifier identifier, string text, string parameterName, bool allowAlias)
    {
        if (identifier.IsStar || (!allowAlias && identifier.Alias is not null)) throw IdentifierGrammar.Invalid(text, parameterName);
        return identifier;
    }

    private static void CheckRaw(string sql, object?[] bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(bindings);
        if (Grammar.CountPlaceholders(sql) != bindings.Length)
        {
            throw new ArgumentException($"The raw SQL has {Grammar.CountPlaceholders(sql)} '?' placeholders but {bindings.Length} bindings were given. Write '??' for a literal question mark.", nameof(bindings));
        }
    }

    private Query AddBasic(string column, string op, object? value, bool or, bool not)
    {
        var identifier = Plain(IdentifierGrammar.Parse(column, nameof(column)), column, nameof(column), allowAlias: false);
        var normalized = Grammar.NormalizeOperator(op, nameof(op));
        if (value is null)
        {
            if (normalized == "=") _wheres.Add(new NullWhere(identifier, isNull: true, or, not));
            else if (normalized is "<>" or "!=") _wheres.Add(new NullWhere(identifier, isNull: false, or, not));
            else throw new ArgumentException("A null value can only be compared with '=' or '<>'.", nameof(value));
            return this;
        }

        _wheres.Add(new BasicWhere(identifier, normalized, value, or, not));
        return this;
    }

    private Query AddPairs(IEnumerable<KeyValuePair<string, object?>> conditions, bool or)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var inner = new Query(Grammar);
        foreach (var pair in conditions) inner.AddBasic(pair.Key, "=", pair.Value, or: false, not: false);
        if (inner._wheres.Count > 0) _wheres.Add(new NestedWhere(inner._wheres.ToArray(), or, false));
        return this;
    }

    private Query AddGroup(Action<Query> group, bool or, bool not)
    {
        ArgumentNullException.ThrowIfNull(group);
        var inner = new Query(Grammar);
        group(inner);
        if (inner._wheres.Count > 0) _wheres.Add(new NestedWhere(inner._wheres.ToArray(), or, not));
        return this;
    }

    private Query AddRaw(string sql, object?[] bindings, bool or)
    {
        CheckRaw(sql, bindings);
        _wheres.Add(new RawWhere(sql, bindings, or, false));
        return this;
    }

    private Query AddIn(string column, IEnumerable values, bool negate, bool or)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values is string) throw new ArgumentException("Pass a collection of values, not a string.", nameof(values));
        var identifier = Plain(IdentifierGrammar.Parse(column, nameof(column)), column, nameof(column), allowAlias: false);
        var list = new List<object?>();
        foreach (var value in values) list.Add(value);
        _wheres.Add(new InWhere(identifier, list.ToArray(), negate, or));
        return this;
    }
}
