using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text;
using Dapper;

namespace Naravel.Database;

/// <summary>
/// Runs compiled SQL. It owns no connection: every operation takes a lease from the <see cref="IConnectionScope"/>, sets
/// <c>command.Transaction</c> from it and disposes it, which closes the connection only when the lease owns it (PDR-022 A1).
/// Plain rows are read straight from the <see cref="DbDataReader"/>; Dapper only maps rows to a type (PDR-022 A2).
/// </summary>
internal sealed class DatabaseExecutor(IConnectionScope scope, string? connectionName, DatabaseManager? manager = null, QueryObserver? observer = null) : IQueryRunner
{
    private readonly QueryObserver _observer = observer ?? QueryObserver.None;

    private string ResolvedName => connectionName ?? manager?.DefaultDriverName ?? "default";

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken) =>
        _observer.ObserveAsync(ResolvedName, QueryOperation.Select, sql, bindings, () => RowsCoreAsync(sql, bindings, cancellationToken));

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsCoreAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await scope.AcquireAsync(connectionName, cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(lease, sql, bindings);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var names = new string[reader.FieldCount];
        for (var i = 0; i < names.Length; i++) names[i] = reader.GetName(i);

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(names.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++) row[names[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    public Task<IReadOnlyList<T>> RowsAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken) =>
        _observer.ObserveAsync(ResolvedName, QueryOperation.Select, sql, bindings, () => RowsCoreAsync<T>(sql, bindings, cancellationToken));

    private async Task<IReadOnlyList<T>> RowsCoreAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await scope.AcquireAsync(connectionName, cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(lease, sql, bindings);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<T>();
        if (ValueConverter.IsSimple(typeof(T)))
        {
            // One value per row: the first column, converted by us (Dapper would return the raw provider value).
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(ValueConverter.Convert<T>(reader.GetValue(0)));
            return rows;
        }

        var parser = Parser<T>(reader);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(parser(reader));
        return rows;
    }

    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> StreamAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken) =>
        _observer.ObserveStreamAsync(ResolvedName, QueryOperation.Select, sql, bindings, () => StreamCoreAsync(sql, bindings, cancellationToken), cancellationToken);

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> StreamCoreAsync(string sql, IReadOnlyList<object?> bindings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await scope.AcquireAsync(connectionName, cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(lease, sql, bindings);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var names = new string[reader.FieldCount];
        for (var i = 0; i < names.Length; i++) names[i] = reader.GetName(i);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(names.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++) row[names[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            yield return row;
        }
    }

    public IAsyncEnumerable<T> StreamAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken) =>
        _observer.ObserveStreamAsync(ResolvedName, QueryOperation.Select, sql, bindings, () => StreamCoreAsync<T>(sql, bindings, cancellationToken), cancellationToken);

    private async IAsyncEnumerable<T> StreamCoreAsync<T>(string sql, IReadOnlyList<object?> bindings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await scope.AcquireAsync(connectionName, cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(lease, sql, bindings);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (ValueConverter.IsSimple(typeof(T)))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) yield return ValueConverter.Convert<T>(reader.GetValue(0));
            yield break;
        }

        var parser = Parser<T>(reader);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) yield return parser(reader);
    }

    public Task<T> FirstValueAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken) =>
        _observer.ObserveAsync(ResolvedName, QueryOperation.Scalar, sql, bindings, () => FirstValueCoreAsync<T>(sql, bindings, cancellationToken));

    private async Task<T> FirstValueCoreAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await scope.AcquireAsync(connectionName, cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(lease, sql, bindings);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (ValueConverter.IsSimple(typeof(T)))
        {
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ValueConverter.Convert<T>(reader.GetValue(0)) : default!;
        }

        var parser = Parser<T>(reader);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? parser(reader) : default!;
    }

    public Task<int> NonQueryAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken) =>
        _observer.ObserveAsync(ResolvedName, QueryOperation.Statement, sql, bindings, () => NonQueryCoreAsync(sql, bindings, cancellationToken));

    private async Task<int> NonQueryCoreAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var lease = await scope.AcquireAsync(connectionName, cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(lease, sql, bindings);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns <c>?</c> placeholders into <c>@p0</c>, <c>@p1</c>... (<c>??</c> is a literal question mark) and checks the value count.</summary>
    public static (string Sql, IReadOnlyList<object?> Bindings) Prepare(string sql, IEnumerable<object?>? bindings)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var values = bindings is null ? Array.Empty<object?>() : bindings.ToArray();
        var text = new StringBuilder(sql.Length + 8);
        var index = 0;
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] != '?')
            {
                text.Append(sql[i]);
            }
            else if (i + 1 < sql.Length && sql[i + 1] == '?')
            {
                text.Append('?');
                i++;
            }
            else
            {
                text.Append("@p").Append(index++);
            }
        }

        if (index != values.Length)
        {
            throw new ArgumentException($"The SQL has {index} placeholder(s) but {values.Length} binding(s) were given. Use '??' for a literal question mark.", nameof(bindings));
        }

        return (text.ToString(), values);
    }

    /// <summary>
    /// Builds Dapper's row mapper for <typeparamref name="T"/>. The non-generic <c>GetRowParser(Type)</c> is used on purpose: the generic
    /// <c>GetRowParser&lt;T&gt;(DbDataReader)</c> casts its delegate to <c>Func&lt;DbDataReader, T&gt;</c> and throws <see cref="InvalidCastException"/>
    /// for simple types such as <see cref="string"/>.
    /// </summary>
    private static Func<DbDataReader, T> Parser<T>(DbDataReader reader)
    {
        var parse = reader.GetRowParser(typeof(T));
        return row => parse(row) is { } value ? (T)value : default!;
    }

    private DbCommand CreateCommand(IConnectionLease lease, string sql, IReadOnlyList<object?> bindings)
    {
        var command = lease.Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = lease.Transaction;
        var dialect = manager?.Connection(connectionName).Dialect;
        for (var i = 0; i < bindings.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assign(parameter, bindings[i], dialect);
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>Sets the value and, for the types that providers disagree about, an explicit <see cref="DbType"/> (PDR-022 A2).</summary>
    private static void Assign(DbParameter parameter, object? value, DatabaseDialect? dialect)
    {
        switch (value)
        {
            case null:
                parameter.Value = DBNull.Value;
                return;
            case bool:
                parameter.DbType = DbType.Boolean;
                break;
            case decimal:
                parameter.DbType = DbType.Decimal;
                break;
            case Guid:
                parameter.DbType = DbType.Guid;
                break;
            // Only SQL Server needs help: its default for DateTime is the old low-precision `datetime`. Npgsql treats DbType.DateTime as
            // `timestamptz` and then rejects an unspecified-kind value, so the other providers infer the type from the CLR value.
            case DateTime when dialect == DatabaseDialect.SqlServer:
                parameter.DbType = DbType.DateTime2;
                break;
            case DateTimeOffset when dialect == DatabaseDialect.SqlServer:
                parameter.DbType = DbType.DateTimeOffset;
                break;
            case DateOnly:
                parameter.DbType = DbType.Date;
                break;
            case TimeOnly:
                parameter.DbType = DbType.Time;
                break;
            case byte[]:
                parameter.DbType = DbType.Binary;
                break;
            case Enum enumValue:
                value = Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()), System.Globalization.CultureInfo.InvariantCulture);
                break;
        }

        parameter.Value = value;
    }
}
