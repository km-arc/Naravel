using Naravel.Queue.Drivers;
using Naravel.Queue.File;

namespace Naravel.Queue.Tests;

// The queue name is the folder name of the file driver. A name is accepted unchanged or rejected before the file system
// is touched, so no name can leave the base path and two different names can never share a folder.
public sealed class FileDriverQueueNameTests : IDisposable
{
    // The base path sits one level below a directory that only this test class uses, so a folder created outside the
    // base path shows up in that directory instead of being mistaken for something another process made.
    private readonly string _outer = Path.Combine(Path.GetTempPath(), "naravel-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _basePath;

    public FileDriverQueueNameTests() => _basePath = Path.Combine(_outer, "base");

    public void Dispose()
    {
        if (Directory.Exists(_outer)) Directory.Delete(_outer, recursive: true);
    }

    private static QueuedMessage MessageFor(string queue) => new() { Queue = queue, JobType = "x", Payload = "{}" };

    // Every operation that takes a queue name, each wrapped so that a synchronous throw also becomes a faulted task.
    private static (string Operation, Func<Task> Call)[] Operations(FileQueueDriver driver, string queue) =>
    [
        ("PushAsync", async () => await driver.PushAsync(MessageFor(queue))),
        ("PopAsync", async () => await driver.PopAsync(queue)),
        ("SizeAsync", async () => await driver.SizeAsync(queue)),
        ("AckAsync", async () => await driver.AckAsync(MessageFor(queue))),
        ("ReleaseAsync", async () => await driver.ReleaseAsync(MessageFor(queue), TimeSpan.Zero)),
        ("FailAsync", async () => await driver.FailAsync(MessageFor(queue))),
        ("TryFailAsync", async () => await driver.TryFailAsync(MessageFor(queue))),
    ];

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/rooted")]
    [InlineData("C:")]
    [InlineData("mail:high")]
    [InlineData("a b")]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData("caf\u00e9")]
    public async Task A_queue_name_outside_the_portable_set_is_rejected_by_every_operation_before_any_folder_is_made(string queue)
    {
        var driver = new FileQueueDriver(_basePath);

        foreach (var (operation, call) in Operations(driver, queue))
        {
            var error = (await call.Should().ThrowExactlyAsync<ArgumentException>($"{operation} must reject the name")).Which;
            error.ParamName.Should().Be("queue");
            error.Message.Should().StartWith($"Queue name '{queue}' is not valid for the file driver.");
        }

        Directory.GetFileSystemEntries(_basePath).Should().BeEmpty();
        Directory.GetFileSystemEntries(_outer).Should().ContainSingle().Which.Should().Be(_basePath);
    }

    [Fact]
    public async Task Control_characters_are_rejected()
    {
        var driver = new FileQueueDriver(_basePath);

        foreach (var queue in new[] { "a\u0000b", "a\nb", "a\tb", "\u202e" })
        {
            Func<Task> push = async () => await driver.PushAsync(MessageFor(queue));
            await push.Should().ThrowExactlyAsync<ArgumentException>();
        }

        Directory.GetFileSystemEntries(_basePath).Should().BeEmpty();
    }

    [Fact]
    public async Task A_null_queue_name_is_rejected_with_ArgumentNullException()
    {
        var driver = new FileQueueDriver(_basePath);

        Func<Task> pop = async () => await driver.PopAsync(null!);

        var error = (await pop.Should().ThrowExactlyAsync<ArgumentNullException>()).Which;
        error.ParamName.Should().Be("queue");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("emails")]
    [InlineData("high-priority")]
    [InlineData("orders_v2")]
    [InlineData("mail.high")]
    [InlineData("v1.2.3")]
    [InlineData("Reports")]
    [InlineData("0")]
    public async Task A_portable_queue_name_keeps_its_own_folder_name(string queue)
    {
        var driver = new FileQueueDriver(_basePath);
        var message = MessageFor(queue);

        await driver.PushAsync(message);

        Directory.EnumerateFiles(Path.Combine(_basePath, queue, "pending"), "*.json").Should().ContainSingle();
        (await driver.SizeAsync(queue)).Should().Be(1);
        (await driver.PopAsync(queue))!.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task A_name_that_the_old_mapping_collapsed_into_another_queue_no_longer_reaches_it()
    {
        var driver = new FileQueueDriver(_basePath);
        await driver.PushAsync(MessageFor("ab"));

        // "a/b" and "a:b" used to lose their separator or stream marker and read the folder of "ab".
        foreach (var alias in new[] { "a/b", "a:b" })
        {
            Func<Task> size = async () => await driver.SizeAsync(alias);
            await size.Should().ThrowExactlyAsync<ArgumentException>();
        }

        (await driver.SizeAsync("ab")).Should().Be(1);
    }
}
