using Naravel.Filesystem.Drivers;

namespace Naravel.Filesystem.Tests;

/// <summary>
/// Regression tests for the path-traversal fix (PDR-008 interim patch). A disk must never let a
/// caller read, write, copy, move or delete anything outside its configured root.
/// </summary>
public class LocalStorageDriverSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "naravel-fs-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalStorageDriver _driver;

    public LocalStorageDriverSecurityTests()
    {
        _driver = new LocalStorageDriver(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("../../secret.txt")]
    [InlineData("a/../../secret.txt")]
    [InlineData("..\\secret.txt")]
    public async Task Traversal_paths_are_rejected(string maliciousPath)
    {
        var act = async () => await _driver.ExistsAsync(maliciousPath);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task A_path_resolving_outside_root_is_rejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "naravel-fs-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "should not be reachable");
        try
        {
            // The storage API treats leading slashes as root-relative keys, so use a relative path
            // that actually traverses out of the configured root on every platform.
            var pathFromRoot = Path.GetRelativePath(_root, outside);
            var act = async () => await _driver.GetAsync(pathFromRoot);
            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task A_file_symlink_to_outside_the_root_is_rejected_for_reads_and_delete()
    {
        var outside = Path.Combine(Path.GetTempPath(), "naravel-fs-symlink-file-" + Guid.NewGuid().ToString("N") + ".txt");
        var link = Path.Combine(_root, "linked-file.txt");
        await File.WriteAllTextAsync(outside, "outside-secret");
        try
        {
            File.CreateSymbolicLink(link, outside);

            var read = async () => await _driver.GetAsync("linked-file.txt");
            await read.Should().ThrowAsync<UnauthorizedAccessException>();
            var delete = () => _driver.DeleteAsync("linked-file.txt");
            await delete.Should().ThrowAsync<UnauthorizedAccessException>();

            File.ReadAllText(outside).Should().Be("outside-secret");
            File.Exists(outside).Should().BeTrue();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task A_directory_symlink_to_outside_the_root_is_rejected_for_read_write_and_url()
    {
        var outside = Path.Combine(Path.GetTempPath(), "naravel-fs-symlink-dir-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(_root, "linked-directory");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "outside-secret");
        try
        {
            Directory.CreateSymbolicLink(link, outside);

            var read = async () => await _driver.GetAsync("linked-directory/secret.txt");
            await read.Should().ThrowAsync<UnauthorizedAccessException>();
            var write = () => _driver.PutAsync("linked-directory/created.txt", "must-not-escape"u8.ToArray());
            await write.Should().ThrowAsync<UnauthorizedAccessException>();
            var url = () => _driver.GetUrlAsync("linked-directory/secret.txt");
            await url.Should().ThrowAsync<UnauthorizedAccessException>();

            File.ReadAllText(Path.Combine(outside, "secret.txt")).Should().Be("outside-secret");
            File.Exists(Path.Combine(outside, "created.txt")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Ordinary_relative_paths_still_work()
    {
        await _driver.PutAsync("reports/2026/summary.txt", "hello"u8.ToArray());

        (await _driver.ExistsAsync("reports/2026/summary.txt")).Should().BeTrue();
        var bytes = await _driver.GetAsync("reports/2026/summary.txt");
        System.Text.Encoding.UTF8.GetString(bytes).Should().Be("hello");
    }

    [Fact]
    public async Task A_leading_slash_is_still_treated_as_relative_to_root()
    {
        await _driver.PutAsync("/a/b.txt", "x"u8.ToArray());
        (await _driver.ExistsAsync("a/b.txt")).Should().BeTrue();
    }
}
