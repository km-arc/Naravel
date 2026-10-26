namespace Naravel.Filesystem.Drivers;

/// <summary>Stores files under a root directory on the local disk; paths that resolve outside the root are rejected.</summary>
public class LocalStorageDriver : IStorageDriver
{
    private readonly string _rootPath;
    private readonly string _baseUrl;

    /// <summary>Initializes a new instance of the <see cref="LocalStorageDriver"/> class.</summary>
    public LocalStorageDriver(string rootPath, string baseUrl = "/storage")
    {
        _rootPath = Path.GetFullPath(rootPath);
        _baseUrl = baseUrl.TrimEnd('/');
        Directory.CreateDirectory(_rootPath);
    }

    // SECURITY (PDR-008 interim patch): the previous version combined the caller's path into
    // _rootPath without checking the result. "../../etc/passwd" (or an absolute path on Windows)
    // resolved OUTSIDE _rootPath, giving arbitrary file read/write/delete. Path.GetFullPath
    // collapses ".." segments, then we require the result to still start with _rootPath.
    private string GetFullPath(string path)
    {
        var combined = Path.Combine(_rootPath, path.Replace('\\', '/').TrimStart('/'));
        var full = Path.GetFullPath(combined);

        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar) ? _rootPath : _rootPath + Path.DirectorySeparatorChar;
        if (!full.Equals(_rootPath, StringComparison.Ordinal) && !full.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Path '{path}' resolves outside the disk's root directory and was rejected.");
        }

        return full;
    }

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
        return Task.FromResult($"{_baseUrl}/{path.Replace('\\', '/').TrimStart('/')}");
    }
}