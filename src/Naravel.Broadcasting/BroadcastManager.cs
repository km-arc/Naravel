using Microsoft.Extensions.Options;
using Naravel.Foundation;

namespace Naravel.Broadcasting;

/// <summary>Resolves named broadcasting drivers and hands out <see cref="IBroadcaster"/> instances bound to them.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Broadcasting\BroadcastManager</c> (<c>connection($name)</c>, <c>extend()</c>).</para>
/// <para><b>Why it exists:</b> choosing <c>log</c> in development, <c>signalr</c> in production and <c>null</c> elsewhere by name is the Manager to Driver job (PDR-004, PDR-005); runtime <c>Extend</c> and configuration reload come from the base class.</para>
/// <para><b>Not ported on purpose:</b> <c>__call</c> forwarding. Inject <see cref="IBroadcaster"/> for the default driver.</para>
/// </remarks>
public sealed class BroadcastManager : Manager<IBroadcastDriver, BroadcastingOptions>
{
    /// <summary>Creates the manager. Normally resolved from DI after <c>AddNaravelBroadcasting</c>.</summary>
    /// <param name="provider">Service provider handed to driver factories.</param>
    /// <param name="registry">The driver registry.</param>
    /// <param name="options">Live broadcasting options.</param>
    public BroadcastManager(IServiceProvider provider, IDriverRegistry<IBroadcastDriver> registry, IOptionsMonitor<BroadcastingOptions> options)
        : base(provider, registry, options)
    {
    }

    /// <summary>
    /// Gets a broadcaster bound to a named driver. The driver is resolved on every send, so a changed <c>Broadcasting:Default</c> or a reloaded driver takes effect without re-injecting.
    /// </summary>
    /// <param name="name">Driver name, or <see langword="null"/> for the default.</param>
    public IBroadcaster Broadcaster(string? name = null) => new DriverBroadcaster(this, name);
}
