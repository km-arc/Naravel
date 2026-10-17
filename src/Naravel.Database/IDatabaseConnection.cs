using System.Data.Common;

namespace Naravel.Database;

/// <summary>
/// A named database connection definition: it knows its dialect and how to open a new ADO.NET connection, and nothing else.
/// This is the "driver" of <see cref="DatabaseManager"/>.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\Connection</c> as created by the connection factory.</para>
/// <para><b>Why it exists:</b> choosing <c>sqlite</c> in tests and <c>pgsql</c> in production by name is the Manager to Driver job.
/// It never builds SQL (that is the grammar, R19.T04) and never owns an open connection: callers get connections through
/// <see cref="IConnectionScope"/>, which is what lets transactions and EF Core share one connection later (PDR-022 A1).</para>
/// <para>Implementations are created once per name by the manager, must be thread-safe, and should make <c>Dispose</c> idempotent.</para>
/// </remarks>
public interface IDatabaseConnection
{
    /// <summary>The configured connection name, for example <c>main</c>.</summary>
    string Name { get; }

    /// <summary>The SQL dialect spoken by this connection.</summary>
    DatabaseDialect Dialect { get; }

    /// <summary>Opens a new connection from the driver's pool. The caller disposes it.</summary>
    /// <param name="cancellationToken">Cancels opening the connection.</param>
    ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
