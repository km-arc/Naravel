using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Naravel.Mail;
using Naravel.Mail.Queue;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the optional queue adapter for Naravel.Mail.</summary>
public static class MailQueueServiceCollectionExtensions
{
    /// <summary>The job alias stored in queued messages.</summary>
    internal const string JobAlias = "naravel.mail.send";

    /// <summary>
    /// Makes <c>QueueAsync</c> work: registers the <see cref="IMailQueue"/> implementation and the
    /// <see cref="SendQueuedMailJob"/> under a stable alias. Call <c>AddQueue</c>, a queue driver and (in worker processes)
    /// <c>AddQueueWorker</c> separately.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> <c>Mail::queue()</c> / <c>ShouldQueue</c> mailables.</para>
    /// <para><b>Why it exists:</b> core <c>Naravel.Mail</c> does not reference the queue; this opt-in package connects them.</para>
    /// <para><b>Not ported on purpose:</b> implicit queueing and serialization of the mailable object.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional connection and queue placement.</param>
    public static IServiceCollection AddNaravelMailQueue(this IServiceCollection services, Action<MailQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<MailQueueOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.AddJob<SendQueuedMailJob>(JobAlias);
        services.Replace(ServiceDescriptor.Singleton<IMailQueue>(provider => new QueuedMailDispatcher(
            provider.GetRequiredService<IJobDispatcher>(),
            provider.GetRequiredService<IOptions<MailQueueOptions>>())));
        return services;
    }
}
