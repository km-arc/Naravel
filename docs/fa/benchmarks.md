# خط مبنای بنچمارک

بنچمارک‌ها با BenchmarkDotNet نسخهٔ 0.15.8 روی Ubuntu 24.04.4، SDK دات‌نت 10.0.112، runtime نسخهٔ
10.0.12 و Intel Core i7-7700 (با یک CPU در دسترس) اجرا شدند:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*'
```

| بنچمارک | میانگین | حافظهٔ تخصیص‌یافته |
|---|---:|---:|
| push + pop + ack صف Memory | 1.434 us | 1,120 B |
| set + get در cache حافظه‌ای | 750.7 ns | 688 B |
| get در cache دارای tag | 236.3 ns | 432 B |
| serialize + deserialize با reflection | 481.9 ns | 208 B |
| serialize + deserialize با source generation | 301.4 ns | 88 B |
| dispatch گرم `Naravel.Events` با یک listener بدون کار | 137.8 ns | 24 B |

این نتایج خط مبنای محلی‌اند و ادعای throughput سرویس نیستند. BenchmarkDotNet برای serialization با
source generation توزیع چندقله‌ای گزارش کرد؛ پیش از مقایسهٔ تغییرهای کوچک، روی سخت‌افزار هدف دوباره اجرا کنید.
بنچمارک Events با job کوتاه سه‌تکراری روی Ubuntu 24.04.5 و .NET 10.0.12 اجرا شد؛ بازهٔ اطمینان ۹۹٫۹٪ گسترده بود،
پس این عدد فقط خط مبنای اولیه است. هدف تخصیص صفر حاصل نشد: context dispatch برای پشتیبانی از توقف انتشار تخصیص می‌یابد.

اجرای بنچمارک Events:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*EventDispatchBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

اجرای بنچمارک Database:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*DatabaseBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

`DatabaseBenchmarks` (PDR-022 S2) ساخت و کامپایل یک کوئری را با یک رشتهٔ ثابت مقایسه می‌کند (`CompileBuilder` در برابر baseline با نام `CompileFloor`، بدون I/O) و همان کوئری را روی SQLite در حافظه از طریق builder (`BuilderRows`، `BuilderTyped`)، Dapper دست‌نویس (`HandWrittenDapper`) و ADO.NET خام (`HandWrittenAdo`) اجرا می‌کند؛ هر کدام در هر فراخوانی یک اتصال باز می‌کنند. **هنوز عددی ثبت نشده:** بنچمارک اجرا نشده است (محیط نوشتن SDK دات‌نت ندارد). نخستین baseline را بعد از اجرا روی CI یا ماشین توسعه‌دهنده همین‌جا ثبت کنید و سربار builder را اگر در برابر رفت‌وبرگشت SQLite بزرگ بود، پیش از R20 یک نقص بدانید.

بنچمارک Pagination را این‌طور اجرا کنید:

benchmark استریم را اجرا کنید با:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*StreamingBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

`StreamingBenchmarks` (PDR-022 R22.T04) همهٔ ردیف‌های یک جدول SQLite درون‌حافظه با ۵۰ هزار ردیف را می‌خواند: `BufferedGet` (خط پایه) کل نتیجه را بار می‌کند، `CursorEnumerate` روی یک reader باز استریم می‌کند، `LazyPages` با `limit`/`offset` صفحه‌بندی می‌کند و `LazyByIdPages` و `ChunkByIdPages` با keyset روی `id`. ابتدا ستون `Allocated` را بخوانید: استریم برای محدود نگه داشتن حافظه است، پس نسخه‌های استریم باید به‌ازای ردیف‌های بازدیدشده تخصیص بدهند نه ردیف‌های نگه‌داشته‌شده، در حالی که `BufferedGet` هر ۵۰ هزار ردیف را همزمان نگه می‌دارد؛ و صفحه‌بندی keyset نباید در صفحه‌های عمیق مثل `limit`/`offset` کند شود. **هنوز عددی ثبت نشده:** benchmark اجرا نشده است (محیط نگارش .NET SDK ندارد). اولین baseline را بعد از اجرا روی CI یا دستگاه توسعه‌دهنده اینجا ثبت کنید؛ اگر یک نسخهٔ استریم از `BufferedGet` بیش از هزینهٔ هر ردیف reader باز کندتر بود، آن را نقص بدانید.

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*PaginationBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

`PaginationBenchmarks` (PDR-020 S2) یک صفحهٔ ۲۰ ردیفی از جدول ۵۰ هزار ردیفی SQLite در حافظه را با `OFFSET` (`OffsetFirstPage`، `SimpleOffsetFirstPage`، `SimpleOffsetDeepPage`، `OffsetDeepPage`) و با کرسر (`KeysetFirstPage`، `KeysetDeepPage`) می‌خواند؛ صفحهٔ عمیق صفحهٔ ۲۰۰۰ است و `SimpleOffsetDeepPage` baseline است. ردیف‌های صفحهٔ اول شاهد مقایسه‌اند: آنجا دو روش باید تقریباً برابر باشند، صفحهٔ عمیق keyset باید نزدیک صفحهٔ اولش بماند و صفحهٔ عمیق offset با مقدار offset بزرگ شود. **هنوز عددی ثبت نشده:** بنچمارک اجرا نشده است (محیط نوشتن SDK دات‌نت ندارد). نخستین baseline را بعد از اجرا روی CI یا ماشین توسعه‌دهنده همین‌جا ثبت کنید. SQLite بهترین حالت برای `OFFSET` است (بدون شبکه، یک فرایند)، پس فاصلهٔ اینجا کمتر از چیزی است که روی پایگاه‌دادهٔ راه دور دیده می‌شود.
