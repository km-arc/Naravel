using System.Data.Common;
using MySqlConnector;

namespace Naravel.Database.MySql;

/// <summary>A named MySql connection definition, built on its ADO.NET provider.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\MySqlConnection</c>.</para>
/// <para><b>Why it exists:</b> opens a pooled <see cref="MySqlConnection"/> for each lease. Pooling is the provider's: nothing here keeps a connection open, so a disposed lease returns its connection to the MySqlConnector pool. Credentials stay in the connection string you configure.</para>
/// </remarks>
public sealed class MySqlDatabaseConnection : IDatabaseConnection
{
    private readonly string _connectionString;

    /// <summary>Creates the definition from a connection string.</summary>
    /// <param name="name">The connection name.</param>
    /// <param name="connectionString">For example <c>Server=localhost;Database=app;User ID=app;Password=...</c>.</param>
    public MySqlDatabaseConnection(string name, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Name = name;
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public DatabaseDialect Dialect => DatabaseDialect.MySql;

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
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
