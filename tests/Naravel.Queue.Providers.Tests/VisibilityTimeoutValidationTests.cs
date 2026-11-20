using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Database;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Redis;

namespace Naravel.Queue.Providers.Tests;

// DBQ-001 and RQR-001 (veto AV-012, owner decision AD-017): a visibility timeout of zero or less would make every reservation count as
// expired at once, so a second worker could take a job that the first one is still running. Both drivers refuse it in the constructor
// and in the registration, as the File driver does. None of these tests needs a database server or a Redis server.
public sealed class DatabaseVisibilityTimeoutTests
{
    private static readonly DbContextOptions<SqliteQueueContext> Options =
        new DbContextOptionsBuilder<SqliteQueueContext>().UseSqlite("Data Source=:memory:").Options; // nothing is opened by these tests

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]                  // also Timeout.InfiniteTimeSpan, which is -1 millisecond
    [InlineData(-300_000L)]
    public void The_constructor_rejects_a_timeout_that_is_zero_or_negative(long milliseconds)
    {
        var construct = () => new DatabaseQueueDriver<SqliteQueueContext>(
            new SqliteQueueContextFactory(Options), TimeSpan.FromMilliseconds(milliseconds));

        construct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("visibilityTimeout");
    }

    [Fact]
    public void The_constructor_still_accepts_the_smallest_positive_timeout_and_keeps_the_five_minute_default()
    {
        var factory = new SqliteQueueContextFactory(Options);

        new DatabaseQueueDriver<SqliteQueueContext>(factory, TimeSpan.FromTicks(1)).VisibilityTimeout.Should().Be(TimeSpan.FromTicks(1));
        new DatabaseQueueDriver<SqliteQueueContext>(factory).VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(5));
        new DatabaseQueueDriver<SqliteQueueContext>(factory, null).VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    private static IQueueDriver Resolve(string? visibilitySeconds)
    {
        var settings = new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "db",
            ["NaravelQueue:Stores:db:Driver"] = "database",
        };
        if (visibilitySeconds is not null) settings["NaravelQueue:Stores:db:VisibilityTimeoutSeconds"] = visibilitySeconds;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<SqliteQueueContext>>(new SqliteQueueContextFactory(Options));
        services.AddQueue(config).AddDatabaseDriver<SqliteQueueContext>(config);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<QueueManager>().Connection("db");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-300")]
    public void A_store_with_a_timeout_setting_of_zero_or_less_fails_with_a_message_that_names_the_store_and_the_setting(string configured)
    {
        var resolve = () => Resolve(configured);

        var message = resolve.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("'db'").And.Contain("VisibilityTimeoutSeconds").And.Contain($"'{configured}'");
    }

    [Fact]
    public void A_store_with_a_timeout_setting_of_NaN_fails_the_same_way()
    {
        var configured = double.NaN.ToString(); // written in the culture that the registration parses with

        var resolve = () => Resolve(configured);

        resolve.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("VisibilityTimeoutSeconds");
    }

    [Theory]
    [InlineData(null, 300)]            // omitted: the default of 300 seconds
    [InlineData("1", 1)]
    [InlineData("300", 300)]
    public void A_store_with_a_positive_or_omitted_timeout_setting_still_resolves(string? configured, int expectedSeconds)
    {
        var driver = Resolve(configured);

        driver.Should().BeOfType<DatabaseQueueDriver<SqliteQueueContext>>().Which.VisibilityTimeout.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}

public sealed class RedisVisibilityTimeoutTests
{
    // The constructor only stores the connection, and the refusals below happen before it is used, so no connection is needed.

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-300_000L)]
    public void The_constructor_rejects_a_timeout_that_is_zero_or_negative(long milliseconds)
    {
        var construct = () => new RedisQueueDriver(null!, TimeSpan.FromMilliseconds(milliseconds));

        construct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("visibilityTimeout");
    }

    [Fact]
    public void A_refusal_never_uses_or_disposes_the_connection_even_when_the_driver_would_own_it()
    {
        // A null connection would fail with a NullReferenceException if the constructor, or a Dispose, touched it.
        var construct = () => new RedisQueueDriver(null!, TimeSpan.Zero, ownsConnection: true);

        construct.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_constructor_still_accepts_the_smallest_positive_timeout()
    {
        new RedisQueueDriver(null!, TimeSpan.FromTicks(1)).VisibilityTimeout.Should().Be(TimeSpan.FromTicks(1));
    }

    private static IQueueDriver Resolve(string visibilitySeconds)
    {
        var settings = new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "jobs",
            ["NaravelQueue:Stores:jobs:Driver"] = "redis",
            // Nothing listens here: the setting must be refused before any connection is tried, or the test would fail with a connection error.
            ["NaravelQueue:Stores:jobs:ConnectionString"] = "127.0.0.1:1",
            ["NaravelQueue:Stores:jobs:VisibilityTimeoutSeconds"] = visibilitySeconds,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(config).AddRedisDriver(config);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<QueueManager>().Connection("jobs");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-300")]
    public void A_store_with_a_timeout_setting_of_zero_or_less_fails_before_any_connection_is_tried(string configured)
    {
        var resolve = () => Resolve(configured);

        var message = resolve.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("'jobs'").And.Contain("VisibilityTimeoutSeconds").And.Contain($"'{configured}'");
    }

    [Fact]
    public void A_store_with_a_timeout_setting_of_NaN_fails_the_same_way()
    {
        var configured = double.NaN.ToString(); // written in the culture that the registration parses with

        var resolve = () => Resolve(configured);

        resolve.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("VisibilityTimeoutSeconds");
    }
}
