using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Broadcasting;
using Naravel.Events;

namespace Naravel.Broadcasting.Tests;

public sealed record OrderShipped(int OrderId) : IBroadcastable
{
    public IEnumerable<string> Channels() => [$"private-orders.{OrderId}"];
}

public sealed record Announcement(string Text) : IBroadcastable
{
    public IEnumerable<string> Channels() => ["news"];

    public string BroadcastAs() => "Announced";
}

public sealed record Marker : IBroadcastable
{
    public IEnumerable<string> Channels() => ["other"];
}

public sealed record Mixed(string Text) : IBroadcastable
{
    public IEnumerable<string> Channels() => ["news", "news", "private-orders.7"];

    public object BroadcastWith() => new { text = Text };
}

internal sealed class BroadcastApp : IAsyncDisposable
{
    private readonly WebApplication _app;

    private BroadcastApp(WebApplication app) => _app = app;

    public IServiceProvider Services => _app.Services;

    public TestServer Server => (TestServer)_app.Services.GetRequiredService<IServer>();

    public static async Task<BroadcastApp> StartAsync(
        Action<BroadcastingOptions>? options = null,
        Action<IServiceCollection>? services = null,
        params (string Key, string? Value)[] config)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(config.ToDictionary(c => $"Broadcasting:{c.Key}", c => c.Value));
        builder.Services.AddNaravelEvents();
        builder.Services.AddNaravelBroadcasting(builder.Configuration, options);
        services?.Invoke(builder.Services);

        var app = builder.Build();
        // A test sign-in: "X-Order: 7" signs the connection in with the claim order=7, no header leaves it anonymous.
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Order", out var order))
            {
                var identity = new ClaimsIdentity([new Claim("order", order.ToString())], authenticationType: "test");
                context.User = new ClaimsPrincipal(identity);
            }

            await next();
        });
        app.MapNaravelBroadcasting("/hubs/broadcast");
        await app.StartAsync();
        return new BroadcastApp(app);
    }

    public HubConnection Connect(string? order = null)
    {
        var handler = Server.CreateHandler();
        var connection = new HubConnectionBuilder()
            .WithUrl(Server.BaseAddress + "hubs/broadcast", o =>
            {
                o.HttpMessageHandlerFactory = _ => handler;
                o.Transports = HttpTransportType.LongPolling;
                if (order is not null) o.Headers["X-Order"] = order;
            })
            .Build();
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

internal sealed class Inbox
{
    private readonly List<(string Method, JsonElement Payload)> _items = new();
    private readonly SemaphoreSlim _signal = new(0);

    public IReadOnlyList<string> Methods
    {
        get
        {
            lock (_items) return _items.Select(i => i.Method).ToArray();
        }
    }

    public void Listen(HubConnection connection, params string[] methods)
    {
        foreach (var method in methods)
        {
            connection.On<JsonElement>(method, payload =>
            {
                lock (_items) _items.Add((method, payload));
                _signal.Release();
            });
        }
    }

    public async Task<JsonElement> NextAsync()
    {
        if (!await _signal.WaitAsync(TimeSpan.FromSeconds(10))) throw new TimeoutException("No message arrived.");
        lock (_items) return _items[^1].Payload;
    }
}
