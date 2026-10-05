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

این نتایج خط مبنای محلی‌اند و ادعای throughput سرویس نیستند. BenchmarkDotNet برای serialization با
source generation توزیع چندقله‌ای گزارش کرد؛ پیش از مقایسهٔ تغییرهای کوچک، روی سخت‌افزار هدف دوباره اجرا کنید.