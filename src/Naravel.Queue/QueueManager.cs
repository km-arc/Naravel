using Microsoft.Extensions.Options;
using Naravel.Foundation;
using Naravel.Queue.Drivers;
using Naravel.Queue.Options;

namespace Naravel.Queue;

/// <summary>
/// Resolves and caches <see cref="IQueueDriver"/> instances by store name.
/// </summary>
/// <remarks>
/// <b>Laravel equivalent:</b> <c>Illuminate\Queue\QueueManager</c> (<c>Queue::connection('redis')</c>).
/// <para>
/// This class adds nothing to <see cref="Manager{TDriver, TOptions}"/> except the Laravel-familiar
/// method name <see cref="Connection"/> (an alias for <see cref="Manager{TDriver,TOptions}.Driver"/>) -
/// per PDR-005, Naravel's config files use one shared keyword ("Stores") for every module, but each
/// module's C# API keeps Laravel's own vocabulary ("connection" for Queue, "disk" for Filesystem, etc.).
/// </para>
/// <para><b>Migration note (PDR-006):</b> before this class subclassed nothing and hand-rolled driver
/// resolution/caching; it now delegates all of that to <see cref="Manager{TDriver, TOptions}"/>, which
/// additionally provides runtime <c>Extend</c>, config hot-reload, and proper disposal - none of which
/// the hand-rolled version had.</para>
/// </remarks>
public sealed class QueueManager : Manager<IQueueDriver, QueueOptions>
{
    public QueueManager(IServiceProvider provider, IDriverRegistry<IQueueDriver> registry, IOptionsMonitor<QueueOptions> options)
        : base(provider, registry, options)
    {
    }

    /// <summary>Gets the driver for the given connection name, or the default connection if null. Alias for <see cref="Manager{TDriver,TOptions}.Driver"/>.</summary>
    public IQueueDriver Connection(string? name = null) => Driver(name);

    /// <summary>The connection name that will be used when <see cref="Connection"/> is called with null.</summary>
    public string DefaultConnectionName => DefaultDriverName;
}
