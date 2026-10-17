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
