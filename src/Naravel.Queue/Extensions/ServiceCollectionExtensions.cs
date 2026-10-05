using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Naravel.Foundation;
using Naravel.Queue.Batching;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Drivers;
using Naravel.Queue.Failed;
using Naravel.Queue.Options;
using Naravel.Queue.Serialization;
using System.Text.Json;
using Naravel.Queue.Worker;

namespace Naravel.Queue.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Naravel.Queue core services (manager, driver registry, dispatcher, serializer, batch
    /// tracking) bound from the "NaravelQueue" configuration section. Chain .AddMemoryDriver(configuration)/
    /// .AddRedisDriver(configuration)/... afterwards to register the drivers your stores actually use.
    /// </summary>
    public static IServiceCollection AddQueue(this IServiceCollection services, IConfiguration configuration, string sectionName = "NaravelQueue")
    {
        services.AddNaravelManager<QueueManager, IQueueDriver, QueueOptions>(configuration.GetSection(sectionName));
        services.TryAddSingleton(configuration);
        services.TryAddSingleton<IJobTypeRegistry>(sp => new JobTypeRegistry(sp.GetRequiredService<IOptions<JobTypeRegistryOptions>>()));
        services.TryAddSingleton(_ => new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        services.TryAddSingleton<IJobSerializer, JsonJobSerializer>();
        services.TryAddSingleton<IJobDispatcher, JobDispatcher>();
        services.TryAddSingleton<IFailedJobStore, InMemoryFailedJobStore>();
        services.TryAddSingleton<IBatchRepository, InMemoryBatchRepository>();
        return services;
    }

    /// <summary>
    /// Registers a background worker (queue:work equivalent) as a hosted service. Call this once per
    /// worker you want running in this process - e.g. once per connection, or with different queue
    /// lists for priority separation.
    /// </summary>
    public static IServiceCollection AddQueueWorker(this IServiceCollection services, Action<QueueWorkerOptions>? configure = null)
    {
        var options = new QueueWorkerOptions();
        configure?.Invoke(options);

        // Use a factory + ActivatorUtilities (instead of a plain DI-registered options singleton) so that
        // calling AddQueueWorker(...) multiple times - e.g. one worker per connection/priority - correctly
        // gives each worker its own options instance, rather than every worker resolving the last one registered.
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp =>
            ActivatorUtilities.CreateInstance<QueueWorkerService>(sp, options));

        return services;
    }
}
