# PDR-005 — یک کلمهٔ کانفیگ برای همه: `Stores` (نه `Connections`/`Disks`/...)

> **وضعیت پیاده‌سازی (۲۰۲۶-۱۰-۰۴):** `Naravel.Queue` در PDR-006 از `Connections` به `Stores` تغییر کرد. مهاجرت تأییدشده و راستی‌آزمایی‌شدهٔ Filesystem در PDR-008، `DefaultDisk`/`Disks` را به `Default`/`Stores` تغییر داد. جمله‌های پایین که شکل قبلی تنظیمات را توصیف می‌کنند تاریخی هستند.

وضعیت: **پذیرفته‌شده**. تأیید مالک در ۲۰۲۶ (نگاه کنید به گفتگوی ارجاع‌شده در `PROGRESS.md`).

## زمینه

`ManagerOptions` در `Naravel.Foundation` کانفیگ را از طریق یک property مشخص bind می‌کند:

```csharp
public class ManagerOptions : IManagerOptions
{
    public string Default { get; set; } = string.Empty;
    public Dictionary<string, IConfigurationSection> Stores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

binder استاندارد کانفیگ دات‌نت کلید JSON را با نام property تطبیق می‌دهد. چون `Stores` یک property مشخص و
غیرقابل‌تغییر است (نه از طریق attribute یا override هر ماژول)، **از نظر فنی هر بخش کانفیگ هر ماژول مجبور است از
کلید literal `"Stores"` استفاده کند** — راهی نیست که ماژولی آن را به `"Connections"` یا `"Disks"` تغییر دهد مگر
خود Foundation عوض شود.

لاراول برعکس، برای هر ماژول کلمهٔ متفاوتی دارد (`config/queue.php` → `connections`، `config/cache.php` →
`stores`، `config/filesystems.php` → `disks`). دو ماژول همین الان به این تصمیم نیاز دارند:

- `Naravel.Queue` فعلاً از `"Connections"` استفاده می‌کند (قبل از وجود Foundation نوشته شده).
- پورت درحال‌ورود `Naravel.Filesystem` از `"Disks"` استفاده می‌کند.
- پورت درحال‌ورود `Naravel.Cache` (از `LaravelCacheNet`) از قبل `"Stores"` دارد.

## گزینه‌های بررسی‌شده

**الف. یک کلمهٔ ثابت (`Stores`) همه‌جا.** بخش JSON هر ماژول از `"Stores"` استفاده می‌کند، فارغ از اسمی که لاراول
رویش می‌گذارد. هیچ تغییری در Foundation نمی‌خواهد. `Naravel.Queue` باید `Connections` را به `Stores` تغییر دهد
(تغییر شکننده، قبل از نسخهٔ ۱.۰ قابل‌قبول است). کمی کمتر وفادار به واژگان لاراول در فایل کانفیگ
(`Filesystem:Stores:s3` به‌جای `Filesystem:Disks:s3`)، که بخشی از آن با نگه‌داشتن نام متد `Disk()` روی
`StorageManager` جبران می‌شود.

**ب. کلمهٔ مخصوص هر ماژول از طریق نقطهٔ توسعهٔ Foundation** (مثلاً `[ConfigurationKeyName]` یا یک property مجازی).
وفادارتر به واژگان خود لاراول در هر ماژول. نیاز به تغییر `IManagerOptions`/`ManagerOptions` و تست برای این نقطهٔ
توسعهٔ جدید دارد — کار Queue، Cache و Filesystem را که آمادهٔ پیشروی‌اند عقب می‌اندازد.

## تصمیم

**گزینهٔ الف.** بخش کانفیگ هر ماژول از `"Stores"` به‌عنوان کلید مجموعه استفاده می‌کند، از طریق همان
`ManagerOptions.Stores` که از قبل تست شده. شامل:

- `Naravel.Queue`: `"Connections"` → `"Stores"` (بخشی از مهاجرت Queue روی Foundation، PDR-006).
- `Naravel.Filesystem`: در کانفیگ از `"Stores"` استفاده می‌کند؛ API عمومی در سطح *متد* واژگان لاراول را نگه
  می‌دارد (`StorageManager.Disk(name)`)، نه در کلید کانفیگ.
- `Naravel.Cache`: از قبل `"Stores"` دارد — تغییری لازم نیست.

## گزینهٔ ردشده

گزینهٔ ب کاملاً رد نشده — **به تعویق افتاده**. اگر بعد از انتشار Cache/Filesystem/Queue، واژگان مخصوص هر ماژول در
کانفیگ برای پذیرش یا وضوح مستندات مهم شد، با یک PDR جدید بازبینی می‌شود. تا آن زمان، یکپارچگی و سرعت انتشار در
سطح فایل کانفیگ اولویت دارد (سطح API زبان C# همچنان واژگان لاراول را دارد: `Disk()`، `Store()`، `Connection()`).

## پیامدها

- شکل `appsettings.json` در `Naravel.Queue` عوض می‌شود (`Connections` → `Stores`) به‌عنوان بخشی از PDR-006. هر کد
  یا مستندی که با شکل قدیمی نوشته شده باید در همان تغییر به‌روز شود.
- هر ماژول آینده (Mail، Session، Events، ...) هم از `"Stores"` استفاده می‌کند — همین‌جا مستند شد تا کسی دوباره
  سر آن بحث نکند.
