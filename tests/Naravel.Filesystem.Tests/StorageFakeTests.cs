using System.Text;
using Naravel.Filesystem.Testing;

namespace Naravel.Filesystem.Tests;

public sealed class StorageFakeTests
{
    [Fact]
    public async Task Assertions_report_what_is_stored()
    {
        var fake = new StorageFake();
        fake.AssertCount(0);

        await fake.PutAsync("avatars/1.png", "png"u8.ToArray());

        fake.AssertExists("avatars/1.png").Should().Equal("png"u8.ToArray());
        fake.AssertCount(1);
        fake.Files.Keys.Should().Equal("avatars/1.png");
        fake.Invoking(f => f.AssertMissing("avatars/1.png")).Should().Throw<InvalidOperationException>();
        fake.Invoking(f => f.AssertExists("avatars/2.png")).Should().Throw<InvalidOperationException>();
        fake.Invoking(f => f.AssertCount(2)).Should().Throw<InvalidOperationException>();
        fake.AssertMissing("avatars/2.png");
    }

    [Fact]
    public async Task Paths_are_normalized_like_the_local_disk()
    {
        var fake = new StorageFake("/files/");
        await fake.PutAsync(@"\docs\.\a\..\readme.txt", "x"u8.ToArray());

        (await fake.ExistsAsync("docs/readme.txt")).Should().BeTrue();
        (await fake.ExistsAsync("/docs/readme.txt")).Should().BeTrue();
        (await fake.GetUrlAsync("/docs/readme.txt", TimeSpan.FromMinutes(1))).Should().Be("/files/docs/readme.txt");
    }

    [Fact]
    public async Task A_path_outside_the_root_is_rejected()
    {
        var fake = new StorageFake();

        await FluentActions.Awaiting(() => fake.PutAsync("../escape.txt", "x"u8.ToArray()))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => fake.GetAsync("a/../../escape.txt"))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task A_missing_file_throws_FileNotFoundException()
    {
        var fake = new StorageFake();

        await FluentActions.Awaiting(() => fake.GetAsync("nope.txt")).Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Awaiting(() => fake.GetStreamAsync("nope.txt")).Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Awaiting(() => fake.SizeAsync("nope.txt")).Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Returned_and_stored_bytes_are_copies()
    {
        var fake = new StorageFake();
        var input = Encoding.UTF8.GetBytes("abc");
        await fake.PutAsync("a.txt", input);
        input[0] = (byte)'z';

        var read = await fake.GetAsync("a.txt");
        read[0] = (byte)'y';

        (await fake.GetAsync("a.txt")).Should().Equal(Encoding.UTF8.GetBytes("abc"));
    }

    [Fact]
    public async Task Operations_observe_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var fake = new StorageFake();

        await FluentActions.Awaiting(() => fake.ExistsAsync("a.txt", cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => fake.PutAsync("a.txt", "x"u8.ToArray(), cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        fake.AssertCount(0);
    }

    [Fact]
    public async Task Reset_forgets_every_file()
    {
        var fake = new StorageFake();
        await fake.PutAsync("a.txt", "x"u8.ToArray());

        fake.Reset();

        fake.AssertCount(0);
    }
}
