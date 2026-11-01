using System.Diagnostics;
using Naravel.Filesystem.Drivers;
using Xunit;

namespace Naravel.Filesystem.Tests;

/// <summary>Exercises every path operation against links that point outside the configured root.</summary>
public sealed class LocalStorageDriverLinkOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "naravel-fs-link-ops-" + Guid.NewGuid().ToString("N"));
    private readonly LocalStorageDriver _driver;

    public LocalStorageDriverLinkOperationTests() => _driver = new LocalStorageDriver(_root);

    [Fact]
    public async Task File_symlink_blocks_read_write_delete_url_copy_and_move()
    {
        var outside = Path.Combine(Path.GetTempPath(), "naravel-fs-file-target-" + Guid.NewGuid().ToString("N") + ".txt");
        var link = Path.Combine(_root, "linked-file.txt");
        var source = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(outside, "outside-secret");
        await File.WriteAllTextAsync(source, "inside-source");

        try
        {
            File.CreateSymbolicLink(link, outside);

            await AssertRejected(() => _driver.GetAsync("linked-file.txt"));
            await AssertRejected(() => _driver.GetStreamAsync("linked-file.txt"));
            await AssertRejected(() => _driver.PutAsync("linked-file.txt", "overwrite"u8.ToArray()));
            await AssertRejected(() => _driver.DeleteAsync("linked-file.txt"));
            await AssertRejected(() => _driver.GetUrlAsync("linked-file.txt"));
            await AssertRejected(() => _driver.CopyAsync("linked-file.txt", "copy.txt"));
            await AssertRejected(() => _driver.MoveAsync("linked-file.txt", "moved.txt"));
            await AssertRejected(() => _driver.CopyAsync("source.txt", "linked-file.txt"));
            await AssertRejected(() => _driver.MoveAsync("source.txt", "linked-file.txt"));

            (await File.ReadAllTextAsync(outside)).Should().Be("outside-secret");
            File.Exists(Path.Combine(_root, "copy.txt")).Should().BeFalse();
            File.Exists(Path.Combine(_root, "moved.txt")).Should().BeFalse();
        }
        finally
        {
            File.Delete(link);
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Directory_symlink_blocks_read_write_delete_url_copy_and_move_below_the_link()
    {
        var outside = Path.Combine(Path.GetTempPath(), "naravel-fs-directory-target-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(_root, "linked-directory");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "outside-secret");
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "inside-source");

        try
        {
            Directory.CreateSymbolicLink(link, outside);

            await AssertRejected(() => _driver.GetAsync("linked-directory/secret.txt"));
            await AssertRejected(() => _driver.GetStreamAsync("linked-directory/secret.txt"));
            await AssertRejected(() => _driver.PutAsync("linked-directory/created.txt", "must-not-escape"u8.ToArray()));
            await AssertRejected(() => _driver.DeleteAsync("linked-directory/secret.txt"));
            await AssertRejected(() => _driver.GetUrlAsync("linked-directory/secret.txt"));
            await AssertRejected(() => _driver.CopyAsync("linked-directory/secret.txt", "copy.txt"));
            await AssertRejected(() => _driver.MoveAsync("linked-directory/secret.txt", "moved.txt"));
            await AssertRejected(() => _driver.CopyAsync("source.txt", "linked-directory/copied.txt"));
            await AssertRejected(() => _driver.MoveAsync("source.txt", "linked-directory/moved.txt"));

            (await File.ReadAllTextAsync(Path.Combine(outside, "secret.txt"))).Should().Be("outside-secret");
            File.Exists(Path.Combine(outside, "created.txt")).Should().BeFalse();
            File.Exists(Path.Combine(outside, "copied.txt")).Should().BeFalse();
            File.Exists(Path.Combine(outside, "moved.txt")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    [WindowsFact]
    public async Task Windows_junction_blocks_read_write_delete_url_copy_and_move()
    {
        var outside = Path.Combine(Path.GetTempPath(), "naravel-fs-junction-target-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(_root, "junction");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "outside-secret");
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "inside-source");

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{outside}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not start cmd.exe to create the junction.");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.Should().Be(0, $"junction creation failed: {stdout} {stderr}");

            await AssertRejected(() => _driver.GetAsync("junction/secret.txt"));
            await AssertRejected(() => _driver.GetStreamAsync("junction/secret.txt"));
            await AssertRejected(() => _driver.PutAsync("junction/created.txt", "must-not-escape"u8.ToArray()));
            await AssertRejected(() => _driver.DeleteAsync("junction/secret.txt"));
            await AssertRejected(() => _driver.GetUrlAsync("junction/secret.txt"));
            await AssertRejected(() => _driver.CopyAsync("junction/secret.txt", "copy.txt"));
            await AssertRejected(() => _driver.MoveAsync("junction/secret.txt", "moved.txt"));
            await AssertRejected(() => _driver.CopyAsync("source.txt", "junction/copied.txt"));
            await AssertRejected(() => _driver.MoveAsync("source.txt", "junction/moved.txt"));

            (await File.ReadAllTextAsync(Path.Combine(outside, "secret.txt"))).Should().Be("outside-secret");
            File.Exists(Path.Combine(outside, "created.txt")).Should().BeFalse();
            File.Exists(Path.Combine(outside, "copied.txt")).Should().BeFalse();
            File.Exists(Path.Combine(outside, "moved.txt")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static async Task AssertRejected(Func<Task> operation)
    {
        await operation.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Windows junction behavior is exercised on windows-latest CI only.";
        }
    }
}
