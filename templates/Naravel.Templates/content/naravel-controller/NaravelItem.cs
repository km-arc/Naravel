using Microsoft.AspNetCore.Mvc;

//#if (namespace != "")
namespace ItemNamespace;
//#elseif (ProjectNamespace != "")
namespace ProjectRootNamespace.App.Http.Controllers;
//#else
#error The namespace is unknown: run dotnet build in the project, generate this file again in an existing folder of the project, or pass --namespace.
//#endif

/// <summary>
/// A controller. Routes are attributes on its actions, for example <c>[HttpGet("orders")]</c>; <c>MapControllers()</c> in Program.cs
/// picks the class up, and constructor parameters are resolved from the services of the application.
/// </summary>
[ApiController]
public sealed class NaravelItem : ControllerBase
{
}
