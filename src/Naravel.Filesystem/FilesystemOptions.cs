using Naravel.Foundation;

namespace Naravel.Filesystem;

/// <summary>Configuration for named filesystem stores.</summary>
/// <remarks><b>Laravel equivalent:</b> the <c>filesystems</c> configuration's default disk and disks.</remarks>
public sealed class FilesystemOptions : ManagerOptions
{
    /// <summary>The default configuration section name.</summary>
    public const string Position = "Filesystem";

    /// <summary>Creates options with the same local default disk as the previous Filesystem API.</summary>
    public FilesystemOptions() => Default = "local";
}
