using Microsoft.AspNetCore.Mvc;
using Naravel.Events;
using Naravel.Sample.App.Events;
using Naravel.Sample.App.Listeners;

namespace Naravel.Sample.App.Http.Controllers;

[ApiController]
[Route("events")]
public sealed class EventsDemoController : ControllerBase
{
    private static int _orderNumber = 1000;

    private readonly IEventDispatcher _events;
    private readonly EventTrace _trace;
    private readonly IWebHostEnvironment _environment;

    public EventsDemoController(IEventDispatcher events, EventTrace trace, IWebHostEnvironment environment)
    {
        _events = events;
        _trace = trace;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var viewPath = Path.Combine(_environment.ContentRootPath, "resources", "views", "events", "index.html");
        return PhysicalFile(viewPath, "text/html");
    }

    /// <summary>
    /// Dispatches <see cref="OrderPlaced"/>. DI listeners run inside this call; the receipt listener is queued and
    /// shows up in <c>GET /events/trace</c> once the worker has processed it.
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> PlaceOrder([FromQuery] decimal total = 120m, [FromQuery] string email = "ali@example.com", CancellationToken cancellationToken = default)
    {
        var order = new OrderPlaced($"ORD-{Interlocked.Increment(ref _orderNumber)}", email, total);

        // Listen registers a callback for this request's scope only; disposing the subscription removes it.
        // It runs after the DI listeners, so it is skipped when the fraud screen stops propagation.
        using var audit = _events.Listen<OrderPlaced>((evt, _, _) =>
        {
            _trace.Add($"listen-callback: audit entry for {evt.OrderId}");
            return Task.CompletedTask;
        });

        _trace.Add($"dispatch: OrderPlaced {order.OrderId}");
        await _events.DispatchAsync(order, cancellationToken);

        return Ok(new { order, trace = _trace.Snapshot() });
    }

    [HttpGet("trace")]
    public IActionResult Trace() => Ok(_trace.Snapshot());

    [HttpDelete("trace")]
    public IActionResult ClearTrace()
    {
        _trace.Clear();
        return NoContent();
    }
}
