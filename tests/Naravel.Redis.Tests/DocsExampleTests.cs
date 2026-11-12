using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Foundation;
using StackExchange.Redis;

namespace Naravel.Redis.Tests;

// Runs the "Quickstart", "Errors", "Testing" and "Complete example" sections of docs/en/redis.md without a Redis server:
// the opener hands out the counting stand-in of RedisManagerTests, so what is checked is what Naravel owns (names, strings, errors, disposal).
// The commands of the guide (StringIncrementAsync, StringGetAsync) belong to StackExchange.Redis and need a server; they are not run here.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    // Your IRedisConnection implementation of the complete example.
    public sealed class ReportsRedisConnection(IServiceProvider provider) : IRedisConnection
    {
        public IServiceProvider Provider => provider;

        public string Name => "reports";

        public IConnectionMultiplexer Multiplexer => throw new NotSupportedException();
    }

    private static IConfigurationRoot Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value)).Build();

    private static ServiceProvider Provider(IConfiguration configuration, Opener? opener = null, Func<string, IConnectionMultiplexer>? open = null)
    {
        var counting = opener ?? new Opener();
        Func<string, IConnectionMultiplexer> opening = open ?? counting.Open;
        var services = new ServiceCollection();
        services.AddSingleton(opening);
        services.AddNaravelRedis(configuration);
        return services.BuildServiceProvider();
    }

    // The helper of the "Testing" section, as written there (it uses the CountingMultiplexer of RedisManagerTests, which does what the guide's does and more).
    private static ServiceProvider Build(List<string> opened)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Redis:Default"] = "main",
            ["Redis:Stores:main:ConnectionString"] = "main-host:6379",
            ["Redis:Stores:other:ConnectionString"] = "other-host:6379"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<Func<string, IConnectionMultiplexer>>(connectionString =>
        {
            opened.Add(connectionString);
            return System.Reflection.DispatchProxy.Create<IConnectionMultiplexer, CountingMultiplexer>();
        });
        services.AddNaravelRedis(configuration);
        return services.BuildServiceProvider();
    }

    private static void Refused<T>(Action act, string param) where T : ArgumentException =>
        act.Should().ThrowExactly<T>().Which.ParamName.Should().Be(param);

    [Fact]
    public void The_quickstart_hands_out_the_default_connection_with_the_string_of_its_store()
    {
        var opener = new Opener();
        using var provider = Provider(Configuration(
            ("Redis:Default", "main"),
            ("Redis:Stores:main:ConnectionString", "localhost:6379")), opener);

        var connection = provider.GetRequiredService<RedisManager>().Connection();   // what the quickstart's endpoint asks for

        connection.Name.Should().Be("main");
        opener.Opened.Should().ContainSingle().Which.Should().Be("localhost:6379");
        connection.Multiplexer.Should().BeSameAs(opener.Created.Single());   // the multiplexer is the one the opener returned: nothing wraps it
        provider.GetRequiredService<IRedisConnection>().Should().BeSameAs(connection);
    }

    [Fact]
    public void Registering_validates_its_arguments_and_the_names_and_strings_it_is_given()
    {
        var configuration = Configuration(("Redis:Default", "main"), ("Redis:Stores:main:ConnectionString", "main-host:6379"));
        using var provider = Provider(configuration);
        var redis = provider.GetRequiredService<RedisManager>();

        Refused<ArgumentNullException>(() => ((IServiceCollection)null!).AddNaravelRedis(configuration), "services");
        Refused<ArgumentNullException>(() => new ServiceCollection().AddNaravelRedis(null!), "configuration");
        Refused<ArgumentNullException>(() => new ServiceCollection().AddNaravelRedis(configuration, null!), "sectionName");
        Refused<ArgumentException>(() => new ServiceCollection().AddNaravelRedis(configuration, ""), "sectionName");
        Refused<ArgumentException>(() => new ServiceCollection().AddNaravelRedis(configuration, " "), "sectionName");
        Refused<ArgumentNullException>(() => redis.FromConnectionString(null!), "connectionString");
        Refused<ArgumentException>(() => redis.FromConnectionString(""), "connectionString");
        Refused<ArgumentException>(() => redis.FromConnectionString(" "), "connectionString");
        foreach (var blank in new[] { "", " " })
        {
            var act = () => redis.Connection(blank);
            var error = act.Should().ThrowExactly<ArgumentException>().Which;
            error.ParamName.Should().Be("name");
            error.Message.Should().StartWith("Driver name must not be blank.");
        }
    }

    [Fact]
    public void A_missing_default_an_unknown_name_and_a_store_without_a_connection_string_give_the_documented_messages()
    {
        using var noDefault = Provider(Configuration(("Redis:Stores:main:ConnectionString", "main-host:6379")));
        var missing = () => noDefault.GetRequiredService<RedisManager>().Connection();
        missing.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Be(
            "No default driver is configured for IRedisConnection. Set the 'Default' option or pass a driver name explicitly.");

        using var provider = Provider(Configuration(
            ("Redis:Default", "main"),
            ("Redis:Stores:main:ConnectionString", "main-host:6379"),
            ("Redis:Stores:other:ConnectionString", "other-host:6379")));
        var redis = provider.GetRequiredService<RedisManager>();
        var unknown = () => redis.Connection("nope");
        unknown.Should().ThrowExactly<DriverNotRegisteredException>().Which.Message.Should().Be(
            "Driver 'nope' is not registered for IRedisConnection. Available drivers: main, other.");

        redis.FromConnectionString("secret-host:6379,password=top-secret");   // an implicit connection joins the list, under a name that hides the string
        var message = unknown.Should().ThrowExactly<DriverNotRegisteredException>().Which.Message;
        message.Should().Contain("implicit-").And.Contain("main, other").And.NotContain("top-secret").And.NotContain("secret-host");

        using var noString = Provider(Configuration(("Redis:Default", "bad"), ("Redis:Stores:bad:Other", "x")));
        var redisWithoutString = noString.GetRequiredService<RedisManager>();   // registering and resolving the manager did not fail
        var byName = () => redisWithoutString.Connection("bad");
        var byInjection = () => noString.GetRequiredService<IRedisConnection>();
        byName.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Be("Redis connection 'bad' requires ConnectionString.");
        byInjection.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Be("Redis connection 'bad' requires ConnectionString.");
    }

    [Fact]
    public void A_manager_that_was_disposed_refuses_every_call_with_its_name()
    {
        var provider = Provider(Configuration(("Redis:Default", "main"), ("Redis:Stores:main:ConnectionString", "main-host:6379")));
        var redis = provider.GetRequiredService<RedisManager>();

        provider.Dispose();

        var act = () => redis.Connection("main");
        act.Should().ThrowExactly<ObjectDisposedException>().Which.ObjectName.Should().Be("RedisManager");
    }

    [Fact]
    public void A_connection_that_failed_to_open_is_not_remembered_and_the_error_is_the_clients_own()
    {
        var attempts = 0;
        var opener = new Opener();
        using var provider = Provider(
            Configuration(("Redis:Default", "main"), ("Redis:Stores:main:ConnectionString", "main-host:6379")),
            open: connectionString => ++attempts == 1 ? throw new TimeoutException("no server") : opener.Open(connectionString));
        var redis = provider.GetRequiredService<RedisManager>();

        var first = () => redis.Connection("main");

        first.Should().ThrowExactly<TimeoutException>().WithMessage("no server");
        redis.Connection("main").Name.Should().Be("main");   // the next call tries again
        opener.Opened.Should().ContainSingle();
    }

    [Fact]
    public void Names_are_not_case_sensitive_and_a_second_AddNaravelRedis_is_ignored()
    {
        var first = Configuration(("Redis:Default", "main"), ("Redis:Stores:main:ConnectionString", "main-host:6379"));
        var second = Configuration(("Redis:Default", "late"), ("Redis:Stores:late:ConnectionString", "late-host:6379"));
        var opener = new Opener();
        var services = new ServiceCollection();
        services.AddSingleton<Func<string, IConnectionMultiplexer>>(opener.Open);
        services.AddNaravelRedis(first);

        services.AddNaravelRedis(second);   // returns at once: neither its configuration nor its section is read

        services.Count(descriptor => descriptor.ServiceType == typeof(RedisManager)).Should().Be(1);
        using var provider = services.BuildServiceProvider();
        var redis = provider.GetRequiredService<RedisManager>();
        redis.Connection("MAIN").Should().BeSameAs(redis.Connection("main"));
        redis.Connection().Name.Should().Be("main");
        ((Action)(() => redis.Connection("late"))).Should().ThrowExactly<DriverNotRegisteredException>();
        opener.Opened.Should().ContainSingle();
    }

    // The two tests of the "Testing" section, as written there.
    [Fact]
    public void A_name_opens_one_connection_with_the_string_of_its_store()
    {
        var opened = new List<string>();
        using var provider = Build(opened);
        var redis = provider.GetRequiredService<RedisManager>();

        redis.Connection("main");
        redis.Connection("main");
        redis.Connection("other");

        opened.Should().Equal("main-host:6379", "other-host:6379");
    }

    [Fact]
    public void The_multiplexer_is_disposed_once_with_the_host_and_not_by_the_borrower()
    {
        using var provider = Build(new List<string>());
        var multiplexer = provider.GetRequiredService<IRedisConnection>().Multiplexer;   // the default connection, borrowed
        var counter = (CountingMultiplexer)(object)multiplexer;

        counter.Disposed.Should().Be(0);
        provider.Dispose();

        counter.Disposed.Should().Be(1);
    }

    [Fact]
    public void The_complete_example_opens_its_second_connection_with_its_own_string_and_extends_at_run_time()
    {
        var opener = new Opener();
        using var provider = Provider(Configuration(
            ("Redis:Default", "main"),
            ("Redis:Stores:main:ConnectionString", "localhost:6379"),
            ("Redis:Stores:sessions:ConnectionString", "localhost:6379,defaultDatabase=2")), opener);
        var manager = provider.GetRequiredService<RedisManager>();

        var sessions = manager.Connection("sessions");                       // GET /sessions/{id}
        manager.Extend("reports", p => new ReportsRedisConnection(p));        // the run-time connection

        sessions.Name.Should().Be("sessions");
        opener.Opened.Should().Equal("localhost:6379,defaultDatabase=2");
        manager.Connection("reports").Should().BeOfType<ReportsRedisConnection>().Which.Provider.GetRequiredService<RedisManager>().Should().BeSameAs(manager);   // the factory gets the application's services
        manager.Connection("reports").Should().BeSameAs(manager.Connection("reports"));
    }
}
