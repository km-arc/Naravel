using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Filesystem.Drivers;
using Naravel.Foundation;

namespace Naravel.Filesystem.Tests;

/// <summary>
/// The configured disks and the disks added with AddNaravelDriver or ConfigureNaravelDrivers live on one registry, as in
/// every other Naravel module. A disk registered at startup used to be silently ignored.
/// </summary>
public sealed class StorageDriverRegistrationTests : IDisposable
{
    private readonly List<string> _roots = new();

    [Fact]
    public void A_disk_added_with_AddNaravelDriver_is_resolved_by_name_and_as_the_default()
    {
        var root = NewRoot();
        var scratch = new LocalStorageDriver(root, "/scratch");
        var services = new ServiceCollection();
        services.AddNaravelFilesystem(Configuration(("Default", "scratch")));
        services.AddNaravelDriver<IStorageDriver>("scratch", _ => scratch);
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<StorageManager>();

        manager.Disk("scratch").Should().BeSameAs(scratch);
        manager.Disk().Should().BeSameAs(scratch);
        provider.GetRequiredService<IStorageDriver>().Should().BeSameAs(scratch);
    }

    [Fact]
    public async Task Configured_disks_and_registered_disks_work_side_by_side()
    {
        var configuredRoot = NewRoot();
        var registeredRoot = NewRoot();
        var services = new ServiceCollection();
        services.AddNaravelFilesystem(Configuration(("Default", "configured"),
            ("Stores:configured:Driver", "local"), ("Stores:configured:Root", configuredRoot)));
        services.ConfigureNaravelDrivers<IStorageDriver>(registry =>
            registry.Register("registered", _ => new LocalStorageDriver(registeredRoot, "/registered")));
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<StorageManager>();

        await manager.Disk().PutAsync("a.txt", "configured"u8.ToArray());
        await manager.Disk("registered").PutAsync("b.txt", "registered"u8.ToArray());

        File.Exists(Path.Combine(configuredRoot, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(registeredRoot, "b.txt")).Should().BeTrue();
        (await manager.Disk("registered").ExistsAsync("a.txt")).Should().BeFalse();
    }

    [Fact]
    public void The_later_registration_wins_when_a_registered_disk_has_the_name_of_a_configured_one()
    {
        var configuredRoot = NewRoot();
        var replacement = new LocalStorageDriver(NewRoot(), "/replacement");

        var after = new ServiceCollection();
        after.AddNaravelFilesystem(Configuration(("Default", "local"),
            ("Stores:local:Driver", "local"), ("Stores:local:Root", configuredRoot)));
        after.AddNaravelDriver<IStorageDriver>("local", _ => replacement);
        using var overridden = after.BuildServiceProvider();
        overridden.GetRequiredService<StorageManager>().Disk("local").Should().BeSameAs(replacement);

        var before = new ServiceCollection();
        before.AddNaravelDriver<IStorageDriver>("local", _ => replacement);
        before.AddNaravelFilesystem(Configuration(("Default", "local"),
            ("Stores:local:Driver", "local"), ("Stores:local:Root", configuredRoot)));
        using var configuredWins = before.BuildServiceProvider();
        configuredWins.GetRequiredService<StorageManager>().Disk("local").Should().NotBeSameAs(replacement);
    }

    private static ConfigurationManager Configuration(params (string Key, string? Value)[] values)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(values.ToDictionary(item => $"Filesystem:{item.Key}", item => item.Value));
        return config;
    }

    private string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "naravel-fs-registration-" + Guid.NewGuid().ToString("N"));
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
}
