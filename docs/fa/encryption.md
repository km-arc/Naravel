# رمزنگاری (Encryption)

`Naravel.Encryption` یک facade نازک با متدهای `Encrypt` / `Decrypt` روی ASP.NET Core Data Protection است. هیچ رمزنگاری‌ای اضافه نمی‌کند: تمام محافظت و تمام کلیدها متعلق به Data Protection است (OD-03، PDR-021). `Naravel.Queue.Encryption` به‌صورت اختیاری payload جاب‌های انتخاب‌شده را رمزشده ذخیره می‌کند.

## اول این را بخوانید: key ring

Data Protection کلیدها را در یک **key ring** نگه می‌دارد. هر چیزی که اینجا رمز شود فقط توسط فرایندی خوانده می‌شود که **همان key ring و همان نام برنامه** را داشته باشد.

- وقتی تولیدکنندهٔ جاب و worker آن دو برنامه یا دو instance جدا هستند، **باید key ring و نام برنامه را به‌اشتراک بگذارند**. در غیر این صورت worker نمی‌تواند رمزگشایی کند و هر جاب علامت‌دار با `DecryptException` شکست می‌خورد.
- در همهٔ برنامه‌هایی که داده به‌اشتراک می‌گذارند، `SetApplicationName(...)` را با یک مقدار یکسان صدا بزنید.
- key ring را جایی ذخیره کنید که همه بتوانند بخوانند (پوشهٔ مشترک، پایگاه داده، Redis یا blob storage) و آن را در حالت ذخیره محافظت کنید: روی بعضی میزبان‌ها Data Protection کلیدها را به‌طور پیش‌فرض بدون رمز می‌نویسد.
- Data Protection تقریباً هر ۹۰ روز یک کلید جدید می‌سازد و کلیدهای قدیمی را برای رمزگشایی داده‌های قدیمی نگه می‌دارد. تا وقتی مقدار یا جاب رمزشده ممکن است وجود داشته باشد، key ring را هرگز پاک نکنید.
- key ring جدا برای هر instance (پیش‌فرض در کانتینری با فایل‌سیستم موقت) رمزگشایی بین instanceها و بعد از هر استقرار مجدد را می‌شکند.

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("my-shop")
    .PersistKeysToFileSystem(new DirectoryInfo("/shared/keys"));   // یا EF Core، Redis، blob storage
```

## شروع سریع

```csharp
builder.Services.AddNaravelEncryption();   // فقط اگر خودتان AddDataProtection() را نزده باشید آن را صدا می‌زند

var token = encrypter.Encrypt("secret");            // Laravel: Crypt::encryptString
var text  = encrypter.Decrypt(token);               // Crypt::decryptString
var card  = encrypter.Decrypt<CardInfo>(encrypter.Encrypt(new CardInfo("Ada", 4242)));   // Crypt::encrypt / decrypt
```

## نحوهٔ استفاده

### مقدارها

`Encrypt(string)` متنی URL-safe برمی‌گرداند. `Encrypt<T>` و `Decrypt<T>` مقدار را به‌صورت JSON (`System.Text.Json`) درون متن محافظت‌شده نگه می‌دارند؛ یک overload هم `JsonTypeInfo<T>` می‌گیرد تا metadata تولیدشده با source generator کار کند. همهٔ اعضا همگام (synchronous) هستند، چون خود Data Protection همگام است.

مقداری که نتوان رمزگشایی‌اش کرد (دستکاری‌شده، نامعتبر، محافظت‌شده برای purpose دیگر، کلید گم‌شده) `DecryptException` با پیام ثابت می‌اندازد؛ پیام هرگز ورودی یا متن اصلی را ندارد. `TryDecrypt` به‌جای پرتاب، `false` برمی‌گرداند.

**Trimming و Native AOT.** `Encrypt<T>(T)` و `Decrypt<T>(string)` از JSON مبتنی بر reflection استفاده می‌کنند و برای همین `RequiresUnreferencedCode` و `RequiresDynamicCode` دارند: در پروژه‌ای که analyzerهای trim یا AOT را روشن کرده (`PublishAot`، `IsTrimmable`) هر محل فراخوانی هشدار IL2026 و IL3050 می‌گیرد. رشته‌ها (`Encrypt(string)` و `Decrypt(string)`) تحت تأثیر نیستند. برای مقدار typed در چنین پروژه‌ای، metadata یک context تولیدشده با source generator را به overloadهای `JsonTypeInfo` بدهید:

```csharp
[JsonSerializable(typeof(CardInfo))]
public partial class AppJson : JsonSerializerContext;

