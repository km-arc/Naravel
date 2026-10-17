using Microsoft.Extensions.Configuration;
using Naravel.Foundation;

namespace Naravel.Database;

/// <summary>Configuration of the database module: the default connection and the named connections.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>config/database.php</c> (<c>default</c>, <c>connections</c>).</para>
/// <para><b>Why it exists:</b> binds <c>Database:Default</c> and <c>Database:Connections:&lt;name&gt;</c> and feeds the shared
/// <c>Manager</c>. <see cref="Connections"/> is the same dictionary the manager reads as <c>Stores</c>. Each connection has a
/// <c>Driver</c> (<c>sqlite</c>, later <c>mysql</c>, <c>pgsql</c>, <c>sqlserver</c>) plus driver-specific keys such as
/// <c>ConnectionString</c>. There is deliberately no built-in default: a query builder that silently picks a database is worse than one
/// that says nothing is configured.</para>
/// </remarks>
public sealed class DatabaseOptions : ManagerOptions
{
    /// <summary>The named connections: <c>Database:Connections:&lt;name&gt;:Driver</c> selects the driver package that creates it.</summary>
    public Dictionary<string, IConfigurationSection> Connections
    {
        get => Stores;
        set => Stores = value;
    }
}
