using System.Collections.Concurrent;
using System.Text.Json;
using Naravel.Queue.Drivers;
using Naravel.Queue.Serialization;

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
/// The queue name is the folder name, so it must be portable: ASCII letters and digits, '_' and '-', with '.' allowed only
/// between two other characters (for example <c>emails</c>, <c>high-priority</c>, <c>mail.high</c>). Any other name is
/// rejected with an <see cref="ArgumentException"/> before the file system is touched; a name is never altered, so no
/// name turns into another name and no name can leave the configured Path (the names <c>.</c> and <c>..</c>,
/// path separators, drive or stream markers such as ':' and control characters are all outside that set).
/// The Windows device names (<c>con</c>, <c>prn</c>, <c>aux</c>, <c>nul</c>, <c>com0</c> to <c>com9</c>, <c>lpt0</c> to <c>lpt9</c>), in any letter case and
/// with or without an extension such as <c>nul.high</c>, are rejected on every host, so a name that works on Linux also works on Windows.
/// Two names that differ only by letter case still share one folder on a file system that ignores case; that is documented, not prevented.
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
    /// <summary>Gets how long a reserved job stays invisible before another worker may reclaim it.</summary>
    public TimeSpan? VisibilityTimeout => _visibilityTimeout;

    /// <summary>Creates a driver that keeps its queues as files under <paramref name="basePath"/> (created if missing).</summary>
    /// <param name="basePath">The directory that holds the queue folders.</param>
    /// <param name="visibilityTimeout">The reservation timeout; five minutes when omitted.</param>
    public FileQueueDriver(string basePath, TimeSpan? visibilityTimeout = null)
    {
        _basePath = basePath;
        _visibilityTimeout = visibilityTimeout ?? TimeSpan.FromMinutes(5);
        Directory.CreateDirectory(_basePath);
    }

    private string Dir(string queue, string sub)
    {
        var dir = Path.Combine(_basePath, QueueFolder(queue), sub);
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

    // The folder of a queue is its name, unchanged, or the call fails: stripping characters would let "a/b" and "ab" share
    // a folder, and ".." would still point above the base path.
    private static string QueueFolder(string queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (IsPortableQueueName(queue)) return queue;
        throw new ArgumentException(
            $"Queue name '{queue}' is not valid for the file driver. Use ASCII letters, digits, '_' and '-', and '.' only between two other characters, and not a Windows device name such as con, nul, aux, com1 or lpt1.",
            nameof(queue));
    }

    private static bool IsPortableQueueName(string name)
    {
        if (name.Length == 0 || name[0] == '.' || name[^1] == '.') return false;
        var previousWasDot = false;
        foreach (var c in name)
        {
            var isDot = c == '.';
            if (isDot && previousWasDot) return false;
            if (!isDot && !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-') return false;
            previousWasDot = isDot;
        }
        return !IsWindowsDeviceName(name);
    }

    // Windows treats these as devices and not as folders, in any letter case and also when an extension follows ("nul.high" is "nul"), so a queue with such a
    // name would work on Linux and macOS and fail or misbehave on Windows. The rule is the same on every host, so that what works in development works in
    // production. Microsoft lists COM1-COM9 and LPT1-LPT9; COM0 and LPT0 are rejected as well because some Windows versions reserve them. The superscript
    // digits that Windows also reserves are not ASCII and are already outside the grammar.
    private static bool IsWindowsDeviceName(string name)
    {
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name.AsSpan() : name.AsSpan(0, dot);
        if (stem.Length == 3)
            return stem.Equals("con", StringComparison.OrdinalIgnoreCase) || stem.Equals("prn", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("aux", StringComparison.OrdinalIgnoreCase) || stem.Equals("nul", StringComparison.OrdinalIgnoreCase);
        return stem.Length == 4 && char.IsAsciiDigit(stem[3])
            && (stem[..3].Equals("com", StringComparison.OrdinalIgnoreCase) || stem[..3].Equals("lpt", StringComparison.OrdinalIgnoreCase));
    }

    // Sort key packs priority (descending) and availability time (ascending) into the filename so a
    // plain alphabetical directory listing already gives us the correct pop order.
    private static string SortKey(QueuedMessage m) => $"{9 - Math.Clamp(m.Priority, 0, 9)}_{m.AvailableAt.UtcTicks:D20}";

    private static string PendingFileName(QueuedMessage m) => $"{SortKey(m)}_{m.Id}.json";

    /// <inheritdoc />
    public async Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Dir(message.Queue, "pending"), PendingFileName(message));
        await WriteAtomicAsync(path, QueueEnvelopeJson.Serialize(message), cancellationToken);
    }

    // Workers inside one process serialize their reserve/reclaim/settle steps per queue folder. Windows reports file moves,
    // deletes and timestamp reads that overlap with another worker's as sharing violations, access-denied or a missing
    // file (GetLastWriteTimeUtc then returns year 1601, which looks like an expired reservation), and CI showed that this
    // delivered messages more than once. The lock makes the in-process case exact; the file moves below still arbitrate
    // between separate processes.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> QueueGates = new(StringComparer.OrdinalIgnoreCase);

    private SemaphoreSlim GateFor(string queue)
        => QueueGates.GetOrAdd(Path.GetFullPath(Path.Combine(_basePath, QueueFolder(queue))), _ => new SemaphoreSlim(1, 1));

    private async Task<T> WithGateAsync<T>(string queue, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var gate = GateFor(queue);
        await gate.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
        => WithGateAsync(queue, () => PopCoreAsync(queue, cancellationToken), cancellationToken);

    private async Task<QueuedMessage?> PopCoreAsync(string queue, CancellationToken cancellationToken)
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
                message = QueueEnvelopeJson.Deserialize(json)!;
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
            await WriteAtomicAsync(dest, QueueEnvelopeJson.Serialize(message), cancellationToken);
            return message;
        }

        return null;
    }

    // File.GetLastWriteTimeUtc returns 1601-01-01 instead of throwing when the file is missing or momentarily
    // unreadable (another worker is replacing/deleting it). That must never look like an expired reservation.
    private bool IsExpired(string file, DateTimeOffset now)
    {
        var lastWrite = System.IO.File.GetLastWriteTimeUtc(file);
        return lastWrite.Year > 1601 && now - lastWrite >= _visibilityTimeout;
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
                if (!IsExpired(file, now)) continue;
                QueuedMessage message;
                await using (var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    if (!IsExpired(file, now)) continue;
                    using (var reader = new StreamReader(stream, leaveOpen: true))
                    {
                        var json = await reader.ReadToEndAsync(cancellationToken);
                        message = QueueEnvelopeJson.Deserialize(json)!;
                    }

                    message.Attempts++;
                    message.ReservedAt = null;
                    message.ReservationId = null;
                    var updated = QueueEnvelopeJson.SerializeToUtf8Bytes(message);
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

    /// <inheritdoc />
    public Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default)
        => WithGateAsync(message.Queue, async () => { await AckCoreAsync(message, cancellationToken); return 0; }, cancellationToken);

    private async Task AckCoreAsync(QueuedMessage message, CancellationToken cancellationToken)
    {
        var file = await FindOwnedReservedFileAsync(message, cancellationToken);
        if (file != null) await DeleteWithRetryAsync(file, cancellationToken);
    }

    /// <inheritdoc />
    public Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
        => WithGateAsync(message.Queue, async () => { await ReleaseCoreAsync(message, delay, cancellationToken); return 0; }, cancellationToken);

    private async Task ReleaseCoreAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken)
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

    /// <inheritdoc />
    public async Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
        => await TryFailAsync(message, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryFailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
        => WithGateAsync(message.Queue, () => FailCoreAsync(message, cancellationToken), cancellationToken);

    private async Task<bool> FailCoreAsync(QueuedMessage message, CancellationToken cancellationToken)
    {
        var fenced = message.ReservationId is not null;
        var reservedFile = await FindOwnedReservedFileAsync(message, cancellationToken);
        if (fenced && reservedFile is null) return false; // reservation was reclaimed or taken over

        var failedPath = Path.Combine(Dir(message.Queue, "failed"), $"{message.Id}.json");
        await WriteAtomicAsync(failedPath, SerializeWithoutReservation(message), cancellationToken);
        if (reservedFile != null) await DeleteWithRetryAsync(reservedFile, cancellationToken);
        return true;
    }

    /// <inheritdoc />
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
        try { return QueueEnvelopeJson.Serialize(message); }
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
            var stored = QueueEnvelopeJson.Deserialize(json);
            return string.Equals(stored?.ReservationId, message.ReservationId, StringComparison.Ordinal) ? file : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; } // being reclaimed or replaced right now: not ours
        catch (JsonException) { return null; }
    }
}
