using Microsoft.Extensions.Options;
using Naravel.Foundation;

namespace Naravel.Database;

/// <summary>Resolves named database connections (drivers) and caches them.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\DatabaseManager</c> (<c>connection($name)</c>, <c>extend()</c>, <c>purge()</c>).</para>
/// <para><b>Why it exists:</b> named connections that can be added at run time and rebuilt when configuration changes are exactly the
/// Manager to Driver job (PDR-022); runtime <c>Extend</c> and configuration reload come from the base class.</para>
/// <para><b>Not ported on purpose:</b> <c>__call</c> forwarding. Inject <see cref="IConnectionScope"/> (or, from R19.T05, <c>IDatabase</c>).</para>
/// </remarks>
public sealed class DatabaseManager : Manager<IDatabaseConnection, DatabaseOptions>
{
    /// <summary>Creates the manager. Normally resolved from DI after <c>AddNaravelDatabase</c>.</summary>
    /// <param name="provider">Service provider handed to connection factories.</param>
    /// <param name="registry">The connection registry.</param>
    /// <param name="options">Live database options.</param>
    public DatabaseManager(IServiceProvider provider, IDriverRegistry<IDatabaseConnection> registry, IOptionsMonitor<DatabaseOptions> options)
        : base(provider, registry, options)
    {
    }

    /// <summary>Gets a connection definition by name, or the default one when <paramref name="name"/> is <see langword="null"/>. Alias for <c>Driver</c>.</summary>
    /// <param name="name">The connection name, or <see langword="null"/> for the default.</param>
    public IDatabaseConnection Connection(string? name = null) => Driver(name);
}
