# PDR-006 — مهاجرت Naravel.Queue روی Naravel.Foundation

وضعیت: **پذیرفته‌شده و پیاده‌سازی‌شده.** تأیید اصولی مالک قبلاً گرفته شده (گفتگوی ارجاع‌شده در
`PROGRESS.md`)؛ این PDR آنچه واقعاً ساخته شده را ثبت می‌کند، طبق قانون ۱ در `AGENTS.md` («PDR اول» —
همراه با پیاده‌سازی نوشته شد نه دقیقاً قبل از آن، چون مالک جهت کار را قبلاً تأیید کرده و خواسته بود
مرحله‌به‌مرحله پیش برود، نگاه کنید به بخش «روش کار» در `PROGRESS.md`).

## زمینه

`Naravel.Queue` قبل از وجود `Naravel.Foundation` ساخته شده بود. `IQueueManager`/`QueueManager`/
`IQueueDriverFactory` دستی خودش را داشت که دقیقاً همان چیزی را تکرار می‌کرد که
`Manager<TDriver, TOptions>` + `IDriverRegistry<TDriver>` در Foundation از قبل حل کرده‌اند (ساخت
یک‌بارهٔ درایور، `Extend` در زمان اجرا، hot reload کانفیگ، dispose) — نقض مستقیم قانون ۲ در `AGENTS.md`
(«هرگز resolve/cache/Extend درایور را دوباره پیاده نکن»). کانفیگش هم از `"Connections"` به‌عنوان کلمهٔ
مجموعهٔ storeها استفاده می‌کرد که PDR-005 آن را با یک کلمهٔ مشترک `"Stores"` برای همهٔ ماژول‌ها جایگزین کرد.

## تصمیم

مهاجرت `Naravel.Queue` روی `Manager<IQueueDriver, QueueOptions>` / `IDriverRegistry<IQueueDriver>`، با
این جزئیات:

۱. **`QueueManager : Manager<IQueueDriver, QueueOptions>`**، sealed، فقط یک عضو اضافه می‌کند:
   `Connection(name)` به‌عنوان نام مستعار `Driver(name)` — لاراول این متد را روی مدیر صف خودش
   «connection» می‌نامد، و PDR-005 این واژگان را در سطح API زبان C# نگه می‌دارد، حتی اگر کلمهٔ کانفیگ
   `"Stores"` باشد.
۲. **`QueueOptions : ManagerOptions`** — زیرکلاس عمداً خالی (هنوز تنظیم سطح‌بالای مخصوص Queue وجود
   ندارد؛ نگه‌داشتن این نوع یک نقطهٔ توسعهٔ مستند می‌دهد به‌جای این‌که ماژول‌ها نوع لفظی `ManagerOptions`
   را به اشتراک بگذارند).
۳. **`IQueueDriverFactory` کاملاً حذف شد.** هر پکیج درایور (`Naravel.Queue.Memory`، `.File`، `.Redis`،
   `.Database`، `.RabbitMQ`، `.Kafka`) حالا `AddXxxDriver(IServiceCollection, IConfiguration, string
   sectionName = "NaravelQueue")` را نشان می‌دهد، با یک helper مشترک جدید:
   `QueueDriverRegistrationExtensions.AddQueueDriver(services, configuration, driverName, factory,
   sectionName)`. این یک‌بار، در زمان ثبت، `{sectionName}:Stores` را برای هر store که کلید `"Driver"`
   خودش با `driverName` مطابقت دارد (case-insensitive) اسکن می‌کند، و برای هر نام store مطابق، یک
   factory در `IDriverRegistry<IQueueDriver>` از طریق `AddNaravelDriver<IQueueDriver>(name, factory)`
   ثبت می‌کند.

   **این دقیقاً مکانیزمی است که به سؤال «نام store در برابر نوع درایور» جواب می‌دهد** که هنگام طراحی
   PDR-005 مطرح شد: خود Foundation نیازی به تغییر نداشت — این لایهٔ واسط کاملاً داخل helper ثبت هر پکیج
   درایور زندگی می‌کند، که از قبل دسترسی زودهنگام به `IConfigurationSection` خام در زمان startup دارد
   (قبل از ساخته‌شدن DI container)، برخلاف closure یک factory درایور (که فقط `IServiceProvider` را در
   زمان ساخت می‌گیرد، نه یک نام).

