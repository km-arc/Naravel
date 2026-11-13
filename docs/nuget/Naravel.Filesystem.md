# Naravel.Filesystem

[![NuGet](https://img.shields.io/nuget/v/Naravel.Filesystem.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Filesystem)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Filesystem.svg)](https://www.nuget.org/packages/Naravel.Filesystem)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

Named storage drivers for Naravel, built on Naravel.Foundation: local files and Amazon S3 / S3-compatible storage.

| Fact | Detail |
|---|---|
| **Install** | `dotnet add package Naravel.Filesystem` |
| **Targets** | .NET 10 |
| **Needs** | `Naravel.Foundation`, which NuGet installs with it (the S3 driver uses `AWSSDK.S3`) |
| **Drivers** | `local` (a folder) and `s3` (Amazon S3 or S3-compatible storage) |

## What you get

- **Named disks.** Configure as many `local` and `s3` stores as you need and pick one by name.
- **One API for files and S3.** The same `IStorageDriver` calls work on a local folder and on a bucket.
- **Presigned S3 URLs.** An S3 disk returns a presigned URL from `GetUrlAsync`.
- **Safe local paths.** A path cannot leave the disk root.
- **A test fake.** `StorageFake` in `Naravel.Filesystem.Testing` is an in-memory `IStorageDriver`.

## Quick start

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

## Learn more

- Filesystem: [English guide](https://github.com/km-arc/Naravel/blob/main/docs/en/filesystem.md)
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
