using System.Text;
using Naravel.Filesystem.Drivers;

namespace Naravel.Filesystem.Tests;

public sealed class LocalStorageDriverContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "naravel-fs-contract-" + Guid.NewGuid().ToString("N"));
    private readonly LocalStorageDriver _driver;

    public LocalStorageDriverContractTests() => _driver = new LocalStorageDriver(_root, "/files");

    [Fact]
    public async Task Put_get_stream_exists_size_and_url_work()
    {
        var expected = Encoding.UTF8.GetBytes("storage contract");
        await _driver.PutAsync("docs/readme.txt", expected);

        (await _driver.ExistsAsync("docs/readme.txt")).Should().BeTrue();
        (await _driver.GetAsync("docs/readme.txt")).Should().Equal(expected);
        await using var stream = await _driver.GetStreamAsync("docs/readme.txt");
        using var contents = new MemoryStream();
        await stream.CopyToAsync(contents);
        contents.ToArray().Should().Equal(expected);
        (await _driver.SizeAsync("docs/readme.txt")).Should().Be(expected.Length);
        (await _driver.GetUrlAsync("docs/readme.txt")).Should().Be("/files/docs/readme.txt");
    }

    [Fact]
    public async Task Copy_move_and_delete_work()
    {
        await _driver.PutAsync("source.txt", "payload"u8.ToArray());

        await _driver.CopyAsync("source.txt", "copies/copy.txt");
        (await _driver.ExistsAsync("copies/copy.txt")).Should().BeTrue();

        await _driver.MoveAsync("copies/copy.txt", "archive/moved.txt");
        (await _driver.ExistsAsync("copies/copy.txt")).Should().BeFalse();
        (await _driver.GetAsync("archive/moved.txt")).Should().Equal("payload"u8.ToArray());

        await _driver.DeleteAsync("archive/moved.txt");
        (await _driver.ExistsAsync("archive/moved.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Url_generation_observes_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => _driver.GetUrlAsync("file.txt", cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Local_file_operations_observe_cancellation_before_accessing_the_filesystem()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => _driver.ExistsAsync("file.txt", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}