۴. **resolve کردن درایور در Worker به داخل حلقهٔ poll منتقل شد.** قبلاً `QueueWorkerService` درایور
   کانکشن خودش را یک‌بار، قبل از حلقهٔ `while`، resolve می‌کرد و همان reference را برای همیشه نگه
   می‌داشت (بدتر: manager دستی قدیمی اصلاً درایورهای کش‌شده را هرگز evict نمی‌کرد، پس این خیلی مهم نبود
   — ولی یعنی تغییر connection string بدون ری‌استارت هرگز اعمال نمی‌شد). حالا
   `_manager.Connection(_options.Connection)` در ابتدای هر دور حلقه فراخوانی می‌شود. چون
   `Manager.Driver()` به‌جز وقتی واقعاً نیاز به rebuild باشد فقط یک lookup ارزان از کش است، این نگرانی
   کارایی ایجاد نمی‌کند، و یعنی reload کانفیگ (PDR-004) حالا بدون ری‌استارت worker اعمال می‌شود. نمونهٔ
   resolve‌شده همچنان برای *کل* چرخهٔ pop→handle→ack/release/fail یک پیام استفاده می‌شود و هرگز وسط
   پردازش یک پیام دوباره resolve نمی‌شود: delivery tagهای RabbitMQ و offsetهای consumer در Kafka به
   نمونهٔ مشخص channel/consumer که پیام را pop کرده محدودند، پس عوض‌کردن نمونه وسط پردازش یک پیام
   ack کردن را می‌شکند.
۵. **شکل کانفیگ**: `"NaravelQueue:Connections:name"` → `"NaravelQueue:Stores:name"` (PDR-005).

## گزینه‌های ردشده

- **نگه‌داشتن `IQueueDriverFactory` و فقط delegate کردنش به `IDriverRegistry` داخلی.** رد شد: این باز
  هم سطح عمومی Foundation را بدون هیچ فایده‌ای تکرار می‌کرد — هر ماژول باید همان الگوی extension را نشان
  دهد (`AddXxxDriver(services, configuration, ...)` که factoryهای per-store ثبت می‌کند)، نه یک الگوی
  موازی فقط برای Queue.
- **دادن overload به `IDriverRegistry<TDriver>.Register` که نام/section store را هم به factory
  بدهد.** یک تغییر در Foundation می‌بود که تست جدید آنجا می‌خواست، و کار Queue، Cache و Filesystem را
  عقب می‌انداخت. روش اسکن در زمان ثبت (بند ۳ بالا) همان نتیجه را با صفر تغییر در Foundation می‌دهد، پس
  این گزینه غیرضروری تشخیص داده شد و کنار گذاشته شد.

## پیامدها

- تغییر شکننده (قبل از نسخهٔ ۱.۰، تأییدشده): `AddMemoryDriver()`, `AddFileDriver()` و غیره حالا یک
  پارامتر `IConfiguration` می‌خواهند. کلید `"Connections"` در `appsettings.json` باید `"Stores"` شود.
  اینترفیس `IQueueManager` حذف شد؛ کدی که مستقیم به آن ارجاع می‌داد باید از کلاس مشخص `QueueManager`
  استفاده کند (یا `IQueueDriver` برای کد سطح درایور).
- قابلیت‌های جدیدی که Queue «رایگان» از Foundation می‌گیرد و manager دستی قدیمی هرگز نداشت: `Extend`
  در زمان اجرا، hot reload کانفیگ با rebuild فقط-وقتی-واقعاً-تغییر-کرده مبتنی بر fingerprint، و dispose
  درست درایورهایی که `IDisposable`/`IAsyncDisposable` پیاده کرده‌اند وقتی خود manager dispose می‌شود.
- تست‌های جدید مخصوص همین مهاجرت اضافه شد (`tests/Naravel.Queue.Tests/ManagerIntegrationTests.cs`): دو
  store با یک نوع درایور از هم ایزوله می‌مانند، `Extend` در زمان اجرا، reload کانفیگ که یک store را
  تغییر می‌دهد درایورش را می‌سازد، و reload بی‌ربط این کار را نمی‌کند.
- دو باگ پنهان و بی‌ربط قبلی هنگام کار روی پروژه‌های تست پیدا و رفع شد: `Naravel.Queue.Tests.csproj` و
  `Naravel.Foundation.Tests.csproj` از `ConfigurationBuilder.AddInMemoryCollection(...)` بدون رفرنس به
  پکیج `Microsoft.Extensions.Configuration.Memory` (که این extension method در آن است) استفاده
  می‌کردند — به هر دو پروژه و به `Directory.Packages.props` اضافه شد.
