namespace Naravel.Filesystem.Drivers;

/// <summary>Stores files under a root directory on the local disk; paths that resolve outside the root are rejected.</summary>
public class LocalStorageDriver : IStorageDriver
{
    private readonly string _rootPath;
    private readonly string _baseUrl;

    /// <summary>Creates a driver rooted at the given directory (created if missing) that builds public URLs under the given base URL.</summary>
    public LocalStorageDriver(string rootPath, string baseUrl = "/storage")
    {
        _rootPath = Path.GetFullPath(rootPath);
        _baseUrl = baseUrl.TrimEnd('/');
        Directory.CreateDirectory(_rootPath);
    }

    // SECURITY: normalize first to reject lexical traversal, then inspect every existing component below
    // the configured root. A symlink, junction, or other reparse point could otherwise redirect an apparently
    // contained key outside the root when the file API follows it.
    private string GetFullPath(string path)
    {
        var combined = Path.Combine(_rootPath, path.Replace('\\', '/').TrimStart('/'));
        var full = Path.GetFullPath(combined);

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = Path.EndsInDirectorySeparator(_rootPath) ? _rootPath : _rootPath + Path.DirectorySeparatorChar;
        if (!full.Equals(_rootPath, comparison) && !full.StartsWith(rootWithSeparator, comparison))
        {
            throw new UnauthorizedAccessException(
                $"Path '{path}' resolves outside the disk's root directory and was rejected.");
        }

        RejectReparsePointComponents(full, path);
        return full;
    }

    private void RejectReparsePointComponents(string fullPath, string requestedPath)
    {
        var relative = Path.GetRelativePath(_rootPath, fullPath);
        if (relative == ".")
        {
            return;
        }

        var current = _rootPath;
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);

            // LinkTarget detects symbolic links even when their target is currently missing. ReparsePoint also
            // covers Windows junctions and other reparse-point types that are not exposed as symbolic links.
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
            {
                throw ReparsePointRejected(requestedPath);
            }

            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw ReparsePointRejected(requestedPath);
                }
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
        }
    }

    private static UnauthorizedAccessException ReparsePointRejected(string path) =>
        new($"Path '{path}' traverses a symbolic link or reparse point and was rejected.");

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetFullPath(path)));
    }

    /// <inheritdoc />
    public async Task<byte[]> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await File.ReadAllBytesAsync(GetFullPath(path), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {path}");

        return Task.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true));
    }

    /// <inheritdoc />
    public async Task PutAsync(string path, Stream contents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        fullPath = GetFullPath(path);
        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await contents.CopyToAsync(fileStream, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PutAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(contents);
        await PutAsync(path, stream, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var src = GetFullPath(sourcePath);
        var dest = GetFullPath(destinationPath);
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        src = GetFullPath(sourcePath);
        dest = GetFullPath(destinationPath);
        File.Copy(src, dest, overwrite: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var src = GetFullPath(sourcePath);
        var dest = GetFullPath(destinationPath);
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        src = GetFullPath(sourcePath);
        dest = GetFullPath(destinationPath);
        File.Move(src, dest, overwrite: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> SizeAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileInfo(GetFullPath(path)).Length);
    }

    /// <inheritdoc />
    public Task<string> GetUrlAsync(string path, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var relative = Path.GetRelativePath(_rootPath, GetFullPath(path));
        return Task.FromResult($"{_baseUrl}/{relative.Replace(Path.DirectorySeparatorChar, '/')}");
    }
}