var token = encrypter.Encrypt(new CardInfo("Ada", 4242), AppJson.Default.CardInfo);
var card  = encrypter.Decrypt(token, AppJson.Default.CardInfo);
```

دو شکل توکن یکدیگر را می‌خوانند. [Trimming و Native AOT](trimming-aot.md) را ببینید.

### Purposeها

`encrypter.ForPurpose("billing")` یک encrypter جدا با purpose خودش برمی‌گرداند: آنچه encrypter پیش‌فرض یا purpose دیگر نوشته را نمی‌تواند بخواند. purpose پیش‌فرض `Naravel.Encryption.v1` است. نام‌های ثابت بدهید، هرگز ورودی کاربر نه.

### جاب‌های رمزشدهٔ صف (`Naravel.Queue.Encryption`)

```csharp
builder.Services.AddQueue(configuration).AddMemoryDriver(configuration);   // اول
builder.Services.AddNaravelQueueEncryption();

public sealed class ChargeCard : Job, IShouldBeEncrypted { public string CardNumber { get; set; } = ""; ... }
```

- فقط کلاس‌هایی که marker خالی `IShouldBeEncrypted` را پیاده‌سازی می‌کنند رمز می‌شوند. جاب‌های بدون marker همان قالب قبلی را دارند؛ به‌طور پیش‌فرض هیچ چیز رمز نمی‌شود.
- جاب علامت‌دار به شکل `naravel:enc:v1:` + خروجی Data Protection از payload معمولی ذخیره می‌شود، با purpose برابر `Naravel.Queue.Encryption.v1` (جدا از purpose facade، پس مقدار `IEncrypter` هرگز به‌عنوان جاب پذیرفته نمی‌شود).
- نوع جاب همچنان فقط با alias registry انتخاب می‌شود. رمزگشایی بعد از آن انجام می‌شود، پس داده‌ٔ رمزگشایی‌شده هرگز نوع را انتخاب نمی‌کند (OD-07).
- زنجیره‌ها، batchها و جاب‌های ناموفق همان رشتهٔ ذخیره‌شده را حمل می‌کنند، پس متن رمزشده را حمل می‌کنند.
- جاب علامت‌دار که رمزگشایی نشود از مسیر عادی جاب ناموفق با `DecryptException` شکست می‌خورد؛ پیام آن key ring و نام برنامهٔ مشترک را اولین چیز برای بررسی معرفی می‌کند. جاب علامت‌دار هرگز به‌صورت متن ساده فرستاده یا اجرا نمی‌شود. نسخهٔ ناشناختهٔ envelope هم شکست می‌خورد.

**مهاجرت.** جابی که قبل از علامت‌دار شدن کلاسش به‌صورت متن ساده در صف رفته، به‌طور پیش‌فرض رد می‌شود. برای اینکه جاب‌های در جریان تمام شوند، در بازهٔ استقرار `AddNaravelQueueEncryption(o => o.AcceptPlaintextForMarkedJobs = true)` را روشن کنید و بعد خاموش کنید. اگر بعداً marker را بردارید، worker همچنان پیام‌هایی را که رمزشده ذخیره شده‌اند رمزگشایی می‌کند.

### تست

`EncryptionFake` وجود ندارد: یک fake برگشت‌پذیر یک تضمین رمزنگاری را جعل می‌کرد. Data Protection خودش یک نمونهٔ تست واقعی و درون‌حافظه‌ای دارد:

```csharp
services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
services.AddNaravelEncryption();
```

## مثال کامل

یک راز نوع‌دار که برای هر tenant رمزشده ذخیره می‌شود، یک توکن دعوت که دست‌کاری را رد می‌کند و یک تست با provider درون‌حافظه‌ای Data Protection. `IIntegrationStore` دسترسی به دادهٔ خودتان است.

```csharp
using Naravel.Encryption;

public sealed record ApiCredentials(string Key, string Secret);

// Program.cs: the key ring is shared and persisted (see "Read this first").
builder.Services.AddDataProtection().SetApplicationName("my-shop").PersistKeysToFileSystem(new DirectoryInfo("/shared/keys"));
builder.Services.AddNaravelEncryption();

