using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Foundation;

namespace Naravel.Filesystem.Tests;

public sealed class StorageManagerIntegrationTests : IDisposable
{
    private readonly List<string> _roots = new();

    [Fact]
    public async Task Named_local_stores_are_isolated_and_default_disk_is_selected()
    {
        var firstRoot = NewRoot();
        var secondRoot = NewRoot();
        var config = Configuration(("Default", "first"),
            ("Stores:first:Driver", "local"), ("Stores:first:Root", firstRoot),
            ("Stores:second:Driver", "local"), ("Stores:second:Root", secondRoot));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<StorageManager>();

        manager.Disk().Should().BeSameAs(manager.Disk("first"));
        manager.Disk("first").Should().NotBeSameAs(manager.Disk("second"));
        await manager.Disk().PutAsync("shared.txt", "first"u8.ToArray());

        (await manager.Disk("second").ExistsAsync("shared.txt")).Should().BeFalse();
    }

    [Fact]
    public void Runtime_extend_registers_a_custom_disk()
    {
        var config = Configuration(("Default", "custom"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<StorageManager>();
        var custom = new TrackingDriver();

        manager.Extend("custom", _ => custom);

        manager.Disk().Should().BeSameAs(custom);
    }

    [Fact]
    public async Task Store_configuration_reload_rebuilds_only_when_stores_change()
    {
        var root = NewRoot();
        var changedRoot = NewRoot();
        var config = Configuration(("Default", "local"),
            ("Stores:local:Driver", "local"), ("Stores:local:Root", root));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<StorageManager>();
        var original = manager.Disk();

        config["Filesystem:Unrelated"] = "ignored";
        ((IConfigurationRoot)config).Reload();
        manager.Disk().Should().BeSameAs(original);

        config["Filesystem:Stores:local:Root"] = changedRoot;
        ((IConfigurationRoot)config).Reload();
        manager.Disk().Should().NotBeSameAs(original);
        await manager.Disk().PutAsync("reload.txt", "new root"u8.ToArray());
        File.Exists(Path.Combine(changedRoot, "reload.txt")).Should().BeTrue();
    }

    [Fact]
    public void Manager_disposes_custom_drivers_it_owns()
    {
        var config = Configuration(("Default", "custom"));
        var provider = Build(config);
        var manager = provider.GetRequiredService<StorageManager>();
        var custom = new TrackingDriver();
        manager.Extend("custom", _ => custom);
        manager.Disk();

        provider.Dispose();

        custom.IsDisposed.Should().BeTrue();
    }

    private ServiceProvider Build(ConfigurationManager config)
    {
        var services = new ServiceCollection();
        services.AddNaravelFilesystem(config);
        return services.BuildServiceProvider();
    }

    private ConfigurationManager Configuration(params (string Key, string? Value)[] values)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(values.ToDictionary(
            item => $"Filesystem:{item.Key}", item => item.Value));
        return config;
    }

    private string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "naravel-fs-manager-" + Guid.NewGuid().ToString("N"));
        _roots.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TrackingDriver : IStorageDriver, IDisposable
    {
        public bool IsDisposed { get; private set; }
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<byte[]> GetAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());
        public Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(Stream.Null);
        public Task PutAsync(string path, Stream contents, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PutAsync(string path, byte[] contents, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> SizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<string> GetUrlAsync(string path, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.FromResult(path);
        public void Dispose() => IsDisposed = true;
    }
}