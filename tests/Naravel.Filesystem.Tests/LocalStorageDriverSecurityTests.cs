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
