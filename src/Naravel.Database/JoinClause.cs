using System.Collections;

namespace Naravel.Database;

/// <summary>The conditions of one join, built inside the callback of <see cref="Query.Join(string, Action{JoinClause})"/> and its siblings.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\Query\JoinClause</c>. <c>On</c> compares two columns; <c>Where</c> compares a column with a bound value, so a value never becomes SQL.</para>
/// <para>The same identifier grammar and operator allow-list as <see cref="Query"/> apply. Conditions are joined with <c>and</c> unless an <c>Or</c> form is used.</para>
/// </remarks>
public sealed class JoinClause
{
    private readonly Query _conditions;

    internal JoinClause(Grammar grammar) => _conditions = new Query(grammar);

    internal IReadOnlyList<WhereNode> Conditions => _conditions.Wheres;

    /// <summary>Requires the two columns to be equal. Laravel: <c>on</c>.</summary>
    /// <param name="first">The first column, usually qualified (<c>users.id</c>).</param>
    /// <param name="second">The second column.</param>
    public JoinClause On(string first, string second)
    {
        _conditions.WhereColumn(first, second);
        return this;
    }

    /// <summary>Compares the two columns with an operator. Laravel: <c>on</c>.</summary>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public JoinClause On(string first, string op, string second)
    {
        _conditions.WhereColumn(first, op, second);
        return this;
    }

    /// <summary>Like <see cref="On(string, string)"/> but joined with <c>or</c>. Laravel: <c>orOn</c>.</summary>
    /// <param name="first">The first column.</param>
    /// <param name="second">The second column.</param>
    public JoinClause OrOn(string first, string second)
    {
        _conditions.OrWhereColumn(first, second);
        return this;
    }

    /// <summary>Like <see cref="On(string, string, string)"/> but joined with <c>or</c>. Laravel: <c>orOn</c>.</summary>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public JoinClause OrOn(string first, string op, string second)
    {
        _conditions.OrWhereColumn(first, op, second);
        return this;
    }

    /// <summary>Compares a column with a bound value using <c>=</c> (a <c>null</c> value means <c>is null</c>). Laravel: <c>where</c> on a join.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value, always a parameter.</param>
    public JoinClause Where(string column, object? value)
    {
        _conditions.Where(column, value);
        return this;
    }

    /// <summary>Compares a column with a bound value using an operator. Laravel: <c>where</c> on a join.</summary>
    /// <param name="column">The column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, always a parameter.</param>
    public JoinClause Where(string column, string op, object? value)
    {
        _conditions.Where(column, op, value);
        return this;
    }

    /// <summary>Like <see cref="Where(string, object)"/> but joined with <c>or</c>. Laravel: <c>orWhere</c> on a join.</summary>
    /// <param name="column">The column.</param>
    /// <param name="value">The value, always a parameter.</param>
    public JoinClause OrWhere(string column, object? value)
    {
        _conditions.OrWhere(column, value);
        return this;
    }

    /// <summary>Like <see cref="Where(string, string, object)"/> but joined with <c>or</c>. Laravel: <c>orWhere</c> on a join.</summary>
    /// <param name="column">The column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="value">The value, always a parameter.</param>
    public JoinClause OrWhere(string column, string op, object? value)
    {
        _conditions.OrWhere(column, op, value);
        return this;
    }

    /// <summary>Requires the column to be <c>null</c>. Laravel: <c>whereNull</c> on a join.</summary>
    /// <param name="column">The column.</param>
    public JoinClause WhereNull(string column)
    {
        _conditions.WhereNull(column);
        return this;
    }

    /// <summary>Requires the column not to be <c>null</c>. Laravel: <c>whereNotNull</c> on a join.</summary>
    /// <param name="column">The column.</param>
    public JoinClause WhereNotNull(string column)
    {
        _conditions.WhereNotNull(column);
        return this;
    }

    /// <summary>Requires the column to be one of the values, each a parameter. Laravel: <c>whereIn</c> on a join.</summary>
    /// <param name="column">The column.</param>
    /// <param name="values">The values.</param>
    public JoinClause WhereIn(string column, IEnumerable values)
    {
        _conditions.WhereIn(column, values);
        return this;
    }
}