// 1. Store a typed secret encrypted: the database column holds only the token.
public sealed class IntegrationService(IEncrypter encrypter, IIntegrationStore store)
{
    private readonly IEncrypter _secrets = encrypter.ForPurpose("integrations");     // a fixed name, never user input

    public Task SaveAsync(int tenantId, ApiCredentials credentials, CancellationToken ct) =>
        store.SetTokenAsync(tenantId, _secrets.Encrypt(credentials), ct);              // Crypt::encrypt

    public async Task<ApiCredentials?> LoadAsync(int tenantId, CancellationToken ct)
    {
        var token = await store.GetTokenAsync(tenantId, ct);
        return token is not null && _secrets.TryDecrypt(token, out var json)           // no exception for a bad value
            ? System.Text.Json.JsonSerializer.Deserialize<ApiCredentials>(json!)
            : null;
    }
}

// 2. An opaque value for a link or a cookie. Anyone who tampers with it gets a DecryptException.
app.MapGet("/invite/{token}", (string token, IEncrypter encrypter) =>
{
    try
    {
        var email = encrypter.ForPurpose("invites").Decrypt(token);                   // Crypt::decryptString
        return Results.Ok(new { email });
    }
    catch (DecryptException)
    {
        return Results.BadRequest("invalid or expired invite");                       // the message never contains the input
    }
});

app.MapPost("/invite", (string email, IEncrypter encrypter) =>
    Results.Ok(new { token = encrypter.ForPurpose("invites").Encrypt(email) }));      // URL-safe text
```

```csharp
// 3. Test: a real in-memory Data Protection provider, no fake cryptography.
[Fact]
public void Invite_token_round_trips_and_purposes_are_isolated()
{
    var services = new ServiceCollection();
    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    services.AddNaravelEncryption();
    var encrypter = services.BuildServiceProvider().GetRequiredService<IEncrypter>();

    var token = encrypter.ForPurpose("invites").Encrypt("ada@example.com");

    encrypter.ForPurpose("invites").Decrypt(token).Should().Be("ada@example.com");
    encrypter.ForPurpose("billing").TryDecrypt(token, out _).Should().BeFalse();       // another purpose cannot read it
}
```

## مشاهده‌پذیری

`Meter` با نام `Naravel.Encryption`، شمارندهٔ `naravel.encryption.operations`، با tagهای `operation` (`encrypt`، `decrypt`) و `outcome` (`ok`، `invalid`). مقدار، token، purpose و داده‌های کلید هرگز tag نمی‌شوند.

## نحوهٔ ساخت (پیاده‌سازی)

- `Encrypter` برای هر purpose یک `IDataProtector` را می‌پیچد (`Naravel.Encryption.v1` و `.../<name>`). `Encrypt` همان `Protect` و `Decrypt` همان `Unprotect` است؛ `CryptographicException` به `DecryptException` تبدیل می‌شود.
- `AddNaravelEncryption` فقط وقتی `IDataProtectionProvider` ثبت نشده باشد Data Protection را ثبت می‌کند و `IEncrypter` را singleton ثبت می‌کند.
- `AddNaravelQueueEncryption` `IJobSerializer` ثبت‌شده را با یک decorator جایگزین می‌کند؛ بعد از `AddQueue` صدا بزنید.
- `Naravel.Encryption` به پکیج کامل `Microsoft.AspNetCore.DataProtection` ارجاع می‌دهد.

## شامل نیست (با تصمیم)

ciphertext سازگار با Laravel، تنظیم `APP_KEY`، انتخاب الگوریتم، facade ایستای `Crypt::`، محافظت زمان‌دار، `EncryptionFake` و رمز کردن همهٔ جاب‌ها به‌طور پیش‌فرض.

## نگاشت به Laravel

| Laravel | Naravel |
|---|---|
| `Crypt::encryptString($v)` / `decryptString($c)` | `encrypter.Encrypt(v)` / `Decrypt(c)` |
| `Crypt::encrypt($obj)` / `decrypt($c)` | `encrypter.Encrypt<T>(obj)` / `Decrypt<T>(c)` |
| `DecryptException` | `DecryptException` |
| جاب `ShouldBeEncrypted` | جاب `IShouldBeEncrypted` + `AddNaravelQueueEncryption()` |
| `Protect` / `Unprotect` در Data Protection | `Encrypt` / `Decrypt` |
