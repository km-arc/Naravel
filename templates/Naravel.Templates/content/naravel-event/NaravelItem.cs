//#if (namespace != "")
namespace ItemNamespace;
//#elseif (ProjectNamespace != "")
namespace ProjectRootNamespace.App.Events;
//#else
#error The namespace is unknown: run dotnet build in the project, generate this file again in an existing folder of the project, or pass --namespace.
//#endif

/// <summary>
/// An application event. Add the values it carries as parameters of the record and keep them small (ids and scalar values): a queued
/// listener or job receives a JSON copy of the payload. Dispatch it with <c>IEventDispatcher.DispatchAsync</c>.
/// </summary>
public sealed record NaravelItem();
