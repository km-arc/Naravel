using Microsoft.AspNetCore.Mvc;
using Naravel.Routing;

namespace Naravel.Sample.App.Http.Controllers;

[ApiController]
[Route("routing/controller")]
[Middleware("api-key:X-Sample-Key,naravel-demo", Except = new[] { "PublicEndpoint" })]
[Middleware("audit", Only = new[] { "AuditedEndpoint" })]
public sealed class RoutingDemoController : ControllerBase
{
    [HttpGet("secure")]
    public IActionResult SecureEndpoint() => Ok(new { controller = "attribute route", access = "API key accepted" });

    [HttpGet("public")]
    [WithoutMiddleware("api-key")]
    [WithoutMiddleware("audit")]
    public IActionResult PublicEndpoint() => Ok(new { controller = "attribute route", access = "middleware excluded" });

    [HttpGet("audited")]
    public IActionResult AuditedEndpoint() => Ok(new { controller = "attribute route", middleware = "terminable audit" });
}