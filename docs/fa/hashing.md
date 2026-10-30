# هش رمز عبور

**Naravel بستهٔ هش ندارد.** .NET همان کاری را که facade با نام `Hash` در Laravel می‌کند از قبل انجام می‌دهد، پس چیزی پورت نشده ([PDR-019](../pdr/fa/PDR-019-hashing.md)، وضعیت `DECLINED`). این صفحه دستور‌العملی است که جای آن را می‌گیرد. نسخهٔ انگلیسی: [docs/en/hashing.md](../en/hashing.md).

## شروع سریع

```sh
dotnet add package Microsoft.Extensions.Identity.Core   # بدون وابستگی وب
```

```csharp
services.Configure<PasswordHasherOptions>(o => o.IterationCount = 250_000);   // بخش «هزینهٔ کار» را ببینید
services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();   // AppUser: هر class

var hash   = hasher.HashPassword(user, password);                              // Laravel: Hash::make
var result = hasher.VerifyHashedPassword(user, hash, password);                // Hash::check و Hash::needsRehash در یک فراخوانی
if (result == PasswordVerificationResult.SuccessRehashNeeded) hash = hasher.HashPassword(user, password);   // هش تازه را ذخیره کنید
```

`PasswordHasher<TUser>` و `PasswordHasherOptions` در namespace با نام `Microsoft.AspNetCore.Identity` هستند. `VerifyHashedPassword` مقدار `Failed`، `Success` یا `SuccessRehashNeeded` برمی‌گرداند. آرگومان `user` پذیرفته می‌شود ولی پیاده‌سازی آن را نمی‌خواند، پس هر class کار می‌کند.

## Laravel به .NET

| Laravel | .NET |
|---|---|
| `Hash::make($value)` | `HashPassword(user, password)` |
| `Hash::check($plain, $hashed)` | `VerifyHashedPassword(...)` برابر `Success` یا `SuccessRehashNeeded` است |
| `Hash::needsRehash($hashed)` | `SuccessRehashNeeded`. هنگام راستی‌آزمایی گزارش می‌شود که تنها لحظه‌ای است که رمز ساده برای هش دوباره در دسترس است. |
| `rounds` (bcrypt) | `PasswordHasherOptions.IterationCount` |
| `argon`، `argon2id` | در کتابخانهٔ پایهٔ .NET نیست، پس ارائه نمی‌شود |
| بررسی الگوریتم (`HASH_VERIFY`) | برای هش‌هایی که همین‌جا ساخته می‌شوند لازم نیست: هش نشانگر قالب خودش را دارد و نشانگر ناشناخته `Failed` برمی‌گرداند. مقداری که اصلاً Base64 نباشد، مثل هش bcrypt لاراول با پیشوند `$2y$`، فرق دارد: `VerifyHashedPassword` برایش `FormatException` می‌اندازد، پس اول پیشوند مقدار ذخیره‌شده را ببینید (بخش «آنچه این صفحه پوشش نمی‌دهد»). |

## هزینهٔ کار

قالب PBKDF2 با HMAC-SHA512، نمک تصادفی ۱۶ بایتی و کلید ۳۲ بایتی است و تعداد تکرار داخل خود هش ذخیره می‌شود. پیش‌فرض **۱۰۰٬۰۰۰ تکرار** است که کمتر از [راهنمای فعلی OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) برای PBKDF2-HMAC-SHA512 (۲۲۰٬۰۰۰) است. تعداد را خودتان مثل شروع سریع تنظیم کنید. آن را با اندازه‌گیری روی سخت‌افزار تولید انتخاب کنید (معمولاً چند صد میلی‌ثانیه برای هر هش).

بالا بردن تعداد در آینده امن است: هش‌های قدیمی همچنان تأیید می‌شوند، `SuccessRehashNeeded` می‌گوید هش تازه ذخیره کنید و کاربران در ورود بعدی ارتقا می‌یابند.

## مثال کامل

ثبت‌نام و ورود همراه با rehash هنگام ورود. `IUserStore` دسترسی به دادهٔ خودتان است؛ endpointها هم برای کاربر ناشناس و هم برای رمز اشتباه `401` برمی‌گردانند.

```csharp
public sealed class AppUser
{
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
public sealed record Credentials(string Email, string Password);

// Program.cs (IUserStore stands for your own data access)
builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 250_000);
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

app.MapPost("/register", async (Credentials body, IPasswordHasher<AppUser> hasher, IUserStore users, CancellationToken ct) =>
{
    var user = new AppUser { Email = body.Email };
    user.PasswordHash = hasher.HashPassword(user, body.Password);          // Hash::make
    await users.AddAsync(user, ct);
    return Results.Created($"/users/{user.Email}", null);
});

app.MapPost("/login", async (Credentials body, IPasswordHasher<AppUser> hasher, IUserStore users, CancellationToken ct) =>
{
    var user = await users.FindByEmailAsync(body.Email, ct);
    if (user is null)
        return Results.Unauthorized();                                      // same answer as a wrong password

    var result = hasher.VerifyHashedPassword(user, user.PasswordHash, body.Password);
    if (result == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    if (result == PasswordVerificationResult.SuccessRehashNeeded)           // Hash::needsRehash
    {
        user.PasswordHash = hasher.HashPassword(user, body.Password);
        await users.UpdateAsync(user, ct);
    }
    return Results.Ok();                                                    // then sign the user in (see auth.md)
});
```

## آنچه این صفحه پوشش نمی‌دهد

- **Argon2id و bcrypt.** OWASP الگوریتم Argon2id را اول می‌گذارد و PBKDF2 را وقتی ترجیح می‌دهد که FIPS-140 لازم باشد. نه Argon2id و نه bcrypt در کتابخانهٔ پایهٔ .NET نیستند؛ استفاده از آن‌ها بستهٔ شخص ثالث می‌خواهد که در این پروژه PDR جدا لازم دارد. این شامل تأیید هش‌های موجود `$2y$` لاراول بعد از مهاجرت هم هست: تا زمانی که چنین بسته‌ای تأیید نشده، هش‌های قدیمی را نگه دارید و با یک adapter کوچک خودتان هر کاربر را در ورود بعدی به قالب Identity دوباره هش کنید. adapter باید **پیش از** صدا زدن `VerifyHashedPassword` روی مقدار ذخیره‌شده شاخه بزند: هش `$2y$` Base64 نیست و دادنش به hasher مربوط به Identity به‌جای `Failed` یک `FormatException` می‌اندازد.
- **Pepper، سیاست رمز عبور و بررسی نشت.** جزو هش نیستند؛ هیچ‌کدام ارائه نمی‌شود.
- **لاگ.** هرگز رمز عبور یا هش را لاگ نکنید.
