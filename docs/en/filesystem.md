# Filesystem

`Naravel.Filesystem` provides named storage drivers through `Naravel.Foundation`. The built-in drivers are local files and Amazon S3/S3-compatible storage.

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

Options can also be configured with the `Action<FilesystemOptions>` overload. `StorageManager` is registered as a singleton; the default `IStorageDriver` is also available for direct injection. Inject `StorageManager` when choosing a disk by name.

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

Use environment variables or a secret provider for credentials; do not commit real keys. `Default` selects the default disk and defaults to `local`. Store names are case-insensitive. Built-in `Driver` values are `local` and `s3`.

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

Local URLs are not signed and do not expire. Protect access to the corresponding static-file route separately.

## Add a runtime driver

Foundation's manager supports runtime `Extend`. A custom driver can be registered after the service provider is built:

```csharp
var manager = app.Services.GetRequiredService<StorageManager>();
manager.Extend("scratch", _ => new LocalStorageDriver("storage/scratch", "/scratch"));
var scratch = manager.Disk("scratch");
```

The manager owns drivers returned by factories and disposes them when the manager is disposed. Do not return an unrelated container-owned singleton from a factory unless the manager is allowed to dispose it.

## Security and limitations

- Local paths are disk-root-relative. Attempts to escape the configured root are rejected with `UnauthorizedAccessException`; keep the regression tests when changing path handling.
- The migration to Foundation changed the configuration keys from `DefaultDisk`/`Disks` to `Default`/`Stores` and removed the old `IStorageManager` and factory APIs. Update callers during migration.
- S3 region selection, endpoint configuration and client disposal have unit coverage, but no live AWS/S3 integration test is run by this project.
- The local driver does not create a public HTTP endpoint. Configure ASP.NET Core static files or an authorized download endpoint separately.
- Filesystem operations do not provide Laravel's URL signing, visibility/policy system, or cloud-provider abstraction beyond the drivers documented here.
