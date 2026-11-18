using Naravel.Events;
//#if (ProjectNamespace != "")
using ProjectRootNamespace.App.Events;
//#endif

//#if (namespace != "")
namespace ItemNamespace;
//#elseif (ProjectNamespace != "")
namespace ProjectRootNamespace.App.Listeners;
//#else
#error The namespace is unknown: run dotnet build in the project, generate this file again in an existing folder of the project, or pass --namespace.
//#endif

/// <summary>
/// A listener of <see cref="ItemEvent"/>. It runs inside the dispatch, so keep it short: queue the slow work as a job and return.
/// Register it in Program.cs with <c>AddScoped&lt;IEventListener&lt;ItemEvent&gt;, NaravelItem&gt;()</c>.
/// </summary>
public sealed class NaravelItem : IEventListener<ItemEvent>
{
    public Task HandleAsync(ItemEvent evt, EventDispatchContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
