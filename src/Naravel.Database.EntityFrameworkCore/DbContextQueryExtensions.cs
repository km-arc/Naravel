using Microsoft.EntityFrameworkCore;

namespace Naravel.Database.EntityFrameworkCore;

/// <summary>Lets the query builder run on a <see cref="DbContext"/>'s connection and transaction.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> none; Eloquent and <c>DB::table</c> share one connection in Laravel. In .NET the two are separate libraries, so this adapter lets
/// them share a connection and a transaction (PDR-022 section 6).</para>
/// <para><b>Why it exists:</b> an EF write and a query-builder write must commit or roll back together. Both run on <c>Database.GetDbConnection()</c> and on the
/// <c>CurrentTransaction</c> of the context at the moment each command runs. Neither side commits or rolls back a transaction it did not start: begin and
/// finish the transaction on the <see cref="DbContext"/>; the builder only joins it. Entities, migrations and change tracking stay in EF Core.</para>
/// <para><b>Not ported on purpose:</b> starting transactions through the borrowed database (it throws, see <see cref="IDatabase.TransactionAsync{T}"/>), and Naravel
/// named connections (the connection is the context's). Supported EF providers: SQLite, SQL Server, PostgreSQL (Npgsql) and MySQL.</para>
/// </remarks>
public static class DbContextQueryExtensions
{
    /// <summary>Starts a query on a table that runs on the context's connection and current transaction.</summary>
    /// <param name="context">The context to borrow the connection from.</param>
    /// <param name="table">The table name (<c>users</c>, <c>app.users</c> or <c>users as u</c>).</param>
    /// <exception cref="NotSupportedException">The context's provider is not relational or not one of the supported databases.</exception>
    public static Naravel.Database.Query Query(this DbContext context, string table)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return new EfCoreDatabase(context, DialectOf(context), QueryObserver.None).Table(table);
    }

    /// <summary>
    /// Returns an <see cref="IDatabase"/> (builder and raw SQL) that runs on the context's connection and current transaction. Queries still reach the
    /// <see cref="IQueryListener"/>s and the query log of <paramref name="database"/>.
    /// </summary>
    /// <param name="database">The Naravel database whose listeners should see the queries.</param>
    /// <param name="context">The context to borrow the connection from.</param>
    /// <exception cref="NotSupportedException">The context's provider is not relational or not one of the supported databases.</exception>
    public static IDatabase Using(this IDatabase database, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(context);
        var observer = database is DatabaseService service ? service.Observer : QueryObserver.None;
        return new EfCoreDatabase(context, DialectOf(context), observer);
    }

    private static DatabaseDialect DialectOf(DbContext context)
    {
        var provider = context.Database.ProviderName;
        if (provider is null || !context.Database.IsRelational())
        {
            throw new NotSupportedException("The query builder needs a relational EF Core provider.");
        }

        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) return DatabaseDialect.Sqlite;
        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)) return DatabaseDialect.SqlServer;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) || provider.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase)) return DatabaseDialect.Postgres;
        if (provider.Contains("MySql", StringComparison.OrdinalIgnoreCase)) return DatabaseDialect.MySql;

        throw new NotSupportedException($"The EF Core provider '{provider}' is not supported. Supported: SQLite, SQL Server, PostgreSQL (Npgsql) and MySQL.");
    }
}
