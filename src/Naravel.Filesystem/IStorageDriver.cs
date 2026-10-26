namespace Naravel.Filesystem;

/// <summary>Asynchronous file storage operations provided by a named filesystem driver.</summary>
/// <remarks><b>Laravel equivalent:</b> the filesystem adapter contract behind <c>Storage::disk()</c>.</remarks>
public interface IStorageDriver
{
    /// <summary>Determines whether a file exists at <paramref name="path"/>.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Reads the whole file at <paramref name="path"/>.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    Task<byte[]> GetAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Opens the file at <paramref name="path"/> for streaming reads; the caller disposes the stream.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Writes <paramref name="contents"/> to <paramref name="path"/>, replacing any existing file.</summary>
    Task PutAsync(string path, Stream contents, CancellationToken cancellationToken = default);
    /// <summary>Writes <paramref name="contents"/> to <paramref name="path"/>, replacing any existing file.</summary>
    Task PutAsync(string path, byte[] contents, CancellationToken cancellationToken = default);
    /// <summary>Deletes the file at <paramref name="path"/>; a missing file is not an error.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Copies a file, replacing the destination if it exists.</summary>
    /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
    Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    /// <summary>Moves a file, replacing the destination if it exists.</summary>
    /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
    Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    /// <summary>Gets the size of the file at <paramref name="path"/> in bytes.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    Task<long> SizeAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Gets a URL for an object, optionally expiring after the supplied duration.</summary>
    Task<string> GetUrlAsync(string path, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}
