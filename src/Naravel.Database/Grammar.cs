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

    /// <summary>Whether raw SQL text contains something that looks like one of the builder's own parameter markers (<c>@p0</c>, <c>@p12</c>...). Such text would be bound to a generated value, or substituted by <c>ToRawSql</c>, by accident.</summary>
    internal static bool ContainsParameterMarker(string sql) => ParameterMarker.IsMatch(sql);

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

    /// <summary>The SQL that extracts one part of a date/time column; the engines have no common function for it (PDR-022).</summary>
    internal abstract string DatePart(DatePartKind kind, string column);

    /// <summary>The SQL expression that orders rows randomly; each engine has its own function.</summary>
    internal abstract string RandomOrder { get; }

    /// <summary>The SQL that extracts a JSON path from a column as a comparable value. The path is a bound parameter; <paramref name="hint"/> is the value it is compared with.</summary>
    internal abstract string JsonExtract(string column, IReadOnlyList<string> path, CompileContext context, object? hint);

    /// <summary>The SQL for a pattern comparison; the pattern is already bound as <paramref name="parameter"/>.</summary>
    internal abstract string Like(string column, string parameter, bool caseSensitive, bool negate);

    internal abstract string FullText(IReadOnlyList<string> columns, string parameter, FullTextMode mode);

    internal abstract string JsonContains(string column, IReadOnlyList<string>? path, object value, CompileContext context);

    internal abstract string JsonContainsKey(string column, IReadOnlyList<string> path, CompileContext context);

    internal abstract string JsonLength(string column, IReadOnlyList<string>? path, string op, int value, CompileContext context);

    /// <summary>The SQL expression that sets one JSON path inside <paramref name="expression"/> (a column or a nested call of this function) to a value.</summary>
    internal abstract string JsonSet(string expression, IReadOnlyList<string> path, object? value, CompileContext context);

    /// <summary>How the dialect writes an update that joins other tables.</summary>
    internal virtual UpdateJoinStyle JoinedUpdate => UpdateJoinStyle.FromWhere;

    internal virtual IReadOnlyCollection<string> ExtraOperators => Array.Empty<string>();

    internal virtual string? OffsetOnlyLimit => null;

    internal string Wrap(ParsedIdentifier identifier)
    {
        if (identifier.JsonPath is not null) throw new NotSupportedException("JSON column paths ('col->key') are supported in where clauses only.");
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

    /// <summary>The column as a SQL operand: the quoted name, or the dialect's JSON extraction when the identifier has a path.</summary>
    internal string Col(ParsedIdentifier identifier, CompileContext context, object? hint = null) =>
        identifier.JsonPath is null
            ? Wrap(identifier)
            : JsonExtract(Wrap(new ParsedIdentifier(identifier.Segments, false, null)), identifier.JsonPath, context, hint);

    private string Base(ParsedIdentifier identifier) => Wrap(new ParsedIdentifier(identifier.Segments, false, null));

    internal static string JsonText(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    /// <summary>A scalar as text for engines whose JSON search compares text (SQL Server) and as a native value for SQLite.</summary>
    internal static bool IsJsonScalar(object value) => value is string or bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    internal NotSupportedException NotSupported(string what) => new($"{what} is not supported by the {Dialect} dialect.");

    private static bool IsNumber(object? value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    internal static string? PostgresCast(object? hint) => hint switch
    {
        bool => "boolean",
        _ when IsNumber(hint) => "numeric",
        _ => null,
    };

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
        var context = new CompileContext();
        var sql = new StringBuilder();
        AppendSelect(sql, query, context);

        if (context.Bindings.Count > MaxParameters)
        {
            throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but this query has {context.Bindings.Count}.");
        }

        return new CompiledQuery(sql.ToString(), context.Bindings.ToArray());
    }

    /// <summary>Compiles <paramref name="inner"/> as a derived table or subquery between two fixed pieces of SQL (no bindings of their own).</summary>
    internal CompiledQuery CompileWrapped(string prefix, Query inner, string suffix)
    {
        var context = new CompileContext();
        var sql = new StringBuilder();
        if (inner.HasCtes)
        {
            // SQL Server accepts with only at the front of the statement, so the expressions are hoisted out of the derived table.
            AppendWith(sql, inner, context);
            inner = inner.WithoutCtes();
        }

        sql.Append(prefix).Append('(');
        AppendSelect(sql, inner, context);
        sql.Append(')').Append(suffix);

        if (context.Bindings.Count > MaxParameters)
        {
            throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but this query has {context.Bindings.Count}.");
        }

        return new CompiledQuery(sql.ToString(), context.Bindings.ToArray());
    }

    /// <summary>One quoted name, for fixed aliases.</summary>
    internal string QuoteName(string name) => Quote(name);

    /// <summary>The <c>insert</c> keywords up to and including the table's <c>into</c>; <paramref name="ignore"/> asks the engine to skip rows that violate a unique key.</summary>
    internal virtual string InsertVerb(bool ignore) => ignore ? throw NotSupported("insert or ignore") : "insert into ";

    /// <summary>Text after the values, for engines that put the ignore rule there.</summary>
    internal virtual string InsertSuffix(bool ignore) => string.Empty;

    /// <summary>What an insert with no columns looks like after the table name.</summary>
    internal virtual string EmptyInsert => " default values";

    /// <summary>The most rows one <c>values</c> list may carry.</summary>
    internal virtual int MaxInsertRows => int.MaxValue;

    /// <summary>Text between the column list and <c>values</c> that returns the generated key (SQL Server <c>output</c>), or <see langword="null"/>.</summary>
    internal virtual string? InsertOutput(string quotedColumn) => null;

    /// <summary>Text after the values that returns the generated key (<c>returning</c>), or <see langword="null"/>.</summary>
    internal virtual string? InsertReturning(string quotedColumn) => null;

    /// <summary>How the dialect returns the rows a write touched: <c>returning</c> after the statement, <c>output</c> inside it, or not at all.</summary>
    internal virtual ReturningStyle ReturningMode => ReturningStyle.Suffix;

    /// <summary>The <c>output inserted.a, inserted.b</c> clause (SQL Server), <see langword="null"/> when nothing is returned or the dialect uses <c>returning</c>; throws where the dialect has neither.</summary>
    internal string? ReturningOutput(IReadOnlyList<string>? columns, string source)
    {
        if (columns is null) return null;
        return ReturningMode switch
        {
            ReturningStyle.Output => " output " + string.Join(", ", columns.Select(column => source + "." + Quote(column))),
            ReturningStyle.None => throw NotSupported("Returning the written rows (RETURNING or OUTPUT)"),
            _ => null,
        };
    }

    /// <summary>The trailing <c>returning a, b</c> clause, <see langword="null"/> when nothing is returned or the dialect uses <c>output</c>; throws where the dialect has neither.</summary>
    internal string? ReturningSuffix(IReadOnlyList<string>? columns)
    {
        if (columns is null) return null;
        return ReturningMode switch
        {
            ReturningStyle.Suffix => " returning " + string.Join(", ", columns.Select(Quote)),
            ReturningStyle.None => throw NotSupported("Returning the written rows (RETURNING or OUTPUT)"),
            _ => null,
        };
    }

    /// <summary>A second command that reads the generated key on the same connection (MySQL), or <see langword="null"/> when the insert itself returns it.</summary>
    internal virtual string? LastInsertIdSql => null;

    /// <summary>Compiles one or more <c>insert</c> statements for the rows; a batch is split to stay under the parameter and row limits of the dialect.</summary>
    internal IReadOnlyList<CompiledQuery> CompileInsert(Query query, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, bool ignore, string? suffix = null, IReadOnlyList<string>? returning = null)
    {
        var table = Wrap(query.TableName ?? throw new InvalidOperationException("The query has no table. Call Table(...) before inserting."));
        if (columns.Count == 0 && rows.Count != 1) throw new ArgumentException("Rows without columns cannot be inserted in a batch.", "rows");
        if (columns.Count > MaxParameters) throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but one row has {columns.Count} values.");

        var output = ReturningOutput(returning, "inserted");
        var tail = ReturningSuffix(returning);
        var perStatement = columns.Count == 0 ? 1 : Math.Min(MaxInsertRows, MaxParameters / columns.Count);
        var statements = new List<CompiledQuery>();
        for (var start = 0; start < rows.Count; start += perStatement)
        {
            var context = new CompileContext();
            var sql = new StringBuilder(InsertVerb(ignore)).Append(table);
            var take = Math.Min(perStatement, rows.Count - start);
            AppendInsertValues(sql, columns, rows, start, take, context, output);
            sql.Append(suffix ?? InsertSuffix(ignore)).Append(tail);
            if (context.Bindings.Count > MaxParameters)
            {
                throw new InvalidOperationException($"A statement of this insert has {context.Bindings.Count} parameters but the {Dialect} dialect allows at most {MaxParameters}: a Raw value with several bindings counts as one when a batch is split. Insert fewer rows per call.");
            }

            statements.Add(new CompiledQuery(sql.ToString(), context.Bindings.ToArray()));
        }

        return statements;
    }

    /// <summary>The text after the values that turns an insert into an upsert (<c>on conflict</c>, <c>on duplicate key update</c>); <paramref name="uniqueBy"/> is unused by MySQL, which reacts to any unique key.</summary>
    internal virtual string UpsertSuffix(IReadOnlyList<string> uniqueBy, IReadOnlyList<string> update) =>
        " on conflict (" + string.Join(", ", uniqueBy.Select(Quote)) + ") do update set " + string.Join(", ", update.Select(column => $"{Quote(column)} = excluded.{Quote(column)}"));

    /// <summary>Compiles one or more upsert statements; a batch is split like an insert so it stays under the parameter and row limits.</summary>
    internal virtual IReadOnlyList<CompiledQuery> CompileUpsert(Query query, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, IReadOnlyList<string> uniqueBy, IReadOnlyList<string> update) =>
        CompileInsert(query, columns, rows, false, UpsertSuffix(uniqueBy, update));

    /// <summary>Compiles one <c>insert</c> that also returns the generated key, with <c>output</c> or <c>returning</c> where the engine has it.</summary>
    internal CompiledQuery CompileInsertGetId(Query query, IReadOnlyList<string> columns, object?[] row, string idColumn)
    {
        var table = Wrap(query.TableName ?? throw new InvalidOperationException("The query has no table. Call Table(...) before inserting."));
        if (columns.Count > MaxParameters) throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but one row has {columns.Count} values.");

        var quotedId = Quote(idColumn);
        var context = new CompileContext();
        var sql = new StringBuilder(InsertVerb(ignore: false)).Append(table);
        AppendInsertValues(sql, columns, [row], 0, 1, context, InsertOutput(quotedId));
        if (InsertReturning(quotedId) is { } returning) sql.Append(returning);
        return new CompiledQuery(sql.ToString(), context.Bindings.ToArray());
    }

    /// <summary>Compiles <c>insert into t (cols) select ...</c>; the source shares the parameter numbering.</summary>
    internal CompiledQuery CompileInsertUsing(Query query, IReadOnlyList<string> columns, Query source)
    {
        var table = Wrap(query.TableName ?? throw new InvalidOperationException("The query has no table. Call Table(...) before inserting."));
        var context = new CompileContext();
        var sql = new StringBuilder(InsertVerb(ignore: false)).Append(table).Append(" (").Append(string.Join(", ", columns.Select(Quote))).Append(") ");
        AppendSelect(sql, source, context);

        if (context.Bindings.Count > MaxParameters)
        {
            throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but this query has {context.Bindings.Count}.");
        }

        return new CompiledQuery(sql.ToString(), context.Bindings.ToArray());
    }

    private void AppendInsertValues(StringBuilder sql, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, int start, int take, CompileContext context, string? output)
    {
        if (columns.Count == 0)
        {
            sql.Append(output is null ? string.Empty : output).Append(EmptyInsert);
            return;
        }

        sql.Append(" (").Append(string.Join(", ", columns.Select(Quote))).Append(')');
        if (output is not null) sql.Append(output);
        sql.Append(" values ");
        for (var r = 0; r < take; r++)
        {
            if (r > 0) sql.Append(", ");
            sql.Append('(');
            var values = rows[start + r];
            for (var c = 0; c < values.Length; c++)
            {
                if (c > 0) sql.Append(", ");
                sql.Append(context.Add(values[c]));
            }

            sql.Append(')');
        }
    }

    /// <summary>Compiles <c>delete</c>, with joins where the dialect can express them.</summary>
    internal CompiledQuery CompileDelete(Query query, IReadOnlyList<string>? returning = null)
    {
        var mid = ReturningOutput(returning, "deleted");
        if (query.TableName is not { } table) throw new InvalidOperationException("The query has no table. Call Table(...) before deleting.");
        var context = new CompileContext();
        var sql = new StringBuilder("delete ");
        var wrapped = Wrap(table);
        var bare = Wrap(new ParsedIdentifier(table.Segments, false, null));
        var target = table.Alias is null ? bare : Quote(table.Alias);

        if (query.Joins.Count == 0)
        {
            if (JoinedUpdate == UpdateJoinStyle.From && table.Alias is not null) sql.Append(target).Append(mid).Append(" from ").Append(wrapped);
            else sql.Append("from ").Append(wrapped).Append(mid);
            AppendWhereClause(sql, query.Wheres, context);
        }
        else
        {
            switch (JoinedUpdate)
            {
                case UpdateJoinStyle.Inline:
                case UpdateJoinStyle.From: // MySQL and SQL Server: delete <target> from <table> <joins> where ...
                    sql.Append(target).Append(mid).Append(" from ").Append(wrapped);
                    AppendJoins(sql, query, context);
                    AppendWhereClause(sql, query.Wheres, context);
                    break;

                default:
                    if (Dialect == DatabaseDialect.Sqlite)
                    {
                        // SQLite has no joined delete: find the rows by rowid in a select that has the joins (a table WITHOUT ROWID cannot be used).
                        sql.Append("from ").Append(wrapped).Append(" where ").Append(Quote("rowid")).Append(" in (select ").Append(target).Append('.').Append(Quote("rowid")).Append(" from ").Append(wrapped);
                        AppendJoins(sql, query, context);
                        AppendWhereClause(sql, query.Wheres, context);
                        sql.Append(')');
                    }
                    else
                    {
                        // PostgreSQL: delete from t using o where ... (inner joins only).
                        foreach (var join in query.Joins)
                        {
                            if (join.Kind != JoinKind.Inner) throw NotSupported("A delete with a left, right or cross join");
                        }

                        sql.Append("from ").Append(wrapped).Append(" using ").Append(Wrap(query.Joins[0].Table));
                        for (var i = 1; i < query.Joins.Count; i++)
                        {
                            sql.Append(" inner join ").Append(Wrap(query.Joins[i].Table)).Append(" on ");
                            AppendWheres(sql, query.Joins[i].Conditions, context);
                        }

                        sql.Append(" where (");
                        AppendWheres(sql, query.Joins[0].Conditions, context);
                        sql.Append(')');
                        if (query.Wheres.Count > 0)
                        {
                            sql.Append(" and (");
                            AppendWheres(sql, query.Wheres, context);
                            sql.Append(')');
                        }
                    }

                    break;
            }
        }

        sql.Append(ReturningSuffix(returning));

        if (context.Bindings.Count > MaxParameters)
        {
            throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but this query has {context.Bindings.Count}.");
        }

        return new CompiledQuery(sql.ToString(), context.Bindings.ToArray());
    }

    /// <summary>Compiles the statements that empty a table and reset its identity counter. <paramref name="resetSequence"/> matters to SQLite only: its counter table exists only after a table used <c>autoincrement</c>.</summary>
    internal virtual IReadOnlyList<CompiledQuery> CompileTruncate(Query query, bool resetSequence)
    {
        var table = Wrap(query.TableName ?? throw new InvalidOperationException("The query has no table. Call Table(...) before truncating."));
        return [new CompiledQuery("truncate table " + table, [])];
    }

    /// <summary>Compiles <c>update ... set ... where ...</c>, with joins where the dialect can express them.</summary>
    internal CompiledQuery CompileUpdate(Query query, IReadOnlyList<UpdateAssignment> assignments, IReadOnlyList<string>? returning = null)
    {
        var mid = ReturningOutput(returning, "inserted");
        if (query.TableName is not { } table) throw new InvalidOperationException("The query has no table. Call Table(...) before updating.");
        var context = new CompileContext();
        var sql = new StringBuilder("update ");
        var hasJoins = query.Joins.Count > 0;
        var wrapped = Wrap(table);

        switch (JoinedUpdate)
        {
            case UpdateJoinStyle.Inline: // MySQL: the joins sit between the table and set.
                sql.Append(wrapped);
                AppendJoins(sql, query, context);
                AppendSet(sql, assignments, context);
                sql.Append(mid);
                AppendWhereClause(sql, query.Wheres, context);
                break;

            case UpdateJoinStyle.From: // SQL Server: the target is the alias or table, and a from clause repeats the table with its joins.
                sql.Append(table.Alias is null ? wrapped : Quote(table.Alias));
                AppendSet(sql, assignments, context);
                sql.Append(mid);
                if (hasJoins || table.Alias is not null)
                {
                    sql.Append(" from ").Append(wrapped);
                    AppendJoins(sql, query, context);
                }

                AppendWhereClause(sql, query.Wheres, context);
                break;

            default: // PostgreSQL and SQLite: update from; the first join's condition moves into where.
                sql.Append(wrapped);
                AppendSet(sql, assignments, context);
                sql.Append(mid);
                if (hasJoins)
                {
                    foreach (var join in query.Joins)
                    {
                        if (join.Kind != JoinKind.Inner) throw NotSupported("An update with a left, right or cross join");
                    }

                    sql.Append(" from ").Append(Wrap(query.Joins[0].Table));
                    for (var i = 1; i < query.Joins.Count; i++)
                    {
                        sql.Append(" inner join ").Append(Wrap(query.Joins[i].Table)).Append(" on ");
                        AppendWheres(sql, query.Joins[i].Conditions, context);
                    }

                    sql.Append(" where (");
                    AppendWheres(sql, query.Joins[0].Conditions, context);
                    sql.Append(')');
                    if (query.Wheres.Count > 0)
                    {
                        sql.Append(" and (");
                        AppendWheres(sql, query.Wheres, context);
                        sql.Append(')');
                    }
                }
                else
                {
                    AppendWhereClause(sql, query.Wheres, context);
                }

                break;
        }

        sql.Append(ReturningSuffix(returning));

        if (context.Bindings.Count > MaxParameters)
        {
            throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but this query has {context.Bindings.Count}.");
        }

        return new CompiledQuery(sql.ToString(), context.Bindings.ToArray());
    }

    private void AppendJoins(StringBuilder sql, Query query, CompileContext context)
    {
        foreach (var join in query.Joins)
        {
            if (join.Lateral)
            {
                sql.Append(LateralKeyword(join.Kind == JoinKind.Left));
                AppendJoinSource(sql, join, context);
                sql.Append(LateralOn);
                continue;
            }

            sql.Append(join.Kind switch { JoinKind.Left => " left join ", JoinKind.Right => " right join ", JoinKind.Cross => " cross join ", _ => " inner join " });
            AppendJoinSource(sql, join, context);
            if (join.Kind == JoinKind.Cross) continue;
            sql.Append(" on ");
            AppendWheres(sql, join.Conditions, context);
        }
    }

    private void AppendJoinSource(StringBuilder sql, JoinItem join, CompileContext context)
    {
        if (join.Subquery is null)
        {
            sql.Append(Wrap(join.Table));
            return;
        }

        sql.Append('(');
        AppendSelect(sql, join.Subquery, context);
        sql.Append(") as ").Append(Quote(join.Table.Segments[0]));
    }

    /// <summary>The keyword that starts a lateral join (<c>inner join lateral</c>, <c>cross apply</c>...); throws where the dialect has none.</summary>
    internal virtual string LateralKeyword(bool left) => throw NotSupported("JoinLateral and LeftJoinLateral");

    /// <summary>What follows a lateral subquery: <c>on true</c> where the dialect needs a condition, nothing for <c>apply</c>.</summary>
    internal virtual string LateralOn => string.Empty;

    private void AppendWhereClause(StringBuilder sql, IReadOnlyList<WhereNode> wheres, CompileContext context)
    {
        if (wheres.Count == 0) return;
        sql.Append(" where ");
        AppendWheres(sql, wheres, context);
    }

    private void AppendSet(StringBuilder sql, IReadOnlyList<UpdateAssignment> assignments, CompileContext context)
    {
        sql.Append(" set ");

        // Several JSON paths of one column become one assignment with nested calls; the column keeps its first position.
        var order = new List<string>();
        var plain = new Dictionary<string, UpdateAssignment>(StringComparer.Ordinal);
        var json = new Dictionary<string, List<UpdateAssignment>>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            if (!order.Contains(assignment.Column)) order.Add(assignment.Column);
            if (assignment.JsonPath is null)
            {
                if (json.ContainsKey(assignment.Column) || !plain.TryAdd(assignment.Column, assignment)) throw new ArgumentException($"The column '{assignment.Column}' is assigned more than once.", "values");
            }
            else
            {
                if (plain.ContainsKey(assignment.Column)) throw new ArgumentException($"The column '{assignment.Column}' is assigned as a whole and as a JSON path in the same update.", "values");
                if (!json.TryGetValue(assignment.Column, out var list)) json[assignment.Column] = list = new List<UpdateAssignment>();
                list.Add(assignment);
            }
        }

        for (var i = 0; i < order.Count; i++)
        {
            if (i > 0) sql.Append(", ");
            var column = Quote(order[i]);
            sql.Append(column).Append(" = ");
            if (plain.TryGetValue(order[i], out var single))
            {
                sql.Append(single.Add ? $"{column} + {context.Add(single.Value)}" : context.Add(single.Value));
            }
            else
            {
                var expression = column;
                foreach (var item in json[order[i]]) expression = JsonSet(expression, item.JsonPath!, item.Value, context);
                sql.Append(expression);
            }
        }
    }

    /// <summary>Writes a whole select into <paramref name="sql"/>; a subquery shares the context of the outer statement so parameter numbers stay unique and in text order.</summary>
    private void AppendSelect(StringBuilder sql, Query query, CompileContext context)
    {
        AppendWith(sql, query, context);
        if (query.HasUnions)
        {
            AppendUnion(sql, query, context);
            return;
        }

        if (query.TableName is null && query.FromSubquery is null && query.FromRawSql is null) throw new InvalidOperationException("The query has no table. Call Table(...), FromSub(...) or FromRaw(...) before compiling it.");

        sql.Append("select ");
        if (query.IsDistinct) sql.Append("distinct ");
        AppendTop(sql, query);
        AppendColumns(sql, query, context);
        sql.Append(" from ");
        if (query.FromRawSql is { } fromRaw)
        {
            RefuseLockHint(query);
            AppendRaw(sql, fromRaw, query.FromRawBindings, context);
        }
        else if (query.FromSubquery is { } fromSubquery)
        {
            RefuseLockHint(query);
            sql.Append('(');
            AppendSelect(sql, fromSubquery, context);
            sql.Append(") as ").Append(Quote(query.FromAlias!));
        }
        else
        {
            sql.Append(Wrap(query.TableName!)).Append(LockHint(query.Lock));
        }

        AppendJoins(sql, query, context);

        if (query.Wheres.Count > 0)
        {
            sql.Append(" where ");
            AppendWheres(sql, query.Wheres, context);
        }

        if (query.Groups.Count > 0)
        {
            sql.Append(" group by ");
            for (var i = 0; i < query.Groups.Count; i++)
            {
                if (i > 0) sql.Append(", ");
                var group = query.Groups[i];
                if (group.Identifier is not null) sql.Append(Wrap(group.Identifier));
                else AppendRaw(sql, group.RawSql!, group.Bindings, context);
            }
        }

        if (query.Havings.Count > 0)
        {
            sql.Append(" having ");
            AppendWheres(sql, query.Havings, context);
        }

        AppendOrders(sql, query, context);
        AppendLimitOffset(sql, query);
        sql.Append(LockClause(query.Lock));
    }

    /// <summary>Whether a lock is a table hint written after the table (SQL Server) and not a clause at the end of the select.</summary>
    internal virtual bool LockIsTableHint => false;

    /// <summary>The clause that ends a select with a lock, with its leading space: <c>for share</c> or <c>for update</c>; empty without a lock and where the lock is a table hint. SQLite throws <see cref="NotSupportedException"/>.</summary>
    internal virtual string LockClause(LockMode mode) => mode switch
    {
        LockMode.Shared => " for share",
        LockMode.Update => " for update",
        _ => string.Empty,
    };

    /// <summary>The table hint that carries a lock, with its leading space; empty where the lock is a clause at the end of the select (every dialect but SQL Server).</summary>
    internal virtual string LockHint(LockMode mode) => string.Empty;

    private void RefuseLockHint(Query query)
    {
        if (query.Lock != LockMode.None && LockIsTableHint)
        {
            throw NotSupported("A lock on a query built on FromSub or FromRaw (the lock is a table hint, which a derived table cannot take)");
        }
    }

    /// <summary>Writes <c>with [recursive] name (columns) as (select ...), ... </c> in front of a statement. SQL Server has no <c>recursive</c> keyword.</summary>
    private void AppendWith(StringBuilder sql, Query query, CompileContext context)
    {
        if (!query.HasCtes) return;
        sql.Append("with ");
        if (RecursiveKeyword && query.Ctes.Any(cte => cte.Recursive)) sql.Append("recursive ");
        for (var i = 0; i < query.Ctes.Count; i++)
        {
            var cte = query.Ctes[i];
            if (i > 0) sql.Append(", ");
            sql.Append(Quote(cte.Name));
            if (cte.Columns is not null) sql.Append(" (").Append(string.Join(", ", cte.Columns.Select(Quote))).Append(')');
            sql.Append(" as (");
            AppendSelect(sql, cte.Query, context);
            sql.Append(')');
        }

        sql.Append(' ');
    }

    /// <summary>Whether a recursive expression is introduced with <c>with recursive</c> (every dialect but SQL Server).</summary>
    internal virtual bool RecursiveKeyword => true;

    /// <summary>Writes <c>select ... union [all] select ...</c>. A term with its own ordering, limit or union, and the whole union when it has ordering or a limit, becomes a derived table.</summary>
    private void AppendUnion(StringBuilder sql, Query query, CompileContext context)
    {
        var tail = query.UnionTail();
        var wrapAll = tail.Orders.Count > 0 || tail.LimitValue.HasValue || tail.OffsetValue.HasValue;
        if (wrapAll)
        {
            sql.Append("select ");
            AppendTop(sql, tail);
            sql.Append("* from (");
        }

        AppendUnionTerm(sql, query.UnionHead(), context, 0);
        var index = 1;
        foreach (var union in query.Unions)
        {
            sql.Append(union.All ? " union all " : " union ");
            AppendUnionTerm(sql, union.Query, context, index++);
        }

        if (!wrapAll) return;
        sql.Append(") as ").Append(Quote("naravel_union"));
        AppendOrders(sql, tail, context);
        AppendLimitOffset(sql, tail);
    }

    private void AppendUnionTerm(StringBuilder sql, Query term, CompileContext context, int index)
    {
        if (term.Lock != LockMode.None) throw new InvalidOperationException("SharedLock and LockForUpdate cannot be used on a query with Union or UnionAll, or on one of its terms: the builder cannot write a row lock on a compound select. Lock the rows in a separate query.");
        if (term.Orders.Count == 0 && !term.LimitValue.HasValue && !term.OffsetValue.HasValue && !term.HasUnions && !term.HasCtes)
        {
            AppendSelect(sql, term, context);
            return;
        }

        sql.Append("select * from (");
        AppendSelect(sql, term, context);
        sql.Append(") as ").Append(Quote("naravel_union_" + index.ToString(CultureInfo.InvariantCulture)));
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

    private protected string Quote(string segment) => _open + segment.Replace(_close.ToString(), _close.ToString() + _close, StringComparison.Ordinal) + _close;

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
            if (item.Identifier is not null)
            {
                sql.Append(Wrap(item.Identifier));
            }
            else if (item.Subquery is not null)
            {
                sql.Append('(');
                AppendSelect(sql, item.Subquery, context);
                sql.Append(") as ").Append(Quote(item.Alias!));
            }
            else
            {
                AppendRaw(sql, item.RawSql!, item.Bindings, context);
            }
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
                    sql.Append(Col(basic.Column, context, basic.Value)).Append(' ').Append(basic.Operator).Append(' ').Append(context.Add(basic.Value));
                    break;
                case NullWhere nullWhere:
                    sql.Append(Col(nullWhere.Column, context)).Append(nullWhere.IsNull ? " is null" : " is not null");
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
                    sql.Append(Col(between.Column, context, between.Low)).Append(between.Negate ? " not between " : " between ")
                        .Append(context.Add(between.Low)).Append(" and ").Append(context.Add(between.High));
                    break;
                case ColumnBetweenWhere columnBetween:
                    sql.Append(Col(columnBetween.Column, context)).Append(columnBetween.Negate ? " not between " : " between ")
                        .Append(Col(columnBetween.LowColumn, context)).Append(" and ").Append(Col(columnBetween.HighColumn, context));
                    break;
                case ColumnWhere columnWhere:
                    sql.Append(Col(columnWhere.First, context)).Append(' ').Append(columnWhere.Operator).Append(' ').Append(Col(columnWhere.Second, context));
                    break;
                case DateWhere dateWhere:
                    sql.Append(DatePart(dateWhere.Kind, Wrap(dateWhere.Column))).Append(' ').Append(dateWhere.Operator).Append(' ').Append(context.Add(dateWhere.Value));
                    break;
                case LikeWhere like:
                {
                    var column = Col(like.Column, context, like.Pattern);
                    sql.Append(Like(column, context.Add(like.Pattern), like.CaseSensitive, like.Negate));
                    break;
                }

                case FullTextWhere fullText:
                {
                    var columns = fullText.Columns.Select(column => Wrap(column)).ToArray();
                    sql.Append(FullText(columns, context.Add(fullText.Value), fullText.Mode));
                    break;
                }

                case JsonContainsWhere contains:
                    sql.Append(JsonContains(Base(contains.Column), contains.Column.JsonPath, contains.Value, context));
                    break;
                case JsonKeyWhere key:
                    sql.Append(JsonContainsKey(Base(key.Column), key.Column.JsonPath!, context));
                    break;
                case JsonLengthWhere length:
                    sql.Append(JsonLength(Base(length.Column), length.Column.JsonPath, length.Operator, length.Value, context));
                    break;
                case ExistsWhere exists:
                    sql.Append("exists (");
                    AppendSelect(sql, exists.Subquery, context);
                    sql.Append(')');
                    break;
                case SubqueryWhere compared:
                    sql.Append(Col(compared.Column, context)).Append(' ').Append(compared.Operator).Append(" (");
                    AppendSelect(sql, compared.Subquery, context);
                    sql.Append(')');
                    break;
                case InSubqueryWhere inSubquery:
                    sql.Append(Col(inSubquery.Column, context)).Append(inSubquery.Negate ? " not in (" : " in (");
                    AppendSelect(sql, inSubquery.Subquery, context);
                    sql.Append(')');
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

        sql.Append(Col(node.Column, context, node.Values[0])).Append(node.Negate ? " not in (" : " in (");
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

    internal sealed class CompileContext
    {
        public List<object?> Bindings { get; } = new();

        public string Add(object? value)
        {
            if (value is Raw raw)
            {
                var text = new StringBuilder();
                AppendRaw(text, raw.Sql, raw.Bindings.ToArray(), this);
                return text.ToString();
            }

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
    // SQLite locks the whole database file while it writes and has no row locks, so asking for one is refused instead of being dropped.
    internal override string LockClause(LockMode mode) =>
        mode == LockMode.None ? string.Empty : throw NotSupported("SharedLock and LockForUpdate (SQLite has no row locks)");

    internal override string InsertVerb(bool ignore) => ignore ? "insert or ignore into " : "insert into ";

    internal override string? InsertReturning(string quotedColumn) => " returning " + quotedColumn;

    internal override IReadOnlyList<CompiledQuery> CompileTruncate(Query query, bool resetSequence)
    {
        var table = query.TableName ?? throw new InvalidOperationException("The query has no table. Call Table(...) before truncating.");
        var statements = new List<CompiledQuery> { new("delete from " + Wrap(table), []) };
        if (resetSequence) statements.Add(new CompiledQuery("delete from \"sqlite_sequence\" where \"name\" = @p0", [table.Segments[^1]]));
        return statements;
    }

    internal override string RandomOrder => "random()";

    internal override string JsonExtract(string column, IReadOnlyList<string> path, CompileContext context, object? hint) =>
        $"json_extract({column}, {context.Add(IdentifierGrammar.JsonPathText(path))})";

    internal override string Like(string column, string parameter, bool caseSensitive, bool negate) =>
        caseSensitive
            ? throw NotSupported("A case-sensitive WhereLike (SQLite LIKE ignores ASCII case; GLOB has different wildcards)")
            : $"{column} {(negate ? "not like" : "like")} {parameter}";

    internal override string FullText(IReadOnlyList<string> columns, string parameter, FullTextMode mode) =>
        throw NotSupported("WhereFullText");

    internal override string JsonContains(string column, IReadOnlyList<string>? path, object value, CompileContext context)
    {
        if (!IsJsonScalar(value)) throw NotSupported("WhereJsonContains with an array or object value (only a scalar inside a JSON array)");
        var source = path is null ? column : $"{column}, {context.Add(IdentifierGrammar.JsonPathText(path))}";
        return $"exists (select 1 from json_each({source}) where \"json_each\".\"value\" is {context.Add(value)})";
    }

    internal override string JsonContainsKey(string column, IReadOnlyList<string> path, CompileContext context) =>
        $"json_type({column}, {context.Add(IdentifierGrammar.JsonPathText(path))}) is not null";

    internal override string JsonSet(string expression, IReadOnlyList<string> path, object? value, CompileContext context) =>
        $"json_set({expression}, {context.Add(IdentifierGrammar.JsonPathText(path))}, json({context.Add(JsonText(value!))}))";

    internal override string JsonLength(string column, IReadOnlyList<string>? path, string op, int value, CompileContext context) =>
        $"json_array_length({(path is null ? column : $"{column}, {context.Add(IdentifierGrammar.JsonPathText(path))}")}) {op} {context.Add(value)}";

    internal override string DatePart(DatePartKind kind, string column) => kind switch
    {
        DatePartKind.Date => $"date({column})",
        DatePartKind.Month => $"cast(strftime('%m', {column}) as integer)",
        DatePartKind.Day => $"cast(strftime('%d', {column}) as integer)",
        DatePartKind.Year => $"cast(strftime('%Y', {column}) as integer)",
        _ => $"strftime('%H:%M:%S', {column})",
    };

    private static readonly string[] Extra = ["glob", "not glob"];

    internal override IReadOnlyCollection<string> ExtraOperators => Extra;

    internal override string? OffsetOnlyLimit => "-1";
}

internal sealed class MySqlGrammar() : Grammar(DatabaseDialect.MySql, 65535, '`', '`')
{
    internal override ReturningStyle ReturningMode => ReturningStyle.None;

    internal override string LateralKeyword(bool left) => left ? " left join lateral " : " inner join lateral ";

    internal override string LateralOn => " on true";

    internal override string InsertVerb(bool ignore) => ignore ? "insert ignore into " : "insert into ";

    internal override string EmptyInsert => " () values ()";

    internal override string? LastInsertIdSql => "select last_insert_id()";

    // Row alias instead of the deprecated values() function: MySQL 8.0.19 or later.
    internal override string UpsertSuffix(IReadOnlyList<string> uniqueBy, IReadOnlyList<string> update) =>
        " as `naravel_upsert` on duplicate key update " + string.Join(", ", update.Select(column => $"`{column}` = `naravel_upsert`.`{column}`"));

    internal override string RandomOrder => "rand()";

    internal override string JsonExtract(string column, IReadOnlyList<string> path, CompileContext context, object? hint) =>
        $"json_unquote(json_extract({column}, {context.Add(IdentifierGrammar.JsonPathText(path))}))";

    internal override string Like(string column, string parameter, bool caseSensitive, bool negate) =>
        $"{column} {(negate ? "not like" : "like")}{(caseSensitive ? " binary" : string.Empty)} {parameter}";

    internal override string FullText(IReadOnlyList<string> columns, string parameter, FullTextMode mode) =>
        $"match({string.Join(", ", columns)}) against ({parameter} in {(mode == FullTextMode.Boolean ? "boolean" : "natural language")} mode)";

    internal override string JsonContains(string column, IReadOnlyList<string>? path, object value, CompileContext context)
    {
        var candidate = context.Add(JsonText(value));
        return path is null
            ? $"json_contains({column}, {candidate})"
            : $"json_contains({column}, {candidate}, {context.Add(IdentifierGrammar.JsonPathText(path))})";
    }

    internal override string JsonContainsKey(string column, IReadOnlyList<string> path, CompileContext context) =>
        $"ifnull(json_contains_path({column}, 'one', {context.Add(IdentifierGrammar.JsonPathText(path))}), 0)";

    internal override UpdateJoinStyle JoinedUpdate => UpdateJoinStyle.Inline;

    internal override string JsonSet(string expression, IReadOnlyList<string> path, object? value, CompileContext context) =>
        $"json_set({expression}, {context.Add(IdentifierGrammar.JsonPathText(path))}, cast({context.Add(JsonText(value!))} as json))";

    internal override string JsonLength(string column, IReadOnlyList<string>? path, string op, int value, CompileContext context) =>
        $"json_length({(path is null ? column : $"{column}, {context.Add(IdentifierGrammar.JsonPathText(path))}")}) {op} {context.Add(value)}";

    internal override string DatePart(DatePartKind kind, string column) => kind switch
    {
        DatePartKind.Date => $"date({column})",
        DatePartKind.Month => $"month({column})",
        DatePartKind.Day => $"day({column})",
        DatePartKind.Year => $"year({column})",
        _ => $"time({column})",
    };

    private static readonly string[] Extra = ["rlike", "not rlike", "regexp", "not regexp", "<=>"];

    internal override IReadOnlyCollection<string> ExtraOperators => Extra;

    internal override string? OffsetOnlyLimit => "18446744073709551615";

    internal override string FormatString(string value) =>
        "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";
}

internal sealed class PostgresGrammar() : Grammar(DatabaseDialect.Postgres, 65535, '"', '"')
{
    internal override string LateralKeyword(bool left) => left ? " left join lateral " : " inner join lateral ";

    internal override string LateralOn => " on true";

    internal override string InsertVerb(bool ignore) => "insert into ";

    internal override string InsertSuffix(bool ignore) => ignore ? " on conflict do nothing" : string.Empty;

    // No cascade: truncating a table that foreign keys point at fails instead of silently emptying the tables that reference it.
    internal override IReadOnlyList<CompiledQuery> CompileTruncate(Query query, bool resetSequence) =>
        [new CompiledQuery("truncate table " + Wrap(query.TableName ?? throw new InvalidOperationException("The query has no table. Call Table(...) before truncating.")) + " restart identity", [])];

    internal override string? InsertReturning(string quotedColumn) => " returning " + quotedColumn;

    internal override string RandomOrder => "random()";

    internal override string JsonExtract(string column, IReadOnlyList<string> path, CompileContext context, object? hint)
    {
        var extracted = $"(cast({column} as jsonb) #>> cast({context.Add(IdentifierGrammar.JsonPathArray(path))} as text[]))";
        return PostgresCast(hint) is { } type ? $"cast({extracted} as {type})" : extracted;
    }

    internal override string Like(string column, string parameter, bool caseSensitive, bool negate) =>
        $"{column} {(negate ? "not " : string.Empty)}{(caseSensitive ? "like" : "ilike")} {parameter}";

    internal override string FullText(IReadOnlyList<string> columns, string parameter, FullTextMode mode) =>
        $"({string.Join(" || ", columns.Select(c => $"to_tsvector('english', {c})"))}) @@ {(mode == FullTextMode.Boolean ? "to_tsquery" : "plainto_tsquery")}('english', {parameter})";

    internal override string JsonContains(string column, IReadOnlyList<string>? path, object value, CompileContext context)
    {
        var source = path is null
            ? $"cast({column} as jsonb)"
            : $"(cast({column} as jsonb) #> cast({context.Add(IdentifierGrammar.JsonPathArray(path))} as text[]))";
        return $"{source} @> cast({context.Add(JsonText(value))} as jsonb)";
    }

    internal override string JsonContainsKey(string column, IReadOnlyList<string> path, CompileContext context) =>
        $"coalesce(jsonb_path_exists(cast({column} as jsonb), cast({context.Add(IdentifierGrammar.JsonPathText(path))} as jsonpath)), false)";

    internal override string JsonSet(string expression, IReadOnlyList<string> path, object? value, CompileContext context) =>
        $"jsonb_set(cast({expression} as jsonb), cast({context.Add(IdentifierGrammar.JsonPathArray(path))} as text[]), cast({context.Add(JsonText(value!))} as jsonb))";

    internal override string JsonLength(string column, IReadOnlyList<string>? path, string op, int value, CompileContext context)
    {
        var source = path is null
            ? $"cast({column} as jsonb)"
            : $"(cast({column} as jsonb) #> cast({context.Add(IdentifierGrammar.JsonPathArray(path))} as text[]))";
        return $"jsonb_array_length({source}) {op} {context.Add(value)}";
    }

    internal override string DatePart(DatePartKind kind, string column) => kind switch
    {
        DatePartKind.Date => $"cast({column} as date)",
        DatePartKind.Month => $"extract(month from {column})",
        DatePartKind.Day => $"extract(day from {column})",
        DatePartKind.Year => $"extract(year from {column})",
        _ => $"cast({column} as time)",
    };

    private static readonly string[] Extra = ["ilike", "not ilike", "~", "~*", "!~", "!~*", "similar to", "not similar to"];

    internal override IReadOnlyCollection<string> ExtraOperators => Extra;

    internal override string FormatBool(bool value) => value ? "true" : "false";

    internal override string FormatBinary(byte[] value) => "'\\x" + Convert.ToHexString(value) + "'";
}

internal sealed class SqlServerGrammar() : Grammar(DatabaseDialect.SqlServer, 2098, '[', ']')
{
    internal override ReturningStyle ReturningMode => ReturningStyle.Output;

    internal override bool RecursiveKeyword => false;

    internal override bool LockIsTableHint => true;

    internal override string LockClause(LockMode mode) => string.Empty;

    internal override string LockHint(LockMode mode) => mode switch
    {
        LockMode.Shared => " with (rowlock, holdlock)",
        LockMode.Update => " with (rowlock, updlock, holdlock)",
        _ => string.Empty,
    };

    internal override string LateralKeyword(bool left) => left ? " outer apply " : " cross apply ";

    internal override int MaxInsertRows => 1000;

    internal override string? InsertOutput(string quotedColumn) => " output inserted." + quotedColumn;

    internal override IReadOnlyList<CompiledQuery> CompileUpsert(Query query, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, IReadOnlyList<string> uniqueBy, IReadOnlyList<string> update)
    {
        if (query.TableName is not { } table) throw new InvalidOperationException("The query has no table. Call Table(...) before upserting.");
        if (table.Alias is not null) throw new InvalidOperationException("An upsert query cannot have a table alias.");
        if (columns.Count > MaxParameters) throw new InvalidOperationException($"The {Dialect} dialect allows at most {MaxParameters} parameters per statement, but one row has {columns.Count} values.");

        var perStatement = Math.Min(MaxInsertRows, MaxParameters / columns.Count);
        var statements = new List<CompiledQuery>();
        for (var start = 0; start < rows.Count; start += perStatement)
        {
            var context = new CompileContext();
            var take = Math.Min(perStatement, rows.Count - start);
            var sql = new StringBuilder("merge ").Append(Wrap(table)).Append(" with (holdlock) as [naravel_target] using (values ");
            for (var r = 0; r < take; r++)
            {
                if (r > 0) sql.Append(", ");
                sql.Append('(').Append(string.Join(", ", rows[start + r].Select(value => context.Add(value)))).Append(')');
            }

            sql.Append(") as [naravel_source] (").Append(string.Join(", ", columns.Select(Quote))).Append(") on ")
                .Append(string.Join(" and ", uniqueBy.Select(column => $"[naravel_target].{Quote(column)} = [naravel_source].{Quote(column)}")))
                .Append(" when matched then update set ")
                .Append(string.Join(", ", update.Select(column => $"[naravel_target].{Quote(column)} = [naravel_source].{Quote(column)}")))
                .Append(" when not matched then insert (").Append(string.Join(", ", columns.Select(Quote))).Append(") values (")
                .Append(string.Join(", ", columns.Select(column => $"[naravel_source].{Quote(column)}"))).Append(");");
            statements.Add(new CompiledQuery(sql.ToString(), context.Bindings.ToArray()));
        }

        return statements;
    }

    internal override string RandomOrder => "newid()";

    internal override string JsonExtract(string column, IReadOnlyList<string> path, CompileContext context, object? hint) =>
        $"json_value({column}, {context.Add(IdentifierGrammar.JsonPathText(path))})";

    internal override string Like(string column, string parameter, bool caseSensitive, bool negate) =>
        $"{column} {(negate ? "not like" : "like")} {parameter}{(caseSensitive ? " collate SQL_Latin1_General_CP1_CS_AS" : string.Empty)}";

    internal override string FullText(IReadOnlyList<string> columns, string parameter, FullTextMode mode)
    {
        var list = columns.Count == 1 ? columns[0] : "(" + string.Join(", ", columns) + ")";
        return $"{(mode == FullTextMode.Boolean ? "contains" : "freetext")}({list}, {parameter})";
    }

    internal override string JsonContains(string column, IReadOnlyList<string>? path, object value, CompileContext context)
    {
        if (!IsJsonScalar(value)) throw NotSupported("WhereJsonContains with an array or object value (only a scalar inside a JSON array)");
        var text = value switch
        {
            bool b => b ? "true" : "false",
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        var candidate = context.Add(text);
        var source = path is null ? column : $"{column}, {context.Add(IdentifierGrammar.JsonPathText(path))}";
        return $"{candidate} in (select [value] from openjson({source}))";
    }

    internal override string JsonContainsKey(string column, IReadOnlyList<string> path, CompileContext context)
    {
        var scalar = context.Add(IdentifierGrammar.JsonPathText(path));
        var fragment = context.Add(IdentifierGrammar.JsonPathText(path));
        return $"(json_value({column}, {scalar}) is not null or json_query({column}, {fragment}) is not null)";
    }

    internal override UpdateJoinStyle JoinedUpdate => UpdateJoinStyle.From;

    internal override string JsonSet(string expression, IReadOnlyList<string> path, object? value, CompileContext context)
    {
        var target = context.Add(IdentifierGrammar.JsonPathText(path));
        return value switch
        {
            null => throw NotSupported("Setting a JSON path to null (json_modify would delete the key)"),
            bool flag => $"json_modify({expression}, {target}, cast({context.Add(flag)} as bit))",
            _ when IsJsonScalar(value) => $"json_modify({expression}, {target}, {context.Add(value)})",
            _ => $"json_modify({expression}, {target}, json_query({context.Add(JsonText(value))}))",
        };
    }

    internal override string JsonLength(string column, IReadOnlyList<string>? path, string op, int value, CompileContext context)
    {
        var source = path is null ? column : $"{column}, {context.Add(IdentifierGrammar.JsonPathText(path))}";
        return $"(select count(*) from openjson({source})) {op} {context.Add(value)}";
    }

    internal override string DatePart(DatePartKind kind, string column) => kind switch
    {
        DatePartKind.Date => $"cast({column} as date)",
        DatePartKind.Month => $"datepart(month, {column})",
        DatePartKind.Day => $"datepart(day, {column})",
        DatePartKind.Year => $"datepart(year, {column})",
        _ => $"cast({column} as time)",
    };

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
