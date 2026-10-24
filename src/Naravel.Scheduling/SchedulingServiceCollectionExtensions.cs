using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;
using Naravel.Scheduling;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the scheduler and its hosted service.</summary>
public static class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a schedule and the hosted service that runs it. The guards use the registered <see cref="ICacheLock"/> (from <c>AddNaravelCache</c>); without one, a process-local lock is used, which is enough for <c>WithoutOverlapping</c> on a single instance but is refused for <c>OnOneServer</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> <c>Schedule</c> in <c>routes/console.php</c> plus the cron line that runs <c>schedule:run</c>; here the host runs the schedule, so there is no cron line.</para>
    /// <para><b>Why it exists:</b> .NET has timers and hosted services but no named schedule with an overlap guard and a one-server guard that work across instances.</para>
    /// <para><b>Not ported on purpose:</b> the PHP command and process tasks (<c>command</c>, <c>exec</c>); see <c>docs/en/scheduling.md</c>.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adds the tasks.</param>
    /// <param name="configureOptions">Optional lock lifetimes.</param>
    public static IServiceCollection AddNaravelScheduling(this IServiceCollection services, Action<Schedule> configure, Action<SchedulingOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var schedule = new Schedule();
        configure(schedule);
        var options = services.AddOptions<SchedulingOptions>();
        if (configureOptions is not null) options.Configure(configureOptions);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(schedule);
        services.AddSingleton(provider => new Scheduler(
            provider.GetRequiredService<Schedule>(),
            provider.GetService<ICacheLock>() ?? new MemoryLock("schedule"),
            provider,
            provider.GetRequiredService<IOptions<SchedulingOptions>>().Value,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ILoggerFactory>()?.CreateLogger("Naravel.Scheduling") ?? NullLogger.Instance));
        services.AddHostedService<SchedulerHostedService>();
        return services;
    }
}
