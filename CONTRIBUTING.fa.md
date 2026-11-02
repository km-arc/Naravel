# مشارکت در Naravel

ممنون که کمک می‌کنید. Naravel *ایده‌های* لاراول را به .NET می‌آورد و کپی خط‌به‌خط آن نیست؛ پس بیشتر کار این است که تصمیم بگیریم
چه چیزی ارزش آوردن دارد و بعد آن را کوچک و درست بسازیم. این راهنما مسیر کوتاه است؛ قواعد الزام‌آور در [AGENTS.md](AGENTS.md) و
[ROADMAP.md](ROADMAP.md) هستند. انگلیسی: [CONTRIBUTING.md](CONTRIBUTING.md).

## چطور کمک کنم

| چه چیزی پیدا کرده‌اید | چه کنید |
|---|---|
| یک باگ | [گزارش باگ](https://github.com/km-arc/Naravel/issues/new?template=bug_report.yml) باز کنید. فرم بستهٔ مربوط، نسخه و یک نمونهٔ بازتولید حداقلی می‌خواهد؛ بهترین نمونه یک تست شکست‌خورده است. |
| یک قابلیت لاراولی که نیست یا اشتباه است | **قبل از نوشتن کد** یک [درخواست قابلیت](https://github.com/km-arc/Naravel/issues/new?template=feature_request.yml) باز کنید. معادل تازه‌ای از لاراول اول PDR می‌خواهد (پایین را ببینید) و تصمیم بزرگ تأیید مالک. |
| یک مشکل امنیتی | issue عمومی **باز نکنید**. [SECURITY.md](SECURITY.md) را دنبال کنید. |
| غلط تایپی، لینک خراب، جملهٔ نامفهوم | pull request بدون issue هم پذیرفته است. مستندات به دو زبان انگلیسی و فارسی است؛ هر دو را اصلاح کنید یا بگویید کدام را نتوانستید. |
| یک پرسش | issue باز کنید و بگویید چه امتحان کرده‌اید. پرسشی که یک خط گمشده در مستندات را نشان دهد، به اصلاح مستندات تبدیل می‌شود. |

## پیش از نوشتن کد

1. **قاعدهٔ صفر.** قابلیتی از لاراول را فقط وقتی پیاده کنید که در .NET ارزش بیاورد. اگر .NET همان مسئله را بومی و درست حل می‌کند
   (session، هش رمز، مجوزدهی مسیر)، تغییر درست یک دستورالعمل مستند است، نه یک بسته.
2. **اول PDR.** معادل تازه‌ای از لاراول با یک PDR (Parity Decision Record) در [docs/pdr/en](docs/pdr/en) و
   [docs/pdr/fa](docs/pdr/fa) شروع می‌شود: قابلیت لاراول، گزینهٔ بومی .NET، ارزش افزوده، تصمیم، جایگزین‌های ردشده.
3. **آنچه به کارتان می‌آید را بخوانید.** [AGENTS.md](AGENTS.md) قواعد سخت را دارد (وابستگی یک‌طرفه، `Manager<TDriver, TOptions>`
   برای ماژول‌های driver، async با `CancellationToken`، هرگز تعیین نوع CLR از داده ذخیره‌شده، هیچ وابستگی تازه‌ای خارج از
   `Microsoft.Extensions.*` بدون PDR). [docs/fa/module-authoring-guide.md](docs/fa/module-authoring-guide.md) ساخت یک ماژول تازه را
   گام‌به‌گام می‌گوید. وضعیت هر مرحله فقط در [ROADMAP.md](ROADMAP.md) است؛ وضعیت یا تعداد تست را در فایل دیگری کپی نکنید
   (`roadmap/check.py` در این حالت خطا می‌دهد).

## راه‌اندازی

SDK نسخهٔ .NET 10 لازم است؛ `global.json` باند نسخه را ثابت می‌کند تا کامپایلر شما با CI یکی باشد. یا مخزن را در dev container
باز کنید (`.devcontainer/`، VS Code یا Codespaces).

```sh
git clone https://github.com/km-arc/Naravel.git
cd Naravel
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

همه‌چیز یک مخزن و یک solution است (`Naravel.slnx`). solution دوم، workflow جدا برای هر ماژول یا کپی فایل‌های مشترک build نسازید.
نسخهٔ بسته‌های NuGet فقط در `Directory.Packages.props` است؛ target framework، متادیتای بسته و نسخهٔ هم‌گام فقط در
`Directory.Build.props`.

## انجام یک تغییر

هر pull request یک دغدغه داشته باشد. از `main` شاخه بگیرید و نام شاخه نوع کار را بگوید (`fix/...`، `feat/...`، `docs/...`، `test/...`،
`ci/...`).

تغییر وقتی آماده است که:

- **بدون هشدار build شود.** برای همهٔ کد زیر `src/` هشدار خطاست، از جمله analyzerهای trim/AOT و بررسی کامنت XML (`CS1591`).
  typeهای عمومی XML doc می‌خواهند با: هدف، *معادل لاراول*، *چرا وجود دارد* و *چه چیزی عمداً پورت نشد*.
- **API عمومی تازه اعلام شده باشد.** `Microsoft.CodeAnalysis.PublicApiAnalyzers` تا وقتی هر عضو عمومی تازه در
  `PublicAPI.Unshipped.txt` همان پروژه نیامده build را رد می‌کند (quick fix در IDE خط را اضافه می‌کند). حذف یا تغییر عضو عمومی یک
  بستهٔ منتشرشده تغییر ناسازگار است: در pull request و در `CHANGELOG.md` بنویسید (Naravel در 0.x است، پس با ثبت در فهرست مجاز
  است، OD-09).
- **تست داشته باشد.** xUnit و FluentAssertions 7.x (نسخه‌های 8 به بعد تجاری‌اند، پس مخزن روی 7.x می‌ماند). ماژول driver علاوه بر
  آن تست `Extend` در زمان اجرا، تست بارگذاری مجدد پیکربندی (اگر config-driven است)، تست قرارداد برای هر driver و یک fake می‌خواهد.
  تست‌هایی که Redis، RabbitMQ، Kafka یا Memcached واقعی لازم دارند به متغیرهای `NARAVEL_TEST_*` (پایین) وابسته‌اند و وقتی سرویس
  نیست «skipped» دیده می‌شوند، نه «passed».
- **مستندات به هر دو زبان باشد.** تغییر رفتار، `docs/en/*` و `docs/fa/*` را با هم به‌روز می‌کند. *دلیل* تصمیم را در مستندات یا پیام commit
  بنویسید، نه فقط خود تصمیم را.
- **`CHANGELOG.md` یک خط زیر `[Unreleased]` داشته باشد** برای هر چیزی که کاربر یک بسته ممکن است متوجه شود.
- **بررسی roadmap سبز باشد:** `python3 roadmap/check.py`.
- **بگویید چه چیزی را اجرا نکرده‌اید.** اگر نتوانستید `dotnet build`، `dotnet test` یا سرویس یک provider را اجرا کنید، در pull request بنویسید
  `NOT RUN: <دلیل>`. تأیید نهایی CI است و تست اجرا‌نشده هرگز «موفق» توصیف نمی‌شود.

### commit و pull request

موضوع commit به شکل `type(scope): summary` و امری است، مثلاً `fix(queue): never leave a reserved message behind` یا
`docs(roadmap): close D-76`. نوع‌های رایج: `feat`، `fix`، `docs`، `test`، `build`، `ci`، `chore`. متن commit *چرا* را می‌گوید؛ خود diff
*چه* را می‌گوید.

pull request را به `main` باز کنید. قالب pull request همان موارد فهرست بالا را می‌پرسد. `main` محافظت‌شده است: بررسی تجمیعی
**`ci-ok`** باید سبز باشد و شاخه باید به‌روز باشد (ببینید [.github/BRANCH-PROTECTION.md](.github/BRANCH-PROTECTION.md)). تا وقتی
بازبینی باز است، commit تکمیلی بهتر از force-push است.

ایجنت‌های هوش مصنوعی هم مثل هر مشارکت‌کنندهٔ دیگری از [AGENTS.md](AGENTS.md) پیروی می‌کنند.

## تست

بررسی‌های solution را با SDK نسخهٔ .NET 10 اجرا کنید:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

برای کار روی یک ماژول فقط پروژهٔ تست آن را اجرا کنید، مثلاً `dotnet test tests/Naravel.Cache.Tests -c Release`.

### تست‌های یکپارچگی provider

سرویس‌های محلی را با `docker compose up -d --wait` بالا بیاورید. همان آدرس‌هایی را که CI استفاده می‌کند تنظیم کنید و تست‌های solution را
اجرا کنید تا موارد وابسته به محیط اجرا شوند:

```sh
NARAVEL_TEST_REDIS=localhost:6379 \
NARAVEL_TEST_RABBITMQ='amqp://naravel:naravel@localhost:5672/%2f' \
NARAVEL_TEST_KAFKA=localhost:9092 \
NARAVEL_TEST_MEMCACHED=localhost:11211 \
dotnet test Naravel.slnx -c Release
```

بعد از پایان با `docker compose down` سرویس‌ها را متوقف کنید.

### اجرای محلی با یک دستور (مثل CI)

`scripts/test-local.sh` (لینوکس/macOS) یا `scripts/test-local.ps1` (ویندوز) سرویس‌ها را بالا می‌آورد، متغیرهای `NARAVEL_TEST_*` را
می‌گذارد و بعد کل solution را restore، build و test می‌کند؛ پس خطای CI را قبل از push می‌توانید بازتولید کنید:

```sh
scripts/test-local.sh                                   # سرویس‌ها + همهٔ تست‌ها
scripts/test-local.sh --down                            # ...و بعد توقف سرویس‌ها
scripts/test-local.sh --no-services                     # چرخهٔ سریع، تست‌های provider رد می‌شوند
scripts/test-local.sh --db                              # همراه MySQL، PostgreSQL، SQL Server (حدود 2 گیگابایت RAM)
scripts/test-local.sh --db -- --filter "Dialect=Postgres"   # فقط یک dialect پایگاه داده
scripts/test-local.sh -- --filter "FullyQualifiedName~Redis"
```

### Dev container

مخزن را در VS Code یا Codespaces باز کنید («Reopen in Container») تا SDK آمادهٔ .NET 10 با دسترسی به Docker داشته باشید
(`.devcontainer/`). `global.json` باند SDK را ثابت می‌کند، پس build محلی و CI یک کامپایلر دارند.

## مجوز

Naravel تحت [مجوز MIT](LICENSE) منتشر می‌شود. مشارکت‌ها هم با همان مجوز پذیرفته می‌شوند.
