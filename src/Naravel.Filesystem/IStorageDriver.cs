namespace Naravel.Filesystem;

/// <summary>Asynchronous file storage operations provided by a named filesystem driver.</summary>
/// <remarks><b>Laravel equivalent:</b> the filesystem adapter contract behind <c>Storage::disk()</c>.</remarks>
public interface IStorageDriver
{
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<byte[]> GetAsync(string path, CancellationToken cancellationToken = default);
    Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default);
    Task PutAsync(string path, Stream contents, CancellationToken cancellationToken = default);
    Task PutAsync(string path, byte[] contents, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    Task<long> SizeAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Gets a URL for an object, optionally expiring after the supplied duration.</summary>
    Task<string> GetUrlAsync(string path, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}
