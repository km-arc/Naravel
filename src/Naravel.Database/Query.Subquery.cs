namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>Adds <c>exists (select ...)</c> built by the callback. Laravel: <c>whereExists</c>.</summary>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query WhereExists(Action<Query> callback) => AddExists(callback, not: false, or: false);

    /// <summary>Adds <c>exists (select ...)</c> joined with <c>or</c> built by the callback. Laravel: <c>orWhereExists</c>.</summary>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query OrWhereExists(Action<Query> callback) => AddExists(callback, not: false, or: true);

    /// <summary>Adds <c>not exists (select ...)</c> built by the callback. Laravel: <c>whereNotExists</c>.</summary>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query WhereNotExists(Action<Query> callback) => AddExists(callback, not: true, or: false);

    /// <summary>Adds <c>not exists (select ...)</c> joined with <c>or</c> built by the callback. Laravel: <c>orWhereNotExists</c>.</summary>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query OrWhereNotExists(Action<Query> callback) => AddExists(callback, not: true, or: true);

    /// <summary>Compares the column with an operator to a scalar subquery built by the callback. Laravel: <c>where</c> with a closure (a separate name here, because <c>Where("a", null)</c> must keep meaning <c>is null</c>).</summary>
    /// <param name="column">The column; a JSON path is allowed.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query WhereSubquery(string column, string op, Action<Query> callback) => AddSubquery(column, op, callback, or: false);

    /// <summary>Compares the column with an operator to a scalar subquery built by the callback joined with <c>or</c>. Laravel: <c>orWhere</c> with a closure (see <see cref="WhereSubquery"/>).</summary>
    /// <param name="column">The column; a JSON path is allowed.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query OrWhereSubquery(string column, string op, Action<Query> callback) => AddSubquery(column, op, callback, or: true);

    /// <summary>Adds <c>column in (select ...)</c> built by the callback, which should select one column. Laravel: <c>whereIn</c> with a closure.</summary>
    /// <param name="column">The column; a JSON path is allowed.</param>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query WhereIn(string column, Action<Query> callback) => AddInSubquery(column, callback, negate: false, or: false);

    /// <summary>Adds <c>column not in (select ...)</c> built by the callback, which should select one column. Laravel: <c>whereNotIn</c> with a closure.</summary>
    /// <param name="column">The column; a JSON path is allowed.</param>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query WhereNotIn(string column, Action<Query> callback) => AddInSubquery(column, callback, negate: true, or: false);

    /// <summary>Adds <c>column in (select ...)</c> joined with <c>or</c> built by the callback, which should select one column. Laravel: <c>orWhereIn</c> with a closure.</summary>
    /// <param name="column">The column; a JSON path is allowed.</param>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query OrWhereIn(string column, Action<Query> callback) => AddInSubquery(column, callback, negate: false, or: true);

    /// <summary>Adds <c>column not in (select ...)</c> joined with <c>or</c> built by the callback, which should select one column. Laravel: <c>orWhereNotIn</c> with a closure.</summary>
    /// <param name="column">The column; a JSON path is allowed.</param>
    /// <param name="callback">Receives a new query: call <c>Table(...)</c> on it, then add columns and conditions. Its values are bound in order with the outer query.</param>
    public Query OrWhereNotIn(string column, Action<Query> callback) => AddInSubquery(column, callback, negate: true, or: true);

    /// <summary>Applies the callback to this query when the condition is true, otherwise the optional second callback. Laravel: <c>when</c>.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="callback">Applied to this query when the condition holds.</param>
    /// <param name="otherwise">Applied to this query when it does not; optional.</param>
    public Query When(bool condition, Action<Query> callback, Action<Query>? otherwise = null) => Conditional(condition, callback, otherwise);

    /// <summary>Applies the callback to this query when the condition is false, otherwise the optional second callback. Laravel: <c>unless</c>.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="callback">Applied to this query when the condition holds.</param>
    /// <param name="otherwise">Applied to this query when it does not; optional.</param>
    public Query Unless(bool condition, Action<Query> callback, Action<Query>? otherwise = null) => Conditional(!condition, callback, otherwise);

    private Query Subquery(Action<Query> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var inner = new Query(Grammar);
        callback(inner);
        if (inner.TableName is null) throw new InvalidOperationException("The subquery has no table. Call Table(...) inside the callback.");
        return inner;
    }

    private Query AddExists(Action<Query> callback, bool not, bool or)
    {
        _wheres.Add(new ExistsWhere(Subquery(callback), or, not));
        return this;
    }

    private Query AddSubquery(string column, string op, Action<Query> callback, bool or)
    {
        var identifier = Column(column, nameof(column));
        var normalized = Grammar.NormalizeOperator(op, nameof(op));
        _wheres.Add(new SubqueryWhere(identifier, normalized, Subquery(callback), or, false));
        return this;
    }

    private Query AddInSubquery(string column, Action<Query> callback, bool negate, bool or)
    {
        var identifier = Column(column, nameof(column));
        _wheres.Add(new InSubqueryWhere(identifier, Subquery(callback), negate, or));
        return this;
    }

    private Query Conditional(bool condition, Action<Query> callback, Action<Query>? otherwise)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (condition) callback(this);
        else otherwise?.Invoke(this);
        return this;
    }
}
