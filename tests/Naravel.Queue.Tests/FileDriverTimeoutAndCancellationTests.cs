using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.File;

namespace Naravel.Queue.Tests;

// FILE-004: a visibility timeout of zero or less would make every reservation expire at once, so a second worker could take a job
// that the first one is still running. The driver and the configuration refuse it before anything is created on disk.
public sealed class FileDriverVisibilityTimeoutTests : IDisposable
{
    private readonly string _outer = Path.Combine(Path.GetTempPath(), "naravel-tests-" + Guid.NewGuid().ToString("N"));
    private string BasePath => Path.Combine(_outer, "base");

    public void Dispose()
    {
        if (Directory.Exists(_outer)) Directory.Delete(_outer, recursive: true);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]                  // also Timeout.InfiniteTimeSpan, which is -1 millisecond
    [InlineData(-300_000L)]
    public void The_constructor_rejects_a_timeout_that_is_zero_or_negative_and_creates_nothing(long milliseconds)
    {
        var construct = () => new FileQueueDriver(BasePath, TimeSpan.FromMilliseconds(milliseconds));

        construct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("visibilityTimeout");
        Directory.Exists(BasePath).Should().BeFalse("the check runs before the base directory is created");
    }

    [Fact]
    public void The_constructor_still_accepts_the_smallest_positive_timeout_and_keeps_the_five_minute_default()
    {
        new FileQueueDriver(BasePath, TimeSpan.FromTicks(1)).VisibilityTimeout.Should().Be(TimeSpan.FromTicks(1));
        new FileQueueDriver(BasePath, TimeSpan.FromMilliseconds(1)).VisibilityTimeout.Should().Be(TimeSpan.FromMilliseconds(1));
        new FileQueueDriver(BasePath).VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(5));
        new FileQueueDriver(BasePath, null).VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    private IQueueDriver Resolve(string? visibilitySeconds)
    {
        var settings = new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "disk",
            ["NaravelQueue:Stores:disk:Driver"] = "file",
            ["NaravelQueue:Stores:disk:Path"] = BasePath,
        };
        if (visibilitySeconds is not null) settings["NaravelQueue:Stores:disk:VisibilityTimeoutSeconds"] = visibilitySeconds;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(config).AddFileDriver(config);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<QueueManager>().Connection("disk");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-300")]
    public void A_store_with_a_timeout_setting_of_zero_or_less_fails_with_a_message_that_names_the_store_and_the_setting(string configured)
    {
        var resolve = () => Resolve(configured);

        var message = resolve.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("'disk'").And.Contain("VisibilityTimeoutSeconds").And.Contain($"'{configured}'");
        Directory.Exists(BasePath).Should().BeFalse("the setting is refused before the driver creates its folder");
    }

    [Fact]
    public void A_store_with_a_timeout_setting_of_NaN_fails_the_same_way()
    {
        var configured = double.NaN.ToString(); // written in the culture that the registration parses with

        var resolve = () => Resolve(configured);

        resolve.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("VisibilityTimeoutSeconds");
        Directory.Exists(BasePath).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, 300)]            // omitted: the default of 300 seconds
    [InlineData("1", 1)]
    [InlineData("300", 300)]
    public void A_store_with_a_positive_or_omitted_timeout_setting_still_resolves(string? configured, int expectedSeconds)
    {
        var driver = Resolve(configured);

        driver.Should().BeOfType<FileQueueDriver>().Which.VisibilityTimeout.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}

// FILE-006: SizeAsync lists a folder synchronously. It used to ignore its token; now a token that is already cancelled returns a
// cancelled task before any folder is made or read, and a token cancelled while the listing runs stops it after the file being read.
// Not covered by a test: cancellation in the middle of a listing, which needs a very large folder and a second thread to be
// reproducible, so only the check in the loop (read from the code) stands behind that part.
public sealed class FileDriverSizeCancellationTests : IDisposable
{
    private readonly string _outer = Path.Combine(Path.GetTempPath(), "naravel-tests-" + Guid.NewGuid().ToString("N"));
    private string BasePath => Path.Combine(_outer, "base");

    public void Dispose()
    {
        if (Directory.Exists(_outer)) Directory.Delete(_outer, recursive: true);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_returns_a_cancelled_task_and_touches_no_folder()
    {
        var driver = new FileQueueDriver(BasePath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var size = driver.SizeAsync("emails", cts.Token);

        size.IsCanceled.Should().BeTrue();
        Func<Task> awaitIt = async () => await size;
        await awaitIt.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(BasePath, "emails")).Should().BeFalse("no folder is made for a call that was cancelled before it began");
    }

    [Fact]
    public async Task A_token_that_is_not_cancelled_gets_the_exact_size()
    {
        var driver = new FileQueueDriver(BasePath);
        for (var i = 0; i < 3; i++)
            await driver.PushAsync(new QueuedMessage { Queue = "emails", JobType = "x", Payload = "{}" });
        using var cts = new CancellationTokenSource();

        (await driver.SizeAsync("emails", cts.Token)).Should().Be(3);
        (await driver.SizeAsync("emails")).Should().Be(3);
        (await driver.SizeAsync("empty", cts.Token)).Should().Be(0);
    }
}
