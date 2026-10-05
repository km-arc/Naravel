using Microsoft.Extensions.Options;
using Naravel.Foundation;

namespace Naravel.Filesystem;

/// <summary>Resolves named filesystem drivers through the shared Naravel manager.</summary>
/// <remarks><b>Laravel equivalent:</b> the filesystem manager's <c>disk()</c> method.</remarks>
public sealed class StorageManager(
    IServiceProvider provider,
    IDriverRegistry<IStorageDriver> registry,
    IOptionsMonitor<FilesystemOptions> options)
    : Manager<IStorageDriver, FilesystemOptions>(provider, registry, options)
{
    /// <summary>Gets the configured default disk or the named disk.</summary>
    public IStorageDriver Disk(string? name = null) => Driver(name);
}