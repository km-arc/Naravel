using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Foundation;
using StackExchange.Redis;

namespace Naravel.Redis.Tests;

// A multiplexer that is only a counter: every member returns its default, Dispose is counted. No server and no Redis protocol is faked (PDR-010 D4).
public class CountingMultiplexer : DispatchProxy
{
    public int Disposed { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IDisposable.Dispose)) Disposed++;
        if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync)) { Disposed++; return default(ValueTask); }
        var returnType = targetMethod?.ReturnType;
        return returnType is { IsValueType: true } && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null;
    }
}

public sealed class Opener
{
    public List<string> Opened { get; } = new();

    public List<CountingMultiplexer> Created { get; } = new();

    public IConnectionMultiplexer Open(string connectionString)
    {
        var proxy = DispatchProxy.Create<IConnectionMultiplexer, CountingMultiplexer>();
        lock (Opened)
        {
            Opened.Add(connectionString);
            Created.Add((CountingMultiplexer)(object)proxy);
        }

        return proxy;
    }
}

public sealed class RedisManagerTests
{
    private static (ServiceProvider Provider, Opener Opener, IConfigurationRoot Configuration) Build(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>
        {
            ["Redis:Default"] = "main",
            ["Redis:Stores:main:ConnectionString"] = "main-host:6379,password=secret-one",
            ["Redis:Stores:other:ConnectionString"] = "other-host:6379"
        }).Build();
        var opener = new Opener();
        var services = new ServiceCollection();
        services.AddSingleton<Func<string, IConnectionMultiplexer>>(opener.Open);
        services.AddNaravelRedis(configuration);
        return (services.BuildServiceProvider(), opener, configuration);
    }

    [Fact]
    public void A_name_opens_one_multiplexer_no_matter_how_often_it_is_asked_for()
    {
        var (provider, opener, _) = Build();
        var redis = provider.GetRequiredService<RedisManager>();

        var first = redis.Connection("main");
        var second = redis.Connection("main");

        second.Should().BeSameAs(first);
        first.Name.Should().Be("main");
        opener.Opened.Should().ContainSingle();
    }

    [Fact]
    public void Different_names_open_different_multiplexers_and_the_default_is_used_for_null()
    {
        var (provider, opener, _) = Build();
        var redis = provider.GetRequiredService<RedisManager>();

        redis.Connection("other").Multiplexer.Should().NotBeSameAs(redis.Connection("main").Multiplexer);
        redis.Connection().Name.Should().Be("main");
        provider.GetRequiredService<IRedisConnection>().Should().BeSameAs(redis.Connection("main"));
        opener.Opened.Should().HaveCount(2);
    }

    [Fact]
    public void The_same_connection_string_shares_one_multiplexer_and_never_appears_in_the_name()
    {
        var (provider, opener, _) = Build();
        var redis = provider.GetRequiredService<RedisManager>();
        const string text = "shared-host:6379,password=top-secret";

        var first = redis.FromConnectionString(text);
        var second = redis.FromConnectionString(text);
        var third = redis.FromConnectionString("another-host:6379");

        second.Should().BeSameAs(first);
        third.Should().NotBeSameAs(first);
        first.Name.Should().StartWith("implicit-").And.NotContain("top-secret").And.NotContain("shared-host");
        opener.Opened.Should().Equal(text, "another-host:6379");
    }

    [Fact]
    public void Disposing_the_provider_disposes_every_multiplexer_exactly_once()
    {
        var (provider, opener, _) = Build();
        var redis = provider.GetRequiredService<RedisManager>();
        redis.Connection("main");
        redis.Connection("other");
        redis.FromConnectionString("x:6379");

        provider.Dispose();

        opener.Created.Should().HaveCount(3).And.OnlyContain(multiplexer => multiplexer.Disposed == 1);
    }

    [Fact]
    public void An_unknown_name_fails_with_the_name_and_the_available_names()
    {
        var (provider, _, _) = Build();

        var act = () => provider.GetRequiredService<RedisManager>().Connection("nope");

        act.Should().Throw<DriverNotRegisteredException>().WithMessage("*nope*main*");
    }

    [Fact]
    public void A_store_without_a_connection_string_fails_naming_the_store_only()
    {
        var (provider, _, _) = Build(new Dictionary<string, string?> { ["Redis:Default"] = "bad", ["Redis:Stores:bad:Other"] = "x" });

        var act = () => provider.GetRequiredService<RedisManager>().Connection("bad");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'bad'*ConnectionString*");
    }

    [Fact]
    public void Extend_adds_a_connection_at_run_time()
    {
        var (provider, _, _) = Build();
        var redis = provider.GetRequiredService<RedisManager>();
        var stub = new StubConnection("late");

        redis.Extend("late", _ => stub);

        redis.Connection("late").Should().BeSameAs(stub);
    }

    [Fact]
    public void Changing_a_connection_string_opens_a_new_multiplexer_and_keeps_the_old_one_until_the_manager_is_disposed()
    {
        var (provider, opener, configuration) = Build();
        var redis = provider.GetRequiredService<RedisManager>();
        var old = redis.Connection("main");

        configuration["Redis:Stores:main:ConnectionString"] = "moved-host:6379";
        var current = redis.Connection("main");

        current.Should().NotBeSameAs(old);
        opener.Opened.Should().Equal("main-host:6379,password=secret-one", "moved-host:6379");
        opener.Created[0].Disposed.Should().Be(0);
        provider.Dispose();
        opener.Created.Should().OnlyContain(multiplexer => multiplexer.Disposed == 1);
    }

    [Fact]
    public void An_unrelated_configuration_change_keeps_the_open_multiplexers()
    {
        var (provider, opener, configuration) = Build();
        var redis = provider.GetRequiredService<RedisManager>();
        var first = redis.Connection("main");

        configuration["Unrelated:Key"] = "value";

        redis.Connection("main").Should().BeSameAs(first);
        opener.Opened.Should().ContainSingle();
    }

    [Fact]
    public void A_blank_connection_string_is_refused()
    {
        var (provider, _, _) = Build();

        ((Action)(() => provider.GetRequiredService<RedisManager>().FromConnectionString(" "))).Should().Throw<ArgumentException>();
    }

    private sealed class StubConnection(string name) : IRedisConnection
    {
        public string Name => name;

        public IConnectionMultiplexer Multiplexer => throw new NotSupportedException();
    }
}
