namespace Naravel.Database;

internal abstract class WhereNode(bool or, bool not)
{
    public bool Or { get; } = or;

    public bool Not { get; } = not;
}

internal sealed class BasicWhere(ParsedIdentifier column, string op, object? value, bool or, bool not) : WhereNode(or, not)
{
    public ParsedIdentifier Column { get; } = column;

    public string Operator { get; } = op;

    public object? Value { get; } = value;
}

internal sealed class NullWhere(ParsedIdentifier column, bool isNull, bool or, bool not) : WhereNode(or, not)
{
    public ParsedIdentifier Column { get; } = column;

    public bool IsNull { get; } = isNull;
}

internal sealed class NestedWhere(IReadOnlyList<WhereNode> nodes, bool or, bool not) : WhereNode(or, not)
{
    public IReadOnlyList<WhereNode> Nodes { get; } = nodes;
}

internal sealed class RawWhere(string sql, object?[] bindings, bool or, bool not) : WhereNode(or, not)
{
    public string Sql { get; } = sql;

    public object?[] Bindings { get; } = bindings;
}

internal sealed class InWhere(ParsedIdentifier column, object?[] values, bool negate, bool or) : WhereNode(or, false)
{
    public ParsedIdentifier Column { get; } = column;

    public object?[] Values { get; } = values;

    public bool Negate { get; } = negate;
}

internal sealed class BetweenWhere(ParsedIdentifier column, object? low, object? high, bool negate, bool or) : WhereNode(or, false)
{
    public ParsedIdentifier Column { get; } = column;

    public object? Low { get; } = low;

    public object? High { get; } = high;

    public bool Negate { get; } = negate;
}

internal sealed class ColumnBetweenWhere(ParsedIdentifier column, ParsedIdentifier lowColumn, ParsedIdentifier highColumn, bool negate, bool or) : WhereNode(or, false)
{
    public ParsedIdentifier Column { get; } = column;

    public ParsedIdentifier LowColumn { get; } = lowColumn;

    public ParsedIdentifier HighColumn { get; } = highColumn;

    public bool Negate { get; } = negate;
}

internal sealed class ColumnWhere(ParsedIdentifier first, string op, ParsedIdentifier second, bool or) : WhereNode(or, false)
{
    public ParsedIdentifier First { get; } = first;

    public string Operator { get; } = op;

    public ParsedIdentifier Second { get; } = second;
}

internal enum DatePartKind
{
    Date,
    Month,
    Day,
    Year,
    Time,
}

internal sealed class DateWhere(DatePartKind kind, ParsedIdentifier column, string op, object value, bool or) : WhereNode(or, false)
{
    public DatePartKind Kind { get; } = kind;

    public ParsedIdentifier Column { get; } = column;

    public string Operator { get; } = op;

    public object Value { get; } = value;
}

internal sealed class LikeWhere(ParsedIdentifier column, string pattern, bool caseSensitive, bool negate, bool or) : WhereNode(or, false)
{
    public ParsedIdentifier Column { get; } = column;

    public string Pattern { get; } = pattern;

    public bool CaseSensitive { get; } = caseSensitive;

    public bool Negate { get; } = negate;
}

internal sealed class FullTextWhere(IReadOnlyList<ParsedIdentifier> columns, string value, FullTextMode mode, bool or) : WhereNode(or, false)
{
    public IReadOnlyList<ParsedIdentifier> Columns { get; } = columns;

    public string Value { get; } = value;

    public FullTextMode Mode { get; } = mode;
}

internal sealed class JsonContainsWhere(ParsedIdentifier column, object value, bool or, bool not) : WhereNode(or, not)
{
    public ParsedIdentifier Column { get; } = column;

    public object Value { get; } = value;
}

internal sealed class JsonKeyWhere(ParsedIdentifier column, bool or, bool not) : WhereNode(or, not)
{
    public ParsedIdentifier Column { get; } = column;
}

internal sealed class JsonLengthWhere(ParsedIdentifier column, string op, int value, bool or) : WhereNode(or, false)
{
    public ParsedIdentifier Column { get; } = column;

    public string Operator { get; } = op;

    public int Value { get; } = value;
}

internal sealed class ExistsWhere(Query subquery, bool or, bool not) : WhereNode(or, not)
{
    public Query Subquery { get; } = subquery;
}

internal sealed class SubqueryWhere(ParsedIdentifier column, string op, Query subquery, bool or, bool not) : WhereNode(or, not)
{
    public ParsedIdentifier Column { get; } = column;

    public string Operator { get; } = op;

    public Query Subquery { get; } = subquery;
}

internal sealed class InSubqueryWhere(ParsedIdentifier column, Query subquery, bool negate, bool or) : WhereNode(or, false)
{
    public ParsedIdentifier Column { get; } = column;

    public Query Subquery { get; } = subquery;

    public bool Negate { get; } = negate;
}

internal enum JoinKind
{
    Inner,
    Left,
    Right,
    Cross,
}

internal sealed class JoinItem(JoinKind kind, ParsedIdentifier table, IReadOnlyList<WhereNode> conditions)
{
    public JoinKind Kind { get; } = kind;

    public ParsedIdentifier Table { get; } = table;

    public IReadOnlyList<WhereNode> Conditions { get; } = conditions;
}

internal sealed class SelectItem(ParsedIdentifier? identifier, string? rawSql, object?[] bindings)
{
    public ParsedIdentifier? Identifier { get; } = identifier;

    public string? RawSql { get; } = rawSql;

    public object?[] Bindings { get; } = bindings;
}

internal sealed class OrderItem(ParsedIdentifier? identifier, bool descending, string? rawSql, object?[] bindings)
{
    public ParsedIdentifier? Identifier { get; } = identifier;

    public bool Descending { get; } = descending;

    public string? RawSql { get; } = rawSql;

    public object?[] Bindings { get; } = bindings;
}
