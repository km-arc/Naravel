# Naravel.Filesystem

Named storage drivers for Naravel, built on Naravel.Foundation: local files and Amazon S3 / S3-compatible storage.

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.

## Install

```bash
dotnet add package Naravel.Filesystem
```

Targets .NET 10. It depends on `Naravel.Foundation`, which NuGet installs with it.

## Use

```json
{
  "Filesystem": {
    "Default": "local",
    "Stores": {
      "local": { "Driver": "local", "Root": "wwwroot/uploads", "BaseUrl": "/storage" },
      "archive": { "Driver": "s3", "Bucket": "my-archive", "Region": "eu-west-1" }
    }
  }
}
```

```csharp
using Naravel.Filesystem;

builder.Services.AddNaravelFilesystem(builder.Configuration);

app.MapPost("/uploads", async (HttpRequest request, IStorageDriver storage, CancellationToken ct) =>
{
    var path = $"reports/{Guid.NewGuid():N}.bin";   // a name you choose, not one taken from the request
    await storage.PutAsync(path, request.Body, ct);
    return Results.Ok(await storage.GetUrlAsync(path, cancellationToken: ct));
});
```

The disk names (`local`, `archive`) and the bucket are yours. `IStorageDriver` is the default disk; inject `StorageManager` and call `Disk("archive")` for another. An S3 disk requires `Bucket`, `Key` and `Secret` (a missing one throws `ArgumentNullException` when the disk is first used); `Region` and `Endpoint` are optional. Keep `Key` and `Secret` out of the file and set them in the environment, for example `Filesystem__Stores__archive__Key` and `Filesystem__Stores__archive__Secret`.

For the local driver `GetUrlAsync` returns a URL under `BaseUrl`, unsigned and without an expiry; the `expiration` argument matters only for S3 presigned URLs. The local driver creates no HTTP endpoint, so serve the files with ASP.NET Core static files or an authorized download endpoint of your own. A path that leaves `Root`, through `..` or a symbolic link, is rejected with `UnauthorizedAccessException`.

## Documentation

- Filesystem: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/filesystem.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license
