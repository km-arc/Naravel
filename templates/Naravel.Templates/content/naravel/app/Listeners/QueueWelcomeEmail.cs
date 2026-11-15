using Naravel.Events;
using Naravel.Queue.Dispatch;
using NaravelApp.App.Events;
using NaravelApp.App.Jobs;

namespace NaravelApp.App.Listeners;

/// <summary>
/// A listener of <see cref="UserRegistered"/>. It runs inside the dispatch, so it only queues the slow work as a job and returns.
/// It is registered in Program.cs with <c>AddScoped&lt;IEventListener&lt;UserRegistered&gt;, QueueWelcomeEmail&gt;()</c>.
/// </summary>
public sealed class QueueWelcomeEmail(IJobDispatcher jobs) : IEventListener<UserRegistered>
{
    public async Task HandleAsync(UserRegistered evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        await jobs.DispatchAsync(new SendWelcomeEmail(evt.Email, evt.Name), cancellationToken: cancellationToken);
    }
}
