using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Routing.Tests;

/// <summary>Thread-safe event log shared between test middleware and assertions.</summary>
public sealed class Recorder
{
    private readonly List<string> _events = new();
    private readonly object _lock = new();

    public TaskCompletionSource Terminated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Add(string entry)
    {
        lock (_lock) _events.Add(entry);
    }

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_lock) return _events.ToList();
        }
    }
}

public abstract class TraceBase(Recorder log, string label) : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var tag = arguments.Count > 0 ? label + ":" + string.Join("|", arguments) : label;
        log.Add(">" + tag);
        await next(context);
        log.Add("<" + tag);
    }
}

public sealed class TraceA(Recorder log) : TraceBase(log, "A");
public sealed class TraceB(Recorder log) : TraceBase(log, "B");
public sealed class TraceC(Recorder log) : TraceBase(log, "C");
public sealed class TraceD(Recorder log) : TraceBase(log, "D");

public sealed class Deny : IRouteMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

public sealed class Terminating(Recorder log) : IRouteMiddleware, ITerminableMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        log.Add(">T");
        await next(context);
        log.Add("<T");
    }

    public Task TerminateAsync(HttpContext context, MiddlewareArguments arguments)
    {
        log.Add("terminated");
        log.Terminated.TrySetResult();
        return Task.CompletedTask;
    }
}

/// <summary>A plain ASP.NET Core IMiddleware, to test the adapter.</summary>
public sealed class Stamp : Microsoft.AspNetCore.Http.IMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Stamp"] = "1";
        return next(context);
    }
}

public sealed record User(int Id, string Name);

public sealed class CustomMarker;

[ApiController]
[Route("ctl")]
[Middleware("a")]
public class SampleController : ControllerBase
{
    [HttpGet("index")]
    [Middleware("b", Only = new[] { "Index" })]
    public IActionResult Index() => Content("index");

    [HttpGet("show")]
    [Middleware("c", Except = new[] { "Show" })]
    public IActionResult Show() => Content("show");

    [HttpGet("skip")]
    [WithoutMiddleware("a")]
    public IActionResult Skip() => Content("skip");
}

/// <summary>A real ASP.NET Core host on TestServer with Naravel routing and the aliases a, b, c, d, deny, t, stamp.</summary>
public sealed class TestApp : IAsyncDisposable
{
    private TestApp(WebApplication app, HttpClient client, Recorder log)
    {
        App = app;
        Client = client;
        Log = log;
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public Recorder Log { get; }

    public IUrlGenerator Urls => App.Services.GetRequiredService<IUrlGenerator>();

    public IEnumerable<Endpoint> Endpoints => ((IEndpointRouteBuilder)App).DataSources.SelectMany(d => d.Endpoints);

    public static async Task<TestApp> StartAsync(
        Action<IRouteRegistrar>? routes = null,
        Action<RoutingOptions>? options = null,
        Action<WebApplication>? native = null,
        bool controllers = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var log = new Recorder();
        builder.Services.AddSingleton(log);
        builder.Services.AddNaravelRouting(o =>
        {
            o.Middleware
                .Alias<TraceA>("a").Alias<TraceB>("b").Alias<TraceC>("c").Alias<TraceD>("d")
                .Alias<Deny>("deny").Alias<Terminating>("t").Alias<Stamp>("stamp");
            options?.Invoke(o);
        });

        if (controllers)
        {
            builder.Services.AddControllers().AddApplicationPart(typeof(TestApp).Assembly);
        }

        var app = builder.Build();
        app.UseNaravelRouting();
        native?.Invoke(app);
        if (routes is not null) app.MapNaravel(routes);
        if (controllers) app.MapControllers();

        await app.StartAsync();
        var server = (TestServer)app.Services.GetRequiredService<IServer>();
        return new TestApp(app, server.CreateClient(), log);
    }

    public async Task<string> GetStringAsync(string url)
    {
        var response = await Client.GetAsync(url);
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> SendStringAsync(HttpMethod method, string url)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(method, url));
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
