namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>Adds a <c>inner join</c> on <c>first = second</c>. Laravel: <c>join</c>.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="first">The first column, usually qualified.</param>
    /// <param name="second">The second column.</param>
    public Query Join(string table, string first, string second) => AddJoin(JoinKind.Inner, table, join => join.On(first, second));

    /// <summary>Adds a <c>inner join</c> on two columns compared with an operator. Laravel: <c>join</c>.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public Query Join(string table, string first, string op, string second) => AddJoin(JoinKind.Inner, table, join => join.On(first, op, second));

    /// <summary>Adds a <c>inner join</c> whose conditions the callback builds with <c>On</c>, <c>OrOn</c> and <c>Where</c>. Laravel: <c>join</c> with a closure.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="on">Adds at least one condition to the join clause.</param>
    public Query Join(string table, Action<JoinClause> on) => AddJoin(JoinKind.Inner, table, on);

    /// <summary>Adds a <c>left join</c> on <c>first = second</c>. Laravel: <c>leftJoin</c>.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="first">The first column, usually qualified.</param>
    /// <param name="second">The second column.</param>
    public Query LeftJoin(string table, string first, string second) => AddJoin(JoinKind.Left, table, join => join.On(first, second));

    /// <summary>Adds a <c>left join</c> on two columns compared with an operator. Laravel: <c>leftJoin</c>.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public Query LeftJoin(string table, string first, string op, string second) => AddJoin(JoinKind.Left, table, join => join.On(first, op, second));

    /// <summary>Adds a <c>left join</c> whose conditions the callback builds with <c>On</c>, <c>OrOn</c> and <c>Where</c>. Laravel: <c>leftJoin</c> with a closure.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="on">Adds at least one condition to the join clause.</param>
    public Query LeftJoin(string table, Action<JoinClause> on) => AddJoin(JoinKind.Left, table, on);

    /// <summary>Adds a <c>right join</c> on <c>first = second</c>. Laravel: <c>rightJoin</c>.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="first">The first column, usually qualified.</param>
    /// <param name="second">The second column.</param>
    public Query RightJoin(string table, string first, string second) => AddJoin(JoinKind.Right, table, join => join.On(first, second));

    /// <summary>Adds a <c>right join</c> on two columns compared with an operator. Laravel: <c>rightJoin</c>.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="first">The first column.</param>
    /// <param name="op">An allowed operator.</param>
    /// <param name="second">The second column.</param>
    public Query RightJoin(string table, string first, string op, string second) => AddJoin(JoinKind.Right, table, join => join.On(first, op, second));

    /// <summary>Adds a <c>right join</c> whose conditions the callback builds with <c>On</c>, <c>OrOn</c> and <c>Where</c>. Laravel: <c>rightJoin</c> with a closure.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    /// <param name="on">Adds at least one condition to the join clause.</param>
    public Query RightJoin(string table, Action<JoinClause> on) => AddJoin(JoinKind.Right, table, on);

    /// <summary>Adds a <c>cross join</c>, which has no condition. Laravel: <c>crossJoin</c>.</summary>
    /// <param name="table">The joined table, optionally <c>table as alias</c>.</param>
    public Query CrossJoin(string table)
    {
        _joins.Add(new JoinItem(JoinKind.Cross, Plain(IdentifierGrammar.Parse(table, nameof(table)), table, nameof(table), allowAlias: true), []));
        return this;
    }

    private Query AddJoin(JoinKind kind, string table, Action<JoinClause> on)
    {
        var identifier = Plain(IdentifierGrammar.Parse(table, nameof(table)), table, nameof(table), allowAlias: true);
        ArgumentNullException.ThrowIfNull(on);
        var clause = new JoinClause(Grammar);
        on(clause);
        if (clause.Conditions.Count == 0) throw new InvalidOperationException("A join needs at least one condition. Call On(...) or Where(...) inside the callback.");
        _joins.Add(new JoinItem(kind, identifier, clause.Conditions.ToArray()));
        return this;
    }
}
