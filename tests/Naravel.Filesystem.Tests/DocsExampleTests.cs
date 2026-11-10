using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Filesystem.Drivers;
using Naravel.Filesystem.Testing;
using Naravel.Foundation;

namespace Naravel.Filesystem.Tests;

// Runs the quickstart, the archive example, the Testing examples and the messages of docs/en/filesystem.md.
// The archive example points its "archive" disk at a second local folder: S3 itself is not run here (see S3StorageDriverTests for its URLs).
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests : IDisposable
{
    private readonly List<string> _paths = [];

    // The service of the Testing example: it only needs IStorageDriver, so StorageFake can stand in.
    public sealed class AvatarService(IStorageDriver storage)
    {
        public Task SaveAsync(int userId, byte[] png, CancellationToken ct = default) =>
            storage.PutAsync($"avatars/{userId}.png", png, ct);
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "naravel-fs-docs-" + Guid.NewGuid().ToString("N"));
        _paths.Add(path);
        return path;
    }

    // The registration of the quickstart, with the settings of its JSON passed as keys.
    private static ServiceProvider Build(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => $"Filesystem:{item.Key}", item => item.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddNaravelFilesystem(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_quickstart_uploads_downloads_and_deletes_on_the_default_local_disk()
    {
        var root = NewPath();
        using var provider = Build(
            ("Default", "local"),
            ("Stores:local:Driver", "local"),
            ("Stores:local:Root", root),
            ("Stores:local:BaseUrl", "/storage"));
        var storage = provider.GetRequiredService<IStorageDriver>();
        var key = $"{Guid.NewGuid():N}/{Path.GetFileName("hello.txt")}";

        await using (var upload = new MemoryStream("hello"u8.ToArray()))
        {
            await storage.PutAsync(key, upload);
        }

        (await storage.ExistsAsync(key)).Should().BeTrue();
        await using (var download = await storage.GetStreamAsync(key))
        {
            using var reader = new StreamReader(download);
            (await reader.ReadToEndAsync()).Should().Be("hello");
        }

        (await storage.GetUrlAsync(key)).Should().Be($"/storage/{key}");
        File.Exists(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();

        await storage.DeleteAsync(key);
        await storage.DeleteAsync(key);   // deleting a missing file is a no-op
        (await storage.ExistsAsync(key)).Should().BeFalse();
        provider.GetRequiredService<StorageManager>().Disk().Should().BeSameAs(storage);
    }

    [Fact]
    public async Task The_archive_example_moves_a_file_to_another_disk_and_hands_out_its_url()
    {
        using var provider = Build(
            ("Default", "local"),
            ("Stores:local:Driver", "local"),
            ("Stores:local:Root", NewPath()),
            ("Stores:archive:Driver", "local"),
            ("Stores:archive:Root", NewPath()),
            ("Stores:archive:BaseUrl", "/archived"));
        var storage = provider.GetRequiredService<StorageManager>();
        var local = storage.Disk("local");
        var archive = storage.Disk("archive");
        const string key = "report.txt";
        await local.PutAsync(key, "q3"u8.ToArray());

        await using (var contents = await local.GetStreamAsync(key))
        {
            await archive.PutAsync(key, contents);
        }

        await local.DeleteAsync(key);
        var url = await archive.GetUrlAsync(key, TimeSpan.FromMinutes(10));   // the expiry only applies to S3

        (await local.ExistsAsync(key)).Should().BeFalse();
        (await archive.GetAsync(key)).Should().Equal("q3"u8.ToArray());
        url.Should().Be("/archived/report.txt");
    }

    [Fact]
    public async Task The_default_disk_and_the_driver_of_a_store_are_both_local_and_store_names_ignore_case()
    {
        using var provider = Build(("Stores:local:Root", NewPath()));   // no Default and no Driver
        var storage = provider.GetRequiredService<StorageManager>();

        await storage.Disk().PutAsync("a.txt", "a"u8.ToArray());

        (await storage.Disk("LOCAL").ExistsAsync("a.txt")).Should().BeTrue();
    }

    [Fact]
    public void A_disk_can_be_added_in_code_or_at_runtime_and_the_default_can_be_set_with_a_delegate()
    {
        var scratchRoot = NewPath();
        var services = new ServiceCollection();
        services.AddNaravelFilesystem(options => options.Default = "scratch");
        services.AddNaravelDriver<IStorageDriver>("scratch", _ => new LocalStorageDriver(scratchRoot, "/scratch"));
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<StorageManager>();

        provider.GetRequiredService<IStorageDriver>().Should().BeSameAs(manager.Disk("scratch"));

        var fake = new StorageFake();
        manager.Extend("fake", _ => fake);
        manager.Disk("fake").Should().BeSameAs(fake);
    }

    [Fact]
    public async Task The_testing_example_injects_the_fake_as_the_default_disk()
    {
        var services = new ServiceCollection();
        services.AddNaravelFilesystem(options => options.Default = "fake");
        var fake = new StorageFake();
        services.AddNaravelDriver<IStorageDriver>("fake", _ => fake);
        services.AddSingleton<AvatarService>();
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<AvatarService>().SaveAsync(7, [9]);

        fake.AssertExists("avatars/7.png");
    }

    [Fact]
    public async Task The_testing_example_uses_a_real_local_disk_in_a_temporary_folder()
    {
        var root = NewPath();
        var storage = new LocalStorageDriver(root, "/files");   // creates the folder

        await storage.PutAsync("a/b.txt", "hi"u8.ToArray());

        (await storage.GetUrlAsync("a/b.txt")).Should().Be("/files/a/b.txt");
        (await storage.SizeAsync("a/b.txt")).Should().Be(2);
        Directory.Exists(root).Should().BeTrue();
    }

    [Fact]
    public void Setup_errors_carry_the_messages_in_the_guide()
    {
        using var empty = Build();   // Default is "local" but no store is configured
        FluentActions.Invoking(() => empty.GetRequiredService<StorageManager>().Disk())
            .Should().Throw<DriverNotRegisteredException>()
            .WithMessage("Driver 'local' is not registered for IStorageDriver. Available drivers: (none).");

        using var one = Build(("Stores:local:Root", NewPath()));
        FluentActions.Invoking(() => one.GetRequiredService<StorageManager>().Disk("archive"))
            .Should().Throw<DriverNotRegisteredException>()
            .WithMessage("Driver 'archive' is not registered for IStorageDriver. Available drivers: local.");

        using var unknown = Build(("Stores:ftp:Driver", "ftp"));
        FluentActions.Invoking(() => unknown.GetRequiredService<StorageManager>().Disk("ftp"))
            .Should().Throw<NotSupportedException>().WithMessage("Driver [ftp] is not supported.");

        using var noKey = Build(("Stores:s3:Driver", "s3"), ("Stores:s3:Bucket", "docs"));
        FluentActions.Invoking(() => noKey.GetRequiredService<StorageManager>().Disk("s3"))
            .Should().Throw<ArgumentNullException>().WithParameterName("Key");
        using var noSecret = Build(("Stores:s3:Driver", "s3"), ("Stores:s3:Key", "k"), ("Stores:s3:Bucket", "docs"));
        FluentActions.Invoking(() => noSecret.GetRequiredService<StorageManager>().Disk("s3"))
            .Should().Throw<ArgumentNullException>().WithParameterName("Secret");
        using var noBucket = Build(("Stores:s3:Driver", "s3"), ("Stores:s3:Key", "k"), ("Stores:s3:Secret", "s"));
        FluentActions.Invoking(() => noBucket.GetRequiredService<StorageManager>().Disk("s3"))
            .Should().Throw<ArgumentNullException>().WithParameterName("Bucket");

    }

    [Fact]
    public async Task The_local_disk_rejects_a_key_that_leaves_its_root_and_a_missing_file_throws()
    {
        using var provider = Build(("Stores:local:Root", NewPath()));
        var storage = provider.GetRequiredService<IStorageDriver>();

        await FluentActions.Invoking(() => storage.ExistsAsync("../outside.txt"))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Path '../outside.txt' resolves outside the disk's root directory and was rejected.");

        await FluentActions.Invoking(() => storage.GetStreamAsync("missing.txt"))
            .Should().ThrowAsync<FileNotFoundException>().WithMessage("File not found: missing.txt");
        await FluentActions.Invoking(() => storage.GetAsync("missing.txt")).Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Invoking(() => storage.SizeAsync("missing.txt")).Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Invoking(() => storage.CopyAsync("missing.txt", "copy.txt")).Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Invoking(() => storage.MoveAsync("missing.txt", "moved.txt")).Should().ThrowAsync<FileNotFoundException>();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Invoking(() => storage.ExistsAsync("a.txt", cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task The_local_disk_rejects_a_symbolic_link_with_the_message_in_the_guide()
    {
        var root = NewPath();
        Directory.CreateDirectory(root);
        var outside = NewPath() + ".txt";
        await File.WriteAllTextAsync(outside, "outside-secret");
        File.CreateSymbolicLink(Path.Combine(root, "linked-file.txt"), outside);
        using var provider = Build(("Stores:local:Root", root));
        var storage = provider.GetRequiredService<IStorageDriver>();

        await FluentActions.Invoking(() => storage.GetAsync("linked-file.txt"))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Path 'linked-file.txt' traverses a symbolic link or reparse point and was rejected.");
    }

    [Fact]
    public async Task The_testing_example_saves_an_avatar_into_the_fake()
    {
        var storage = new StorageFake();

        await new AvatarService(storage).SaveAsync(1, [1, 2, 3]);

        storage.AssertExists("avatars/1.png").Should().Equal(new byte[] { 1, 2, 3 });
        storage.AssertCount(1);
        storage.AssertMissing("avatars/2.png");
    }

    [Fact]
    public async Task The_fake_assertions_and_exceptions_carry_the_messages_in_the_guide()
    {
        var storage = new StorageFake("/files/");
        await storage.PutAsync("avatars/1.png", new byte[] { 1 });

        FluentActions.Invoking(() => storage.AssertExists("avatars/2.png"))
            .Should().Throw<InvalidOperationException>().WithMessage("Expected file 'avatars/2.png' to exist, but it does not.");
        FluentActions.Invoking(() => storage.AssertMissing("avatars/1.png"))
            .Should().Throw<InvalidOperationException>().WithMessage("Expected file 'avatars/1.png' to be missing, but it exists.");
        FluentActions.Invoking(() => storage.AssertCount(2))
            .Should().Throw<InvalidOperationException>().WithMessage("Expected 2 file(s), but found 1.");

        await FluentActions.Invoking(() => storage.GetAsync("nope.txt"))
            .Should().ThrowAsync<FileNotFoundException>().WithMessage("File not found: nope.txt");
        await FluentActions.Invoking(() => storage.ExistsAsync("../x"))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Path '../x' resolves outside the disk's root directory and was rejected.");
        (await storage.GetUrlAsync("avatars/1.png", TimeSpan.FromMinutes(5))).Should().Be("/files/avatars/1.png");

        storage.Reset();
        storage.AssertCount(0);
    }
}
