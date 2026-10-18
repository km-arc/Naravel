using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Naravel.Database.SqlServer;

/// <summary>A named SqlServer connection definition, built on its ADO.NET provider.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\SqlServerConnection</c>.</para>
/// <para><b>Why it exists:</b> opens a pooled <see cref="SqlConnection"/> for each lease; pooling belongs to the provider. SQL Server limits one statement to 2100 parameters, which the grammar enforces at 2098, and <c>Offset</c> needs <c>OrderBy</c> on this dialect.</para>
/// </remarks>
public sealed class SqlServerDatabaseConnection : IDatabaseConnection
{
    private readonly string _connectionString;

    /// <summary>Creates the definition from a connection string.</summary>
    /// <param name="name">The connection name.</param>
    /// <param name="connectionString">For example <c>Server=localhost;Database=app;User Id=app;Password=...;TrustServerCertificate=true</c>.</param>
    public SqlServerDatabaseConnection(string name, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Name = name;
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public DatabaseDialect Dialect => DatabaseDialect.SqlServer;

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }
}
