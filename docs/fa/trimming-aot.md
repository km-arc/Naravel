# وضعیت Trimming و Native AOT

**Naravel هنوز trim-safe یا سازگار با AOT نیست و هیچ پکیجی `IsTrimmable` یا `IsAotCompatible` اعلام نمی‌کند.** این صفحه فهرست اندازه‌گیری‌شده است، نه وعده. نسخهٔ کامل و جدول: [docs/en/trimming-aot.md](../en/trimming-aot.md).

- اندازه‌گیری: `.github/workflows/evidence.yml` همهٔ پروژه‌های `src/` را با analyzerهای trim و AOT می‌سازد (گزارشی، نه gate).
- نتیجهٔ 2026-10-28: ۱۲۶ هشدار یکتا در ۱۸ پکیج؛ حدود نه‌دهم آن سریال‌سازی JSON مبتنی بر reflection (IL2026 و IL3050) و بقیه bind پیکربندی و کمک‌های DI است. بزرگ‌ترین‌ها: `Naravel.Queue` (۲۱)، `Naravel.Queue.Redis` (۱۸)، `Naravel.Queue.Database` (۱۵)، `Naravel.Queue.File` (۱۴).
- یعنی برنامه‌هایی که با trimming یا Native AOT منتشر می‌شوند باید انتظار هشدار و خطای احتمالی زمان اجرا از این پکیج‌ها را داشته باشند؛ استقرار JIT تحت تأثیر نیست.
- برنامه: پکیج به پکیج، از کوچک‌ترین (`Naravel.Database`، `Naravel.Encryption`، `Naravel.Cache.Redis`، `Naravel.Pagination`) با همان الگوی `JsonTypeInfo` منبع‌تولیدشده؛ `IsAotCompatible` فقط وقتی برای یک پکیج گذاشته می‌شود که ردیف آن در فهرست بعدی صفر باشد.
