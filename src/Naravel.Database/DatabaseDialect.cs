namespace Naravel.Database;

/// <summary>The SQL dialects a Naravel database connection can speak.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> the grammar class chosen by the connection (<c>MySqlGrammar</c>, <c>PostgresGrammar</c>,
/// <c>SQLiteGrammar</c>, <c>SqlServerGrammar</c>). The grammar itself arrives with the query compiler (R19.T04).
/// </remarks>
public enum DatabaseDialect
{
    /// <summary>SQLite.</summary>
    Sqlite = 0,

    /// <summary>MySQL and MariaDB.</summary>
    MySql = 1,

    /// <summary>PostgreSQL.</summary>
    Postgres = 2,

    /// <summary>Microsoft SQL Server.</summary>
    SqlServer = 3,
}
