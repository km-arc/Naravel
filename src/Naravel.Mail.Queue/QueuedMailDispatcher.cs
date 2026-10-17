using Microsoft.Extensions.Options;
using Naravel.Queue.Dispatch;

namespace Naravel.Mail.Queue;

internal sealed class QueuedMailDispatcher(IJobDispatcher dispatcher, IOptions<MailQueueOptions> options) : IMailQueue
{
    public Task EnqueueAsync(MailMessage message, string? transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var job = SendQueuedMailJob.FromMessage(message, transport);
        var placement = options.Value;
        return dispatcher.DispatchAsync(job, dispatch =>
        {
            if (placement.Connection is not null) dispatch.OnConnection(placement.Connection);
            if (placement.Queue is not null) dispatch.OnQueue(placement.Queue);
        }, cancellationToken);
    }
}
