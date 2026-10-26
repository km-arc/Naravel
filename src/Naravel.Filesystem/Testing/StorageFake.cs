using System.Collections.Concurrent;

namespace Naravel.Filesystem.Testing;

/// <summary>
/// In-memory <see cref="IStorageDriver"/> for application tests, with assertions on what is stored.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Storage::fake()</c>.</para>
/// <para><b>Why it exists:</b> inject it where <see cref="IStorageDriver"/> is expected (or register it as a disk with <c>StorageManager.Extend</c>) and assert on the files, with no disk and no S3.</para>
/// <para><b>Behaviour:</b> it follows the local driver: a missing file throws <see cref="FileNotFoundException"/>, deleting a missing file is a no-op, copy and move overwrite the destination, and a path that leaves the root throws <see cref="UnauthorizedAccessException"/>. <c>GetUrlAsync</c> returns <c>{baseUrl}/{path}</c> and ignores the expiry.</para>
/// <para><b>Not ported on purpose:</b> global fake state; the fake is an ordinary object you pass in.</para>
/// </remarks>
public sealed class StorageFake : IStorageDriver
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly string _baseUrl;

    /// <summary>Creates an empty fake disk.</summary>
    /// <param name="baseUrl">The prefix <see cref="GetUrlAsync"/> puts before a path.</param>
    public StorageFake(string baseUrl = "/storage")
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>A snapshot of the stored files: normalized path to contents.</summary>
    public IReadOnlyDictionary<string, byte[]> Files =>
        _files.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone(), StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_files.ContainsKey(Normalize(path)));
    }

    /// <inheritdoc />
    public Task<byte[]> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((byte[])Read(path).Clone());
    }

    /// <inheritdoc />
    public Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new MemoryStream((byte[])Read(path).Clone(), writable: false));
    }

    /// <inheritdoc />
    public async Task PutAsync(string path, Stream contents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(contents);
        var key = Normalize(path);
        using var buffer = new MemoryStream();
        await contents.CopyToAsync(buffer, cancellationToken);
        _files[key] = buffer.ToArray();
    }

    /// <inheritdoc />
    public Task PutAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(contents);
        _files[Normalize(path)] = (byte[])contents.Clone();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _files.TryRemove(Normalize(path), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contents = Read(sourcePath);
        _files[Normalize(destinationPath)] = (byte[])contents.Clone();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = Normalize(sourcePath);
        var destination = Normalize(destinationPath);
        var contents = Read(sourcePath);
        _files[destination] = contents;
        if (!string.Equals(source, destination, StringComparison.Ordinal)) _files.TryRemove(source, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> SizeAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((long)Read(path).Length);
    }

    /// <inheritdoc />
    public Task<string> GetUrlAsync(string path, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult($"{_baseUrl}/{Normalize(path)}");
    }

    /// <summary>Asserts that a file exists at <paramref name="path"/> and returns its contents.</summary>
    /// <param name="path">The file path.</param>
    /// <exception cref="InvalidOperationException">There is no such file.</exception>
    public byte[] AssertExists(string path) =>
        _files.TryGetValue(Normalize(path), out var contents)
            ? (byte[])contents.Clone()
            : throw new InvalidOperationException($"Expected file '{path}' to exist, but it does not.");

    /// <summary>Asserts that no file exists at <paramref name="path"/>.</summary>
    /// <param name="path">The file path.</param>
    /// <exception cref="InvalidOperationException">The file exists.</exception>
    public void AssertMissing(string path)
    {
        if (_files.ContainsKey(Normalize(path)))
        {
            throw new InvalidOperationException($"Expected file '{path}' to be missing, but it exists.");
        }
    }

    /// <summary>Asserts that exactly <paramref name="expectedCount"/> files are stored.</summary>
    /// <param name="expectedCount">The expected number of files.</param>
    /// <exception cref="InvalidOperationException">The count differs.</exception>
    public void AssertCount(int expectedCount)
    {
        var count = _files.Count;
        if (count != expectedCount)
        {
            throw new InvalidOperationException($"Expected {expectedCount} file(s), but found {count}.");
        }
    }

    /// <summary>Forgets every stored file.</summary>
    public void Reset() => _files.Clear();

    private byte[] Read(string path) =>
        _files.TryGetValue(Normalize(path), out var contents)
            ? contents
            : throw new FileNotFoundException($"File not found: {path}");

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var parts = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (parts.Count == 0)
                {
                    throw new UnauthorizedAccessException(
                        $"Path '{path}' resolves outside the disk's root directory and was rejected.");
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(segment);
        }

        if (parts.Count == 0) throw new ArgumentException("The path does not name a file.", nameof(path));
        return string.Join('/', parts);
    }
}
