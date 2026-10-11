using System.Text.Json;
using Naravel.Queue.Drivers;

namespace Naravel.Queue.File;

/// <summary>
/// Durable queue backed by plain JSON files on disk - no external service required, survives
/// process restarts, and can be inspected/debugged just by looking at the folder. Good fit for
/// single-machine deployments, small apps, or as a zero-dependency fallback.
///
/// Layout under the configured Path:
///   {queue}/pending/{sortKey}_{id}.json   - waiting to be picked up
///   {queue}/reserved/{sortKey}_{id}.json  - currently being processed by a worker
///   {queue}/failed/{id}.json              - exhausted all attempts
///
/// Reservation uses File.Move (pending -> reserved), which is effectively atomic on a single
/// volume, so two workers racing for the same file will not both succeed.
///
/// Fencing: each reservation stamps the message with a fresh <c>ReservationId</c> that is stored in the reserved file.
/// Ack/Release/Fail only act on a reserved file whose token still matches (a reclaim clears it), so a worker whose
/// reservation expired cannot delete or re-queue the copy another worker now owns. Messages without a token keep the
/// previous unfenced behaviour.
/// </summary>
public class FileQueueDriver : IQueueDriver
{
    private readonly string _basePath;
    private readonly TimeSpan _visibilityTimeout;
    public TimeSpan? VisibilityTimeout => _visibilityTimeout;

    public FileQueueDriver(string basePath, TimeSpan? visibilityTimeout = null)
    {
        _basePath = basePath;
        _visibilityTimeout = visibilityTimeout ?? TimeSpan.FromMinutes(5);
        Directory.CreateDirectory(_basePath);
    }

