# Filesystem

`Naravel.Filesystem` provides named storage drivers through `Naravel.Foundation`. The built-in drivers are local files and Amazon S3/S3-compatible storage.

## Quickstart

A minimal API that uploads, downloads and deletes files on a local disk. The disk is a folder, so nothing needs to be installed or started.

```sh
dotnet add package Naravel.Filesystem
```

```json
{ "Filesystem": { "Default": "local", "Stores": { "local": { "Driver": "local", "Root": "storage/uploads", "BaseUrl": "/storage" } } } }
```

```csharp
using Naravel.Filesystem;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelFilesystem(builder.Configuration);
var app = builder.Build();

// Upload: the key is generated, never taken from the client.
app.MapPost("/documents", async (IFormFile file, IStorageDriver storage, CancellationToken ct) =>
{
    var key = $"{Guid.NewGuid():N}/{Path.GetFileName(file.FileName)}";
    await using var contents = file.OpenReadStream();
    await storage.PutAsync(key, contents, ct);
    return Results.Created($"/documents/{key}", new { key });
}).DisableAntiforgery();

// Download: this key comes from the URL, so a key that leaves the disk's root is answered like a missing file.
app.MapGet("/documents/{*key}", async (string key, IStorageDriver storage, CancellationToken ct) =>
{
    try
    {
        return await storage.ExistsAsync(key, ct)
            ? Results.File(await storage.GetStreamAsync(key, ct), "application/octet-stream", Path.GetFileName(key))
            : Results.NotFound();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.NotFound();
    }
});

app.MapDelete("/documents/{*key}", async (string key, IStorageDriver storage, CancellationToken ct) =>
{
    try
    {
        await storage.DeleteAsync(key, ct);   // deleting a missing file is a no-op
        return Results.NoContent();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.NotFound();
    }
});

app.Run();
```

Try it, with `$URL` set to the address `dotnet run` prints:

```sh
echo hello > hello.txt
curl -F "file=@hello.txt" "$URL/documents"      # {"key":"<32 hex digits>/hello.txt"}
curl "$URL/documents/<key>"                     # hello
curl -X DELETE "$URL/documents/<key>"           # 204 No Content
```

The file is written to `storage/uploads/<32 hex digits>/hello.txt` under the working directory. `Default` is `local` unless you set it, but a disk exists only when it is in `Stores` (or added in code): with no `local` store the first use throws (see [Errors](#errors)).

## Register

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelFilesystem(builder.Configuration);

var app = builder.Build();
app.Run();
```

The configuration section defaults to `Filesystem`. To use a different section name, pass it as the second argument:

```csharp
builder.Services.AddNaravelFilesystem(builder.Configuration, "ObjectStorage");
```

Options can also be configured with the `Action<FilesystemOptions>` overload. `FilesystemOptions.Stores` holds configuration sections, so the delegate is for `Default`; disks come from configuration or from the code registrations below. `StorageManager` is registered as a singleton; the default `IStorageDriver` is also available for direct injection. Inject `StorageManager` when choosing a disk by name.

## Configuration

```json
{
  "Filesystem": {
    "Default": "local",
    "Stores": {
      "local": {
        "Driver": "local",
        "Root": "storage/uploads",
        "BaseUrl": "/storage"
      },
      "archive": {
        "Driver": "s3",
        "Key": "${S3_ACCESS_KEY}",
        "Secret": "${S3_SECRET_KEY}",
        "Bucket": "documents",
        "Region": "us-east-1"
      },
      "minio": {
        "Driver": "s3",
        "Key": "${S3_ACCESS_KEY}",
        "Secret": "${S3_SECRET_KEY}",
        "Bucket": "documents",
        "Endpoint": "http://localhost:9000",
        "Region": "us-east-1"
      }
    }
  }
}
```

Use environment variables or a secret provider for credentials; do not commit real keys. `Default` selects the default disk and defaults to `local`. Store names are case-insensitive. Built-in `Driver` values are `local` and `s3` (compared without regard to case), and a store without `Driver` is `local`.

The local driver defaults to `Root = "wwwroot/uploads"` and `BaseUrl = "/storage"`. S3 uses the standard AWS endpoint when `Endpoint` is omitted. For a custom S3-compatible endpoint, `Region` configures the signing region while `Endpoint` selects the service URL.

## Select a disk

Inject `IStorageDriver` when the default disk is enough. Use `StorageManager.Disk(name)` for runtime selection:

```csharp
using Naravel.Filesystem;

public sealed class ArchiveService(StorageManager storage)
{
    public IStorageDriver GetArchive() => storage.Disk("archive");
}
```

The `Disk()` method name is retained for filesystem terminology; driver resolution, caching, reload invalidation, runtime extension and disposal come from Foundation.

A file can move between disks. This endpoint copies a document from `local` to `archive` (S3 in the Configuration above), deletes it from `local` and returns a link that expires in 10 minutes:

```csharp
app.MapPost("/archive/{*key}", async (string key, StorageManager storage, CancellationToken ct) =>
{
    var local = storage.Disk("local");
    var archive = storage.Disk("archive");

    await using (var contents = await local.GetStreamAsync(key, ct))
        await archive.PutAsync(key, contents, ct);
    await local.DeleteAsync(key, ct);

    var url = await archive.GetUrlAsync(key, TimeSpan.FromMinutes(10), ct);   // presigned: the expiry only applies to S3
    return Results.Ok(new { url });
});
```

## Upload from a controller

Generate a storage key instead of trusting a client-provided path or filename:

```csharp
using Microsoft.AspNetCore.Mvc;
using Naravel.Filesystem;

[ApiController]
[Route("documents")]
public sealed class DocumentsController(IStorageDriver storage) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        var key = $"{Guid.NewGuid():N}/{Path.GetFileName(file.FileName)}";
        await using var contents = file.OpenReadStream();
        await storage.PutAsync(key, contents, cancellationToken);
        return Ok(new { key });
    }
}
```

`IStorageDriver` operations are asynchronous and accept a `CancellationToken`. The local driver's `GetUrlAsync` returns a URL under `BaseUrl`; the expiration argument has meaning only for S3 presigned URLs.

## Generate an S3 presigned URL

```csharp
var url = await storage.Disk("archive").GetUrlAsync(
    key,
    expiration: TimeSpan.FromMinutes(10),
    cancellationToken);
