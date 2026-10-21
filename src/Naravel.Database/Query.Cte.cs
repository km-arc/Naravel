namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>
    /// Adds a common table expression: <c>with name as (select ...)</c>. Refer to it by name as a table: <c>Table(name)</c>, a join or a subquery. Several expressions may be added; each can use the ones before it.
    /// </summary>
    /// <param name="name">The name, a plain identifier.</param>
    /// <param name="query">The query that defines it, built for the same dialect. It is copied when added.</param>
    /// <exception cref="ArgumentException">The name is not a plain identifier, is already used by another expression of this query, or the query has another dialect.</exception>
    /// <remarks>
    /// Put the expressions on the <b>outermost</b> query: SQL Server accepts <c>with</c> nowhere else. A <c>Union</c> term that has its own expressions is written as a derived table, a count or exists hoists them to the front,
    /// and insert, update, delete and <c>InsertUsingAsync</c> sources refuse them (the position of <c>with</c> differs by dialect and statement). Bound values are numbered in text order: the expressions first.
    /// </remarks>
    public Query WithExpression(string name, Query query) => AddCte(name, null, query, recursive: false);

    /// <summary>Adds a common table expression with a column list: <c>with name (a, b) as (select ...)</c>.</summary>
    /// <param name="name">The name, a plain identifier.</param>
    /// <param name="columns">The names of its columns, plain identifiers, at least one.</param>
    /// <param name="query">The query that defines it, built for the same dialect.</param>
    public Query WithExpression(string name, string[] columns, Query query) => AddCte(name, columns, query, recursive: false);

    /// <summary>
    /// Adds a recursive common table expression. <paramref name="query"/> is the starting rows joined with <c>UnionAll</c> to the step that selects from <paramref name="name"/> itself, which is how to walk a tree or count.
    /// </summary>
    /// <param name="name">The name, a plain identifier; the step refers to it.</param>
    /// <param name="query">The starting query <c>UnionAll</c> the recursive step, built for the same dialect.</param>
    /// <remarks>
    /// MySQL, PostgreSQL and SQLite write <c>with recursive</c> (once, when any expression is recursive); SQL Server writes plain <c>with</c>, as it has no <c>recursive</c> keyword. Use <c>UnionAll</c> (SQL Server requires it) and
    /// a step that stops by itself: a recursion without an end is a loop the database cuts off at its own limit (SQL Server 100 levels unless <c>option (maxrecursion)</c>, which this builder does not write).
    /// </remarks>
    public Query WithRecursiveExpression(string name, Query query) => AddCte(name, null, query, recursive: true);

    /// <summary>Adds a recursive common table expression with a column list: <c>with recursive name (a, b) as (...)</c>.</summary>
    /// <param name="name">The name, a plain identifier; the step refers to it.</param>
    /// <param name="columns">The names of its columns, plain identifiers, at least one.</param>
    /// <param name="query">The starting query <c>UnionAll</c> the recursive step.</param>
    public Query WithRecursiveExpression(string name, string[] columns, Query query) => AddCte(name, columns, query, recursive: true);

    private Query AddCte(string name, string[]? columns, Query query, bool recursive)
    {
        var source = SubSource(query, nameof(query));
        var cteName = AliasName(name, nameof(name));
        if (_ctes.Any(cte => string.Equals(cte.Name, cteName, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"A common table expression named '{cteName}' was already added to this query.", nameof(name));

        string[]? names = null;
        if (columns is not null)
        {
            if (columns.Length == 0) throw new ArgumentException("A column list needs at least one column.", nameof(columns));
            names = columns.Select(column => AliasName(column, nameof(columns))).ToArray();
        }

        _ctes.Add(new CteItem(cteName, names, source, recursive));
        return this;
    }

    /// <summary>A copy without the common table expressions, for the places that write them in front of the statement.</summary>
    internal Query WithoutCtes()
    {
        var copy = Clone();
        copy._ctes.Clear();
        return copy;
    }
}