    private string Dir(string queue, string sub)
    {
        var dir = Path.Combine(_basePath, Sanitize(queue), sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Write to a ".tmp" sibling (ignored by the "*.json" scans) and rename, so a reader can never observe a
    // half-written message.
    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await System.IO.File.WriteAllTextAsync(tmp, content, cancellationToken);
        try
        {
            await RetryTransientAsync(() => System.IO.File.Move(tmp, path, overwrite: true), cancellationToken);
        }
        catch
        {
            try { System.IO.File.Delete(tmp); } catch { /* best effort: do not leave a stray .tmp behind */ }
            throw;
        }
    }

    // Windows reports a file that another worker currently has open (or is deleting) as a sharing violation
    // (IOException) or, for overwrite/delete, as UnauthorizedAccessException. Both are transient contention between
    // workers sharing the folder, so replace/delete retries briefly with a growing back-off instead of failing the job.
    private const int TransientRetries = 8;

    private static async Task RetryTransientAsync(Action action, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < TransientRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5 * (attempt + 1)), cancellationToken);
            }
        }
    }

    private static Task DeleteWithRetryAsync(string file, CancellationToken cancellationToken)
        => RetryTransientAsync(() => System.IO.File.Delete(file), cancellationToken);

    private static string Sanitize(string name) => string.Concat(name.Split(Path.GetInvalidFileNameChars()));

    // Sort key packs priority (descending) and availability time (ascending) into the filename so a
    // plain alphabetical directory listing already gives us the correct pop order.
    private static string SortKey(QueuedMessage m) => $"{9 - Math.Clamp(m.Priority, 0, 9)}_{m.AvailableAt.UtcTicks:D20}";

    private static string PendingFileName(QueuedMessage m) => $"{SortKey(m)}_{m.Id}.json";

    public async Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Dir(message.Queue, "pending"), PendingFileName(message));
        await WriteAtomicAsync(path, JsonSerializer.Serialize(message), cancellationToken);
    }

    public async Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
    {
        var pendingDir = Dir(queue, "pending");
        var reservedDir = Dir(queue, "reserved");
        var now = DateTimeOffset.UtcNow;

        await ReclaimStaleReservationsAsync(pendingDir, reservedDir, now, cancellationToken);

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(pendingDir, "*.json").OrderBy(f => f, StringComparer.Ordinal); }
        catch (DirectoryNotFoundException) { return null; }

        foreach (var file in files)
        {
            QueuedMessage message;
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
                message = JsonSerializer.Deserialize<QueuedMessage>(json)!;
            }
            catch (IOException) { continue; }        // file vanished / being written - try the next one
            catch (UnauthorizedAccessException) { continue; } // Windows: file held by another worker mid-move/delete
            catch (JsonException) { continue; }      // unreadable/corrupt file: skip it instead of crashing the worker

            if (message.AvailableAt > now) continue; // not due yet - files are sorted, but priorities can interleave availability times

            var dest = Path.Combine(reservedDir, Path.GetFileName(file));
            try
            {
                // Rename keeps the old last-write time, so refresh it first: the reservation age used by
                // ReclaimStaleReservations must start NOW, or a message that waited a long time in pending could be
                // "reclaimed" by another worker the instant it is reserved.
                System.IO.File.SetLastWriteTimeUtc(file, now.UtcDateTime);
                System.IO.File.Move(file, dest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // another worker grabbed it first (Windows may report that as access denied)
            }

            message.ReservedAt = now;
            message.ReservationId = Guid.NewGuid().ToString("N");
            await WriteAtomicAsync(dest, JsonSerializer.Serialize(message), cancellationToken);
            return message;
        }

        return null;
    }

    // A reserved file whose last write is older than the visibility timeout belongs to a worker that most likely
    // crashed: move it back to pending so it is retried instead of being stuck forever.
    private async Task ReclaimStaleReservationsAsync(
        string pendingDir,
        string reservedDir,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(reservedDir, "*.json"))
        {
            try
            {
                if (now - System.IO.File.GetLastWriteTimeUtc(file) < _visibilityTimeout) continue;
                QueuedMessage message;
                await using (var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    if (now - System.IO.File.GetLastWriteTimeUtc(file) < _visibilityTimeout) continue;
                    using (var reader = new StreamReader(stream, leaveOpen: true))
                    {
                        var json = await reader.ReadToEndAsync(cancellationToken);
                        message = JsonSerializer.Deserialize<QueuedMessage>(json)!;
                    }

                    message.Attempts++;
                    message.ReservedAt = null;
                    message.ReservationId = null;
                    var updated = JsonSerializer.SerializeToUtf8Bytes(message);
                    stream.SetLength(0);
                    stream.Position = 0;
                    await stream.WriteAsync(updated, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
                System.IO.File.Move(file, Path.Combine(pendingDir, Path.GetFileName(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* another worker reclaimed or finished it - fine */ }
        }
    }

    public async Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        var file = await FindOwnedReservedFileAsync(message, cancellationToken);
        if (file != null) await DeleteWithRetryAsync(file, cancellationToken);
    }

    public async Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var fenced = message.ReservationId is not null;
        var reservedFile = await FindOwnedReservedFileAsync(message, cancellationToken);
        if (fenced && reservedFile is null) return; // reservation was reclaimed or taken over: nothing of ours to hand back

        message.ReservedAt = null;
        message.AvailableAt = DateTimeOffset.UtcNow + delay;

        var newPath = Path.Combine(Dir(message.Queue, "pending"), PendingFileName(message));
        await WriteAtomicAsync(newPath, SerializeWithoutReservation(message), cancellationToken);
        if (reservedFile != null) await DeleteWithRetryAsync(reservedFile, cancellationToken);
    }

    public async Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        var fenced = message.ReservationId is not null;
        var reservedFile = await FindOwnedReservedFileAsync(message, cancellationToken);
        if (fenced && reservedFile is null) return; // reservation was reclaimed or taken over

        var failedPath = Path.Combine(Dir(message.Queue, "failed"), $"{message.Id}.json");
        await WriteAtomicAsync(failedPath, SerializeWithoutReservation(message), cancellationToken);
        if (reservedFile != null) await DeleteWithRetryAsync(reservedFile, cancellationToken);
    }

    public Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
    {
        try { return Task.FromResult((long)Directory.EnumerateFiles(Dir(queue, "pending"), "*.json").Count()); }
        catch (DirectoryNotFoundException) { return Task.FromResult(0L); }
    }

    private string? FindReservedFile(QueuedMessage message)
        => Directory.EnumerateFiles(Dir(message.Queue, "reserved"), $"*_{message.Id}.json").FirstOrDefault();

    // The stored envelope must not carry the old token, but the caller's object keeps it so a stale worker stays fenced.
    private static string SerializeWithoutReservation(QueuedMessage message)
    {
        var reservationId = message.ReservationId;
        message.ReservationId = null;
        try { return JsonSerializer.Serialize(message); }
        finally { message.ReservationId = reservationId; }
    }

    // Returns the reserved file only if this caller still owns the reservation. A message without a ReservationId
    // (legacy envelope) is not fenced and gets the file as before.
    private async Task<string?> FindOwnedReservedFileAsync(QueuedMessage message, CancellationToken cancellationToken)
    {
        var file = FindReservedFile(message);
        if (file is null || message.ReservationId is null) return file;

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(file, cancellationToken);
            var stored = JsonSerializer.Deserialize<QueuedMessage>(json);
            return string.Equals(stored?.ReservationId, message.ReservationId, StringComparison.Ordinal) ? file : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; } // being reclaimed or replaced right now: not ours
        catch (JsonException) { return null; }
    }
}
