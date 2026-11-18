using Naravel.Queue.Jobs;

//#if (namespace != "")
namespace ItemNamespace;
//#elseif (ProjectNamespace != "")
namespace ProjectRootNamespace.App.Jobs;
//#else
#error The namespace is unknown: run dotnet build in the project, generate this file again in an existing folder of the project, or pass --namespace.
//#endif

/// <summary>
/// A queued job. Its public properties are stored as JSON and rebuilt in the queue worker, so keep them small and serializable.
/// Register it with <c>AddJob&lt;NaravelItem&gt;("alias")</c> in Program.cs: the alias is what is stored in the queue, and only
/// registered jobs can be rebuilt. Dispatch it with <c>IJobDispatcher.DispatchAsync</c>.
/// </summary>
public sealed class NaravelItem : Job
{
    public NaravelItem() { } // the worker needs it to rebuild the job from JSON

    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        // Resolve what the job needs from the services of the worker, as a controller would from its constructor.
        return Task.CompletedTask;
    }
}
