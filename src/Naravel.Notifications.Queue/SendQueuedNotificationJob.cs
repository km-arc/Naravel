using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Jobs;

namespace Naravel.Notifications.Queue;

/// <summary>
/// Queue job that runs the handler registered under <see cref="Alias"/> with the stored <see cref="Payload"/>.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>SendQueuedNotifications</c> job.</para>
/// <para><b>Why it exists:</b> it stores two strings, the alias and the payload as JSON, so no CLR type name, delegate, notification or recipient
/// object is ever written to the queue (PDR-014, OD-07). An unknown alias fails the job instead of guessing a type.</para>
/// <para><b>Delivery guarantee:</b> at-least-once; retries use the standard job backoff.</para>
/// <para><b>Not ported on purpose:</b> serializing the notification object and restoring it by type name.</para>
/// </remarks>
public sealed class SendQueuedNotificationJob : Job
{
    /// <summary>The alias registered with <c>AddQueuedNotification</c>.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>The payload serialized as JSON.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Looks up the alias and runs its handler with the job's service scope.</summary>
    public override async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        var entry = context.Services.GetRequiredService<QueuedNotificationRegistry>().Get(Alias);
        await entry.Run(Payload, context.Services, cancellationToken).ConfigureAwait(false);
    }
}
