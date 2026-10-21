namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>Adds columns to <c>group by</c>. Laravel: <c>groupBy</c>.</summary>
    /// <param name="columns">The columns, qualified names allowed; at least one.</param>
    public Query GroupBy(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Length == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        foreach (var column in columns) _groups.Add(new GroupItem(Plain(IdentifierGrammar.Parse(column, nameof(columns)), column, nameof(columns), allowAlias: false), null, []));
        return this;
    }

    /// <summary>Adds a raw expression to <c>group by</c>; each <c>?</c> is replaced by a bound parameter (write <c>??</c> for a literal question mark). Laravel: <c>groupByRaw</c>.</summary>
    /// <param name="sql">The SQL text. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>.</param>
    public Query GroupByRaw(string sql, params object?[] bindings)
    {
        CheckRaw(sql, bindings);
        _groups.Add(new GroupItem(null, sql, bindings));
        return this;
    }

    /// <summary>Adds a raw condition to <c>having</c>; each <c>?</c> is replaced by a bound parameter. Laravel: <c>havingRaw</c>.</summary>
    /// <param name="sql">The SQL text. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>.</param>
    public Query HavingRaw(string sql, params object?[] bindings) => AddHaving(q => q.WhereRaw(sql, bindings));

    /// <summary>Like <see cref="HavingRaw"/> but joined with <c>or</c>. Laravel: <c>orHavingRaw</c>.</summary>
    /// <param name="sql">The SQL text. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>.</param>
    public Query OrHavingRaw(string sql, params object?[] bindings) => AddHaving(q => q.OrWhereRaw(sql, bindings));

    /// <summary>Adds a <c>having</c> condition with <c>=</c> on a grouped column or a select alias. PostgreSQL and SQL Server only accept grouped columns here, not aliases. Laravel: <c>having</c>.</summary>
    /// <param name="column">The column or alias.</param>
    /// <param name="value">The value, always a parameter.</param>
    public Query Having(string column, object? value) => AddHaving(q => q.Where(column, value));

    /// <summary>Adds a <c>having</c> condition with an operator. Laravel: <c>having</c>.</summary>
    /// <param name="column">The column or alias.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, always a parameter.</param>
    public Query Having(string column, string op, object? value) => AddHaving(q => q.Where(column, op, value));

    /// <summary>Like <c>Having</c> with <c>=</c> but joined with <c>or</c>. Laravel: <c>orHaving</c>.</summary>
    /// <param name="column">The column or alias.</param>
    /// <param name="value">The value, always a parameter.</param>
    public Query OrHaving(string column, object? value) => AddHaving(q => q.OrWhere(column, value));

    /// <summary>Like <c>Having</c> with an operator but joined with <c>or</c>. Laravel: <c>orHaving</c>.</summary>
    /// <param name="column">The column or alias.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, always a parameter.</param>
    public Query OrHaving(string column, string op, object? value) => AddHaving(q => q.OrWhere(column, op, value));

    /// <summary>Adds <c>column between low and high</c> to <c>having</c>; both bounds are parameters. Laravel: <c>havingBetween</c>.</summary>
    /// <param name="column">The column or alias.</param>
    /// <param name="low">The lower bound.</param>
    /// <param name="high">The upper bound.</param>
    public Query HavingBetween(string column, object low, object high) => AddHaving(q => q.WhereBetween(column, low, high));

    /// <summary>Like <c>HavingBetween</c> but joined with <c>or</c>. Laravel: <c>orHavingBetween</c>.</summary>
    /// <param name="column">The column or alias.</param>
    /// <param name="low">The lower bound.</param>
    /// <param name="high">The upper bound.</param>
    public Query OrHavingBetween(string column, object low, object high) => AddHaving(q => q.OrWhereBetween(column, low, high));

    /// <summary>Adds <c>column is null</c> to <c>having</c>. Laravel: <c>havingNull</c>.</summary>
    /// <param name="column">The column or alias.</param>
    public Query HavingNull(string column) => AddHaving(q => q.WhereNull(column));

    /// <summary>Adds <c>column is not null</c> to <c>having</c>. Laravel: <c>havingNotNull</c>.</summary>
    /// <param name="column">The column or alias.</param>
    public Query HavingNotNull(string column) => AddHaving(q => q.WhereNotNull(column));

    /// <summary>Like <c>HavingNull</c> but joined with <c>or</c>. Laravel: <c>orHavingNull</c>.</summary>
    /// <param name="column">The column or alias.</param>
    public Query OrHavingNull(string column) => AddHaving(q => q.OrWhereNull(column));

    /// <summary>Like <c>HavingNotNull</c> but joined with <c>or</c>. Laravel: <c>orHavingNotNull</c>.</summary>
    /// <param name="column">The column or alias.</param>
    public Query OrHavingNotNull(string column) => AddHaving(q => q.OrWhereNotNull(column));

    /// <summary>Orders by the column descending, newest first; the default column is <c>created_at</c>. Laravel: <c>latest</c>.</summary>
    /// <param name="column">The column.</param>
    public Query Latest(string column = "created_at") => OrderBy(column, "desc");

    /// <summary>Orders by the column ascending, oldest first; the default column is <c>created_at</c>. Laravel: <c>oldest</c>.</summary>
    /// <param name="column">The column.</param>
    public Query Oldest(string column = "created_at") => OrderBy(column, "asc");

    /// <summary>Orders rows randomly (<c>random()</c>, <c>rand()</c> or <c>newid()</c> per dialect). Laravel: <c>inRandomOrder</c>.</summary>
    public Query InRandomOrder()
    {
        _orders.Add(new OrderItem(null, false, Grammar.RandomOrder, []));
        return this;
    }

    /// <summary>Removes every <c>order by</c> term. Laravel: <c>reorder</c>.</summary>
    public Query Reorder()
    {
        _orders.Clear();
        return this;
    }

    /// <summary>Removes every <c>order by</c> term and orders by the column ascending. Laravel: <c>reorder</c>.</summary>
    /// <param name="column">The column.</param>
    public Query Reorder(string column) => ReorderBy(column, "asc");

    /// <summary>Removes every <c>order by</c> term and orders by the column descending. Laravel: <c>reorderDesc</c>.</summary>
    /// <param name="column">The column.</param>
    public Query ReorderDesc(string column) => ReorderBy(column, "desc");

    private Query ReorderBy(string column, string direction)
    {
        // Validate first (a bad column must not leave the query without its ordering).
        _ = Plain(IdentifierGrammar.Parse(column, nameof(column)), column, nameof(column), allowAlias: false);
        _orders.Clear();
        return OrderBy(column, direction);
    }

    private Query AddHaving(Func<Query, Query> add)
    {
        var scratch = new Query(Grammar);
        add(scratch);
        _havings.AddRange(scratch._wheres);
        return this;
    }
}
