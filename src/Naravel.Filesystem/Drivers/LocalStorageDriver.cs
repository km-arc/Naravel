namespace Naravel.Filesystem.Drivers;

public class LocalStorageDriver : IStorageDriver
{
    private readonly string _rootPath;
    private readonly string _baseUrl;

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

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetFullPath(path)));
    }

    public async Task<byte[]> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await File.ReadAllBytesAsync(GetFullPath(path), cancellationToken);
    }

    public Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {path}");

        return Task.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true));
    }

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

    public async Task PutAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(contents);
        await PutAsync(path, stream, cancellationToken);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

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

    public Task<long> SizeAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileInfo(GetFullPath(path)).Length);
    }

    public Task<string> GetUrlAsync(string path, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult($"{_baseUrl}/{path.Replace('\\', '/').TrimStart('/')}");
    }
}