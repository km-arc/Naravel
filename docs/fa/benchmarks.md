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