```

Without an `expiration` the S3 driver returns a plain URL, `{endpoint}/{bucket}/{key}` (`https://s3.amazonaws.com` when no `Endpoint` is configured). It carries no signature, so an object that is not public will not open through it. Local URLs are not signed and do not expire. Protect access to the corresponding static-file route separately.

## Add a disk at startup

Disks that need code, not configuration, are registered like the built-in drivers of every other Naravel module. They share one registry with the configured disks, so the same name resolves through `StorageManager.Disk(name)`, and a disk named in `Default` also becomes the injected `IStorageDriver`:

```csharp
builder.Services.AddNaravelFilesystem(builder.Configuration);
builder.Services.AddNaravelDriver<IStorageDriver>("scratch", _ => new LocalStorageDriver("storage/scratch", "/scratch"));
```

`ConfigureNaravelDrivers<IStorageDriver>(registry => ...)` registers several at once. Registrations run in the order they were added, and the later one wins when two use one name: add a disk after `AddNaravelFilesystem` to replace a configured disk of the same name. Registered disks are created when first used, like configured ones; who disposes them is described in the ownership note below.

## Add a runtime driver

Foundation's manager supports runtime `Extend`. A custom driver can be registered after the service provider is built:

```csharp
var manager = app.Services.GetRequiredService<StorageManager>();
manager.Extend("scratch", _ => new LocalStorageDriver("storage/scratch", "/scratch"));
var scratch = manager.Disk("scratch");
```

The manager owns named and otherwise manager-created drivers. When the default `IStorageDriver` is resolved directly from DI, the container owns and disposes that exact instance; the manager skips it, including if a configuration reload later retires it. If the direct service is never resolved, the manager disposes the default itself. Do not return an unrelated container-owned singleton from a driver factory.

## Errors

The messages below are the ones in the source. A bad store is found when its disk is first used, not when the host starts. Keys that come from a request are untrusted: the Quickstart answers the `UnauthorizedAccessException` of a key that leaves the root like a missing file.

**Setup.**

| When | Exception and message |
|---|---|
| The default disk is asked for and no store has its name (`Default` is `local` unless set; with no `Stores` at all) | `DriverNotRegisteredException` (derives from `InvalidOperationException`, namespace `Naravel.Foundation`): `Driver 'local' is not registered for IStorageDriver. Available drivers: (none).` The list holds the configured names, or `(none)`. |
| `Disk("archive")` and no store of that name | `DriverNotRegisteredException`: `Driver 'archive' is not registered for IStorageDriver. Available drivers: local.` |
| A store whose `Driver` is neither `local` nor `s3` | `NotSupportedException`: `Driver [ftp] is not supported.` |
| An `s3` store without `Key`, `Secret` or `Bucket` | `ArgumentNullException`, parameter `Key`, `Secret` or `Bucket` (checked in that order) |

**Using a disk** (the local driver and `StorageFake`; S3 throws what the AWS SDK throws).

| When | Exception and message |
|---|---|
| A key that leaves the disk's root (`../x`, `a/../../x`, and the same with backslashes) | `UnauthorizedAccessException`: `Path '../outside.txt' resolves outside the disk's root directory and was rejected.` |
| Local disk: a key through a symbolic link, junction or other reparse point below the root | `UnauthorizedAccessException`: `Path 'linked-file.txt' traverses a symbolic link or reparse point and was rejected.` |
| `GetStreamAsync` of a missing file | `FileNotFoundException`: `File not found: missing.txt` |
| `GetAsync`, `SizeAsync`, `CopyAsync` or `MoveAsync` of a missing file | The local disk: `FileNotFoundException` with the .NET message, which names the full path on disk. `StorageFake`: `FileNotFoundException`: `File not found: nope.txt`. |
| A token that is already cancelled | `OperationCanceledException`, before anything is read or written |

`StorageFake` assertions are `InvalidOperationException`: `Expected file 'avatars/2.png' to exist, but it does not.`, `Expected file 'avatars/1.png' to be missing, but it exists.` and `Expected 2 file(s), but found 1.`

The exception for a missing file differs between drivers and is not part of the shared contract (see Testing). `tests/Naravel.Filesystem.Tests/DocsExampleTests.cs` runs the Quickstart's calls, the archive example (with a second local folder in place of S3) and every message above except the .NET message of the local disk, which is not copied here. Change one, change the other.

## Testing

`Naravel.Filesystem.Testing.StorageFake` is an in-memory `IStorageDriver` for application tests. Inject it where `IStorageDriver` is expected, then assert on the files. `AvatarService` is your own class that takes an `IStorageDriver`:

```csharp
using Naravel.Filesystem;
using Naravel.Filesystem.Testing;

public sealed class AvatarService(IStorageDriver storage)
{
    public Task SaveAsync(int userId, byte[] png, CancellationToken ct = default) =>
        storage.PutAsync($"avatars/{userId}.png", png, ct);
}

// In a test:
var storage = new StorageFake();

await new AvatarService(storage).SaveAsync(1, [1, 2, 3]);

storage.AssertExists("avatars/1.png");   // returns the bytes
storage.AssertCount(1);
storage.AssertMissing("avatars/2.png");
```

To test through the container, register the fake as the default disk:

```csharp
var services = new ServiceCollection();
services.AddNaravelFilesystem(options => options.Default = "fake");
var fake = new StorageFake();
services.AddNaravelDriver<IStorageDriver>("fake", _ => fake);
// provider.GetRequiredService<IStorageDriver>() is now the fake; so is StorageManager.Disk("fake").
```

`Files` returns a copy of everything stored, and `Reset()` empties the fake. To exercise the real local driver, give it a temporary folder, which it creates, and delete the folder afterwards:

```csharp
using Naravel.Filesystem.Drivers;

var root = Path.Combine(Path.GetTempPath(), "my-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var storage = new LocalStorageDriver(root, "/files");
    await storage.PutAsync("a/b.txt", "hi"u8.ToArray());
    // await storage.GetUrlAsync("a/b.txt") is "/files/a/b.txt", and await storage.SizeAsync("a/b.txt") is 2.
}
finally
{
    Directory.Delete(root, recursive: true);
}
```

`StorageFake` behaves like the local driver: a missing file throws `FileNotFoundException`, deleting a missing file is a no-op, copy and move replace the destination, and a path that leaves the root throws `UnauthorizedAccessException`. `GetUrlAsync` returns `{baseUrl}/{path}` and ignores the expiry.

`tests/Naravel.Filesystem.Tests/StorageDriverContractTests.cs` holds one contract that the local driver, `StorageFake` and S3 all run: put and read back (bytes, stream, empty and binary content), replace, URL, copy, move, idempotent delete, and an exception for every read of a missing file. The kind of exception for a missing file differs between drivers and is not part of the contract. Set `NARAVEL_TEST_S3=endpoint;accessKey;secretKey;bucket` to run it against a live S3-compatible service (the bucket is created when absent); without the variable xUnit skips it. Closes AUDIT D-22 and, with `CacheFake`, D-23.

## Security and limitations

- Local paths are disk-root-relative. Lexical traversal and existing symbolic links, junctions or other reparse points below the root are rejected with `UnauthorizedAccessException`. The root itself is trusted configuration. Validation and file opening are separate OS operations, so an attacker who can concurrently mutate the directory tree may still race the check; keep the root writable only by the application/trusted administrators.
- The migration to Foundation changed the configuration keys from `DefaultDisk`/`Disks` to `Default`/`Stores` and removed the old `IStorageManager` and factory APIs. Update callers during migration.
- S3 region selection, endpoint configuration and client disposal have unit coverage. CI runs the shared storage contract against Adobe S3Mock 5.2.3 through `NARAVEL_TEST_S3`. This covers the tested S3-compatible operations, but does not establish real AWS endpoint parity or presigned-URL signature verification.
- The local driver does not create a public HTTP endpoint. Configure ASP.NET Core static files or an authorized download endpoint separately.
- Filesystem operations do not provide Laravel's URL signing, visibility/policy system, or cloud-provider abstraction beyond the drivers documented here.
