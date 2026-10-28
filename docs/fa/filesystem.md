# Filesystem

`Naravel.Filesystem` درایورهای ذخیره‌سازی نام‌دار را از طریق `Naravel.Foundation` فراهم می‌کند. درایورهای داخلی شامل فایل محلی و Amazon S3/S3-compatible هستند.

## ثبت سرویس

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNaravelFilesystem(builder.Configuration);

var app = builder.Build();
app.Run();
```

بخش تنظیمات به‌صورت پیش‌فرض `Filesystem` است. برای استفاده از نام دیگری، آن را به‌عنوان آرگومان دوم بدهید:

```csharp
builder.Services.AddNaravelFilesystem(builder.Configuration, "ObjectStorage");
```

همچنین می‌توان از overload مبتنی بر `Action<FilesystemOptions>` استفاده کرد. `StorageManager` به‌صورت singleton ثبت می‌شود؛ درایور پیش‌فرض `IStorageDriver` نیز برای تزریق مستقیم در دسترس است. اگر انتخاب زمان اجرای دیسک لازم است، `StorageManager` را تزریق کنید.

## تنظیمات

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

برای اطلاعات محرمانه از environment variable یا secret provider استفاده کنید؛ کلید واقعی را commit نکنید. `Default` دیسک پیش‌فرض را انتخاب می‌کند و مقدار پیش‌فرض آن `local` است. نام storeها به بزرگی و کوچکی حروف حساس نیست. مقدارهای داخلی `Driver` برابر `local` و `s3` هستند.

مقدارهای پیش‌فرض درایور local عبارت‌اند از `Root = "wwwroot/uploads"` و `BaseUrl = "/storage"`. اگر `Endpoint` داده نشود، درایور S3 از endpoint استاندارد AWS استفاده می‌کند. برای endpoint سازگار با S3، `Region` منطقهٔ امضای درخواست و `Endpoint` نشانی سرویس را تعیین می‌کند.

## انتخاب دیسک

اگر دیسک پیش‌فرض کافی است، `IStorageDriver` را تزریق کنید. برای انتخاب زمان اجرا از `StorageManager.Disk(name)` استفاده کنید:

```csharp
using Naravel.Filesystem;

public sealed class ArchiveService(StorageManager storage)
{
    public IStorageDriver GetArchive() => storage.Disk("archive");
}
```

نام متد `Disk()` برای اصطلاحات Filesystem حفظ شده است؛ resolution، cache، باطل‌سازی هنگام reload، افزودن driver در زمان اجرا و آزادسازی منابع را Foundation انجام می‌دهد.

## Upload از controller

به‌جای اعتماد به مسیر یا نام فایل ورودی کاربر، یک storage key تولید کنید:

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

عملیات `IStorageDriver` ناهمگام هستند و `CancellationToken` می‌پذیرند. `GetUrlAsync` در درایور local نشانی زیر `BaseUrl` می‌سازد؛ پارامتر انقضا فقط برای URL امضاشدهٔ S3 کاربرد دارد.

## ساخت URL امضاشدهٔ S3

```csharp
var url = await storage.Disk("archive").GetUrlAsync(
    key,
    expiration: TimeSpan.FromMinutes(10),
    cancellationToken);
```

URLهای local امضا نمی‌شوند و انقضا ندارند. دسترسی به مسیر static-file مربوط را جداگانه محافظت کنید.

## افزودن driver در زمان اجرا

manager مربوط به Foundation از `Extend` زمان اجرا پشتیبانی می‌کند. پس از ساخته‌شدن service provider می‌توان driver سفارشی اضافه کرد:

```csharp
var manager = app.Services.GetRequiredService<StorageManager>();
manager.Extend("scratch", _ => new LocalStorageDriver("storage/scratch", "/scratch"));
var scratch = manager.Disk("scratch");
```

manager مالک driverهایی است که factory برمی‌گرداند و هنگام dispose شدن manager آن‌ها را آزاد می‌کند. از برگرداندن singleton متعلق به container استفاده نکنید، مگر اینکه manager اجازهٔ آزادسازی آن را داشته باشد.

## مثال کامل

آپلود، دانلود، حذف و آرشیو با minimal API و یک تست با `StorageFake`. `AvatarService` کد خودتان است که `IStorageDriver` می‌گیرد.

```csharp
// Program.cs: "local" is the default disk, "archive" is S3 (see Configuration).
builder.Services.AddNaravelFilesystem(builder.Configuration);

