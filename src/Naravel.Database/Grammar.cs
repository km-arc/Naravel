using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Naravel.Database;

/// <summary>Turns a <see cref="Query"/> into SQL for one dialect. Pure string generation: no driver, no connection, so every dialect is unit-testable without a database.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\Query\Grammars\Grammar</c> and its <c>SQLiteGrammar</c>, <c>MySqlGrammar</c>, <c>PostgresGrammar</c>, <c>SqlServerGrammar</c>.</para>
/// <para><b>Safety rules (PDR-022):</b> values are always bound as parameters (<c>@p0</c>, <c>@p1</c>...), never written into the SQL; identifiers pass the
/// frozen grammar of PDR-022 A3 and are quoted per dialect; operators come from an allow-list; anything else must use a <c>*Raw</c> method.
/// A dialect gap throws <see cref="NotSupportedException"/> instead of generating wrong SQL.</para>
/// </remarks>
public abstract class Grammar
{
    private static readonly Regex OperatorWhitespace = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ParameterMarker = new(@"@p(\d+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> CommonOperators = new(StringComparer.Ordinal)
    {
        "=", "<", ">", "<=", ">=", "<>", "!=", "like", "not like",
    };

    private readonly char _open;
    private readonly char _close;

    internal Grammar(DatabaseDialect dialect, int maxParameters, char open, char close)
    {
        Dialect = dialect;
        MaxParameters = maxParameters;
        _open = open;
        _close = close;
    }

    /// <summary>The dialect this grammar writes.</summary>
    public DatabaseDialect Dialect { get; }

    /// <summary>The most parameters one statement may carry on this dialect (PDR-022 A4).</summary>
    public int MaxParameters { get; }

    /// <summary>Returns the grammar for a dialect.</summary>
    /// <param name="dialect">The dialect.</param>
    public static Grammar For(DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.Sqlite => Instances.Sqlite,
        DatabaseDialect.MySql => Instances.MySql,
        DatabaseDialect.Postgres => Instances.Postgres,
        DatabaseDialect.SqlServer => Instances.SqlServer,
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unknown database dialect."),
    };

    /// <summary>Validates an identifier against the frozen grammar and quotes it for this dialect, for example <c>users.id as user_id</c>.</summary>
    /// <param name="identifier">The identifier.</param>
    /// <exception cref="ArgumentException">The identifier does not match the grammar.</exception>
    public string Wrap(string identifier) => Wrap(IdentifierGrammar.Parse(identifier, nameof(identifier)));

    internal virtual IReadOnlyCollection<string> ExtraOperators => Array.Empty<string>();

    internal virtual string? OffsetOnlyLimit => null;

    internal string Wrap(ParsedIdentifier identifier)
    {
        var sql = new StringBuilder();
        for (var i = 0; i < identifier.Segments.Count; i++)
        {
            if (i > 0) sql.Append('.');
            if (identifier.IsStar && i == identifier.Segments.Count - 1) sql.Append('*');
            else sql.Append(Quote(identifier.Segments[i]));
        }

        if (identifier.Alias is not null) sql.Append(" as ").Append(Quote(identifier.Alias));
        return sql.ToString();
    }

    internal string NormalizeOperator(string op, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(op, parameterName);
        var normalized = OperatorWhitespace.Replace(op.Trim(), " ").ToLowerInvariant();
        if (CommonOperators.Contains(normalized) || ExtraOperators.Contains(normalized)) return normalized;
        if (AllExtraOperators.Contains(normalized)) throw new NotSupportedException($"The operator '{normalized}' is not supported by the {Dialect} dialect.");
        throw new ArgumentException($"'{IdentifierGrammar.Truncate(op)}' is not an allowed operator.", parameterName);
    }

    internal static int CountPlaceholders(string sql)
    {
        var count = 0;
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] != '?') continue;
            if (i + 1 < sql.Length && sql[i + 1] == '?') i++;
            else count++;
        }

        return count;
    }

    internal CompiledQuery CompileSelect(Query query)
    {
        if (query.TableName is null) throw new InvalidOperationException("The query has no table. Call Table(...) before compiling it.");

        var context = new CompileContext();
        var sql = new StringBuilder("select ");
        if (query.IsDistinct) sql.Append("distinct ");
        AppendTop(sql, query);
        AppendColumns(sql, query, context);
        sql.Append(" from ").Append(Wrap(query.TableName));
        if (query.Wheres.Count > 0)
        {
            sql.Append(" where ");
            AppendWheres(sql, query.Wheres, context);
        }

        AppendOrders(sql, query, context);
        AppendLimitOffset(sql, query);

        if (context.Bindings.Count > MaxParameters)
        {
            throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but this query has {context.Bindings.Count}.");
        }

        return new CompiledQuery(sql.ToString(), context.Bindings.ToArray());
    }

    /// <summary>Writes the bound values into the SQL for display only. Never execute the result.</summary>
    internal string Substitute(CompiledQuery compiled) =>
        ParameterMarker.Replace(compiled.Sql, match =>
        {
            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return index < compiled.Bindings.Count ? FormatLiteral(compiled.Bindings[index]) : match.Value;
        });

    internal virtual void AppendTop(StringBuilder sql, Query query)
    {
    }

    internal virtual void AppendLimitOffset(StringBuilder sql, Query query)
    {
        if (query.LimitValue is { } limit) sql.Append(" limit ").Append(limit.ToString(CultureInfo.InvariantCulture));
        else if (query.OffsetValue.HasValue && OffsetOnlyLimit is { } unbounded) sql.Append(" limit ").Append(unbounded);
        if (query.OffsetValue is { } offset) sql.Append(" offset ").Append(offset.ToString(CultureInfo.InvariantCulture));
    }

    internal virtual string FormatBool(bool value) => value ? "1" : "0";

    internal virtual string FormatString(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    internal virtual string FormatBinary(byte[] value) => "x'" + Convert.ToHexString(value) + "'";

    internal string FormatLiteral(object? value) => value switch
    {
        null => "NULL",
        DBNull => "NULL",
        bool b => FormatBool(b),
        string s => FormatString(s),
        char c => FormatString(c.ToString()),
        byte[] bytes => FormatBinary(bytes),
        Guid g => "'" + g.ToString("D") + "'",
        DateTime d => "'" + d.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "'",
        DateTimeOffset d => "'" + d.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture) + "'",
        DateOnly d => "'" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'",
        TimeOnly t => "'" + t.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "'",
        Enum e => Convert.ToDecimal(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => FormatString(value.ToString() ?? string.Empty),
    };

    private static IReadOnlySet<string> AllExtraOperators { get; } =
        new HashSet<string>(
            new SqliteGrammar().ExtraOperators
                .Concat(new MySqlGrammar().ExtraOperators)
                .Concat(new PostgresGrammar().ExtraOperators)
                .Concat(new SqlServerGrammar().ExtraOperators),
            StringComparer.Ordinal);

    private string Quote(string segment) => _open + segment.Replace(_close.ToString(), _close.ToString() + _close, StringComparison.Ordinal) + _close;

    private void AppendColumns(StringBuilder sql, Query query, CompileContext context)
    {
        if (query.Columns.Count == 0)
        {
            sql.Append('*');
            return;
        }

        for (var i = 0; i < query.Columns.Count; i++)
        {
            if (i > 0) sql.Append(", ");
            var item = query.Columns[i];
            if (item.Identifier is not null) sql.Append(Wrap(item.Identifier));
            else AppendRaw(sql, item.RawSql!, item.Bindings, context);
        }
    }

    private void AppendWheres(StringBuilder sql, IReadOnlyList<WhereNode> nodes, CompileContext context)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (i > 0) sql.Append(node.Or ? " or " : " and ");
            if (node.Not) sql.Append("not ");
            switch (node)
            {
                case BasicWhere basic:
                    sql.Append(Wrap(basic.Column)).Append(' ').Append(basic.Operator).Append(' ').Append(context.Add(basic.Value));
                    break;
                case NullWhere nullWhere:
                    sql.Append(Wrap(nullWhere.Column)).Append(nullWhere.IsNull ? " is null" : " is not null");
                    break;
                case NestedWhere nested:
                    sql.Append('(');
                    AppendWheres(sql, nested.Nodes, context);
                    sql.Append(')');
                    break;
                case RawWhere raw:
                    sql.Append('(');
                    AppendRaw(sql, raw.Sql, raw.Bindings, context);
                    sql.Append(')');
                    break;
                case InWhere inWhere:
                    AppendIn(sql, inWhere, context);
                    break;
                case BetweenWhere between:
                    sql.Append(Wrap(between.Column)).Append(between.Negate ? " not between " : " between ")
                        .Append(context.Add(between.Low)).Append(" and ").Append(context.Add(between.High));
                    break;
                case ColumnBetweenWhere columnBetween:
                    sql.Append(Wrap(columnBetween.Column)).Append(columnBetween.Negate ? " not between " : " between ")
                        .Append(Wrap(columnBetween.LowColumn)).Append(" and ").Append(Wrap(columnBetween.HighColumn));
                    break;
                case ColumnWhere columnWhere:
                    sql.Append(Wrap(columnWhere.First)).Append(' ').Append(columnWhere.Operator).Append(' ').Append(Wrap(columnWhere.Second));
                    break;
                default:
                    throw new InvalidOperationException("Unknown where clause.");
            }
        }
    }

    private void AppendIn(StringBuilder sql, InWhere node, CompileContext context)
    {
        if (node.Values.Length == 0)
        {
            sql.Append(node.Negate ? "1 = 1" : "0 = 1");
            return;
        }

        sql.Append(Wrap(node.Column)).Append(node.Negate ? " not in (" : " in (");
        for (var i = 0; i < node.Values.Length; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append(context.Add(node.Values[i]));
        }

        sql.Append(')');
    }

    private void AppendOrders(StringBuilder sql, Query query, CompileContext context)
    {
        if (query.Orders.Count == 0) return;
        sql.Append(" order by ");
        for (var i = 0; i < query.Orders.Count; i++)
        {
            if (i > 0) sql.Append(", ");
            var order = query.Orders[i];
            if (order.Identifier is not null) sql.Append(Wrap(order.Identifier)).Append(order.Descending ? " desc" : " asc");
            else AppendRaw(sql, order.RawSql!, order.Bindings, context);
        }
    }

    private static void AppendRaw(StringBuilder sql, string raw, object?[] bindings, CompileContext context)
    {
        var next = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '?')
            {
                sql.Append(raw[i]);
            }
            else if (i + 1 < raw.Length && raw[i + 1] == '?')
            {
                sql.Append('?');
                i++;
            }
            else
            {
                sql.Append(context.Add(bindings[next++]));
            }
        }
    }

    private sealed class CompileContext
    {
        public List<object?> Bindings { get; } = new();

        public string Add(object? value)
        {
            Bindings.Add(value);
            return "@p" + (Bindings.Count - 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static class Instances
    {
        public static readonly Grammar Sqlite = new SqliteGrammar();
        public static readonly Grammar MySql = new MySqlGrammar();
        public static readonly Grammar Postgres = new PostgresGrammar();
        public static readonly Grammar SqlServer = new SqlServerGrammar();
    }
}

internal sealed class SqliteGrammar() : Grammar(DatabaseDialect.Sqlite, 32766, '"', '"')
{
    private static readonly string[] Extra = ["glob", "not glob"];

    internal override IReadOnlyCollection<string> ExtraOperators => Extra;

    internal override string? OffsetOnlyLimit => "-1";
}

internal sealed class MySqlGrammar() : Grammar(DatabaseDialect.MySql, 65535, '`', '`')
{
    private static readonly string[] Extra = ["rlike", "not rlike", "regexp", "not regexp", "<=>"];

    internal override IReadOnlyCollection<string> ExtraOperators => Extra;

    internal override string? OffsetOnlyLimit => "18446744073709551615";

    internal override string FormatString(string value) =>
        "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";
}

internal sealed class PostgresGrammar() : Grammar(DatabaseDialect.Postgres, 65535, '"', '"')
{
    private static readonly string[] Extra = ["ilike", "not ilike", "~", "~*", "!~", "!~*", "similar to", "not similar to"];

    internal override IReadOnlyCollection<string> ExtraOperators => Extra;

    internal override string FormatBool(bool value) => value ? "true" : "false";

    internal override string FormatBinary(byte[] value) => "'\\x" + Convert.ToHexString(value) + "'";
}

internal sealed class SqlServerGrammar() : Grammar(DatabaseDialect.SqlServer, 2098, '[', ']')
{
    internal override string FormatString(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    internal override string FormatBinary(byte[] value) => "0x" + Convert.ToHexString(value);

    internal override void AppendTop(StringBuilder sql, Query query)
    {
        if (query.LimitValue is { } limit && (query.OffsetValue ?? 0) == 0)
        {
            sql.Append("top (").Append(limit.ToString(CultureInfo.InvariantCulture)).Append(") ");
        }
    }

    internal override void AppendLimitOffset(StringBuilder sql, Query query)
    {
        if ((query.OffsetValue ?? 0) == 0) return;
        if (query.Orders.Count == 0)
        {
            throw new InvalidOperationException("SQL Server needs OrderBy(...) when Offset or Skip is used, because OFFSET ... FETCH requires ORDER BY.");
        }

        sql.Append(" offset ").Append(query.OffsetValue!.Value.ToString(CultureInfo.InvariantCulture)).Append(" rows");
        if (query.LimitValue is { } limit) sql.Append(" fetch next ").Append(limit.ToString(CultureInfo.InvariantCulture)).Append(" rows only");
    }
}
