namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>Selects from a subquery: <c>select ... from (select ...) as alias</c>. Replaces the table. Laravel: <c>fromSub</c>.</summary>
    /// <param name="query">The subquery, built for the same dialect. It is copied when added.</param>
    /// <param name="alias">The name of the derived table, a plain identifier. Columns of the outer query refer to it.</param>
    /// <remarks>A query built on a subquery can be read but not written to (insert, update, delete and truncate need a table).</remarks>
    public Query FromSub(Query query, string alias)
    {
        var source = SubSource(query, nameof(query));
        var name = AliasName(alias, nameof(alias));
        _table = null;
        _fromSubquery = source;
        _fromAlias = name;
        _fromRawSql = null;
        _fromRawBindings = [];
        return this;
    }

    /// <summary>
    /// Selects from raw SQL text: <c>select ... from {sql}</c>, for a table function, a derived table or anything the identifier grammar cannot name. Replaces the table. Laravel: <c>fromRaw</c>.
    /// </summary>
    /// <param name="sql">The SQL text after <c>from</c>, including any alias: <c>"generate_series(1, ?) as g(n)"</c>. Never build it from user input.</param>
    /// <param name="bindings">The values for each <c>?</c>; they are sent as parameters, in text order.</param>
    /// <remarks>A query built on <c>FromRaw</c> can be read but not written to (insert, update, delete and truncate need a table).</remarks>
    public Query FromRaw(string sql, params object?[] bindings)
    {
        CheckRaw(sql, bindings);
        _table = null;
        _fromSubquery = null;
        _fromAlias = null;
        _fromRawSql = sql;
        _fromRawBindings = bindings.ToArray();
        return this;
    }

    /// <summary>
    /// Adds a scalar subquery to the selected columns: <c>(select ...) as alias</c>. Laravel: <c>selectSub</c>.
    /// </summary>
    /// <param name="query">A subquery that returns one column and at most one row, built for the same dialect. It is copied when added.</param>
    /// <param name="alias">The name of the column, a plain identifier.</param>
    /// <remarks>Like <c>AddSelect</c>, it adds to the columns already selected; when none were selected the result has only this column. Its values are bound where it sits in the SQL text, before the <c>where</c> values.</remarks>
    public Query SelectSub(Query query, string alias)
    {
        var source = SubSource(query, nameof(query));
        var name = AliasName(alias, nameof(alias));
        _columns.Add(new SelectItem(null, null, [], source, name));
        return this;
    }

    /// <summary>Adds an <c>inner join</c> to a subquery on <c>first = second</c>. Laravel: <c>joinSub</c>.</summary>
    /// <param name="query">The subquery, built for the same dialect. It is copied when added.</param>
    /// <param name="alias">The name of the derived table, a plain identifier.</param>
    /// <param name="first">The first column, usually qualified with the alias.</param>
    /// <param name="second">The second column.</param>
    public Query JoinSub(Query query, string alias, string first, string second) => AddSubJoin(JoinKind.Inner, query, alias, join => join.On(first, second), lateral: false);

    /// <summary>Adds an <c>inner join</c> to a subquery on two columns compared with an operator. Laravel: <c>joinSub</c>.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public Query JoinSub(Query query, string alias, string first, string op, string second) => AddSubJoin(JoinKind.Inner, query, alias, join => join.On(first, op, second), lateral: false);

    /// <summary>Adds an <c>inner join</c> to a subquery whose conditions the callback builds. Laravel: <c>joinSub</c> with a closure.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="on">Adds at least one condition to the join clause.</param>
    public Query JoinSub(Query query, string alias, Action<JoinClause> on) => AddSubJoin(JoinKind.Inner, query, alias, on, lateral: false);

    /// <summary>Adds a <c>left join</c> to a subquery on <c>first = second</c>. Laravel: <c>leftJoinSub</c>.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="first">The first column.</param>
    /// <param name="second">The second column.</param>
    public Query LeftJoinSub(Query query, string alias, string first, string second) => AddSubJoin(JoinKind.Left, query, alias, join => join.On(first, second), lateral: false);

    /// <summary>Adds a <c>left join</c> to a subquery on two columns compared with an operator. Laravel: <c>leftJoinSub</c>.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public Query LeftJoinSub(Query query, string alias, string first, string op, string second) => AddSubJoin(JoinKind.Left, query, alias, join => join.On(first, op, second), lateral: false);

    /// <summary>Adds a <c>left join</c> to a subquery whose conditions the callback builds. Laravel: <c>leftJoinSub</c> with a closure.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="on">Adds at least one condition to the join clause.</param>
    public Query LeftJoinSub(Query query, string alias, Action<JoinClause> on) => AddSubJoin(JoinKind.Left, query, alias, on, lateral: false);

    /// <summary>Adds a <c>right join</c> to a subquery on <c>first = second</c>. Laravel: <c>rightJoinSub</c>.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="first">The first column.</param>
    /// <param name="second">The second column.</param>
    public Query RightJoinSub(Query query, string alias, string first, string second) => AddSubJoin(JoinKind.Right, query, alias, join => join.On(first, second), lateral: false);

    /// <summary>Adds a <c>right join</c> to a subquery on two columns compared with an operator. Laravel: <c>rightJoinSub</c>.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public Query RightJoinSub(Query query, string alias, string first, string op, string second) => AddSubJoin(JoinKind.Right, query, alias, join => join.On(first, op, second), lateral: false);

    /// <summary>Adds a <c>right join</c> to a subquery whose conditions the callback builds. Laravel: <c>rightJoinSub</c> with a closure.</summary>
    /// <param name="query">The subquery, built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <param name="on">Adds at least one condition to the join clause.</param>
    public Query RightJoinSub(Query query, string alias, Action<JoinClause> on) => AddSubJoin(JoinKind.Right, query, alias, on, lateral: false);

    /// <summary>
    /// Adds a lateral join: a subquery that may refer to the tables before it, run once per row of them. It keeps only rows that have a match, like an inner join. Laravel: <c>joinLateral</c>.
    /// </summary>
    /// <param name="query">The subquery; correlate it with <c>WhereColumn("posts.user_id", "users.id")</c>. Built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <exception cref="NotSupportedException">SQLite has no lateral join; the call throws when the query is compiled.</exception>
    /// <remarks>PostgreSQL writes <c>inner join lateral (...) as alias on true</c>, MySQL (8.0.14 or later) the same, SQL Server <c>cross apply (...) as alias</c>.</remarks>
    public Query JoinLateral(Query query, string alias) => AddSubJoin(JoinKind.Inner, query, alias, null, lateral: true);

    /// <summary>Adds a lateral join that keeps the rows without a match (<c>left join lateral ... on true</c>, SQL Server <c>outer apply</c>). Laravel: <c>leftJoinLateral</c>.</summary>
    /// <param name="query">The subquery; correlate it with <c>WhereColumn</c>. Built for the same dialect.</param>
    /// <param name="alias">The name of the derived table.</param>
    /// <exception cref="NotSupportedException">SQLite has no lateral join; the call throws when the query is compiled.</exception>
    public Query LeftJoinLateral(Query query, string alias) => AddSubJoin(JoinKind.Left, query, alias, null, lateral: true);

    private Query AddSubJoin(JoinKind kind, Query query, string alias, Action<JoinClause>? on, bool lateral)
    {
        var source = SubSource(query, nameof(query));
        var name = AliasName(alias, nameof(alias));
        var conditions = Array.Empty<WhereNode>();
        if (!lateral)
        {
            ArgumentNullException.ThrowIfNull(on);
            var clause = new JoinClause(Grammar);
            on(clause);
            if (clause.Conditions.Count == 0) throw new InvalidOperationException("A join needs at least one condition. Call On(...) or Where(...) inside the callback.");
            conditions = clause.Conditions.ToArray();
        }

        _joins.Add(new JoinItem(kind, new ParsedIdentifier([name], false, null), conditions, source, lateral));
        return this;
    }

    private Query SubSource(Query query, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(query, parameterName);
        if (query.Grammar.Dialect != Grammar.Dialect) throw new ArgumentException("The subquery must use the same dialect as this query.", parameterName);
        return query.Clone();
    }

    private static string AliasName(string alias, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(alias, parameterName);
        var identifier = IdentifierGrammar.Parse(alias, parameterName);
        if (identifier.IsStar || identifier.Alias is not null || identifier.JsonPath is not null || identifier.Segments.Count != 1) throw IdentifierGrammar.Invalid(alias, parameterName);
        return identifier.Segments[0];
    }

    private bool HasSubqueryJoin => _joins.Any(join => join.Subquery is not null);
}