// Upload: the key is generated, never taken from the client.
app.MapPost("/documents", async (IFormFile file, IStorageDriver storage, CancellationToken ct) =>
{
    var key = $"{Guid.NewGuid():N}/{Path.GetFileName(file.FileName)}";
    await using var contents = file.OpenReadStream();
    await storage.PutAsync(key, contents, ct);
    return Results.Created($"/documents/{key}", new { key });
}).DisableAntiforgery();

// Download and delete.
app.MapGet("/documents/{*key}", async (string key, IStorageDriver storage, CancellationToken ct) =>
    await storage.ExistsAsync(key, ct)
        ? Results.File(await storage.GetStreamAsync(key, ct), "application/octet-stream", Path.GetFileName(key))
        : Results.NotFound());

app.MapDelete("/documents/{*key}", async (string key, IStorageDriver storage, CancellationToken ct) =>
{
    await storage.DeleteAsync(key, ct);                      // deleting a missing file is a no-op
    return Results.NoContent();
});

// Move a document to the archive disk and hand out a link that expires in 10 minutes.
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

```csharp
// Test: the same IStorageDriver, in memory.
public sealed class AvatarService(IStorageDriver storage)
{
    public Task SaveAsync(int userId, byte[] png, CancellationToken ct = default) =>
        storage.PutAsync($"avatars/{userId}.png", png, ct);
}

[Fact]
public async Task Save_writes_the_avatar()
{
    var storage = new StorageFake();

    await new AvatarService(storage).SaveAsync(1, [1, 2, 3]);

    storage.AssertExists("avatars/1.png");
    storage.AssertCount(1);
}
```

## Fake برای تست و قرارداد مشترک

`Naravel.Filesystem.Testing.StorageFake` یک `IStorageDriver` درون‌حافظه برای تست برنامه است. آن را جایی تزریق کنید که `IStorageDriver` انتظار می‌رود، یا با `StorageManager.Extend` به‌عنوان دیسک ثبت کنید و روی فایل‌ها assert بزنید:

```csharp
var storage = new StorageFake();
await sut.SaveAvatarAsync(storage, userId: 1);

storage.AssertExists("avatars/1.png");
storage.AssertCount(1);
```

رفتارش مثل درایور local است: فایل ناموجود `FileNotFoundException` می‌دهد، حذف فایل ناموجود بی‌اثر است، copy و move مقصد را جایگزین می‌کنند و مسیری که از ریشه خارج شود `UnauthorizedAccessException` می‌دهد. `GetUrlAsync` مقدار `{baseUrl}/{path}` را برمی‌گرداند و انقضا را نادیده می‌گیرد.

فایل `tests/Naravel.Filesystem.Tests/StorageDriverContractTests.cs` یک قرارداد دارد که درایور local، `StorageFake` و S3 هر سه اجرا می‌کنند: نوشتن و خواندن (bytes، stream، محتوای خالی و باینری)، جایگزینی، URL، copy، move، حذف idempotent و exception برای هر خواندن از فایل ناموجود. نوع exception برای فایل ناموجود بین درایورها فرق دارد و جزو قرارداد نیست. با `NARAVEL_TEST_S3=endpoint;accessKey;secretKey;bucket` قرارداد روی یک سرویس زندهٔ سازگار با S3 اجرا می‌شود (اگر bucket نباشد ساخته می‌شود)؛ بدون این متغیر xUnit آن را Skip می‌کند. AUDIT D-22 و، همراه با `CacheFake`، D-23 را می‌بندد.

## امنیت و محدودیت‌ها

- مسیرهای local نسبت به ریشهٔ دیسک هستند. تلاش برای خروج از ریشه با `UnauthorizedAccessException` رد می‌شود؛ هنگام تغییر مسیرها تست‌های regression را حفظ کنید.
- مهاجرت به Foundation کلیدهای تنظیمات را از `DefaultDisk`/`Disks` به `Default`/`Stores` تغییر داده و APIهای قدیمی `IStorageManager` و factory را حذف کرده است. هنگام مهاجرت، مصرف‌کننده‌ها را به‌روزرسانی کنید.
- انتخاب region در S3، endpoint و آزادسازی client تست واحد دارند. قرارداد مشترک storage با `NARAVEL_TEST_S3` روی سرویس زندهٔ سازگار با S3 قابل اجراست، اما CI چنین سرویسی بالا نمی‌آورد، پس به‌طور پیش‌فرض تست زندهٔ S3 اجرا نمی‌شود.
- درایور local endpoint عمومی HTTP ایجاد نمی‌کند. static files یا endpoint مجاز دانلود را جداگانه در ASP.NET Core تنظیم کنید.
- عملیات Filesystem سیستم Laravel برای امضای URL، visibility/policy یا abstraction ابری فراتر از driverهای مستندشده را ارائه نمی‌کند.
