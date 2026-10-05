# Troubleshooting helpers / عیب‌یابی

## English

**Restore fails with `NU1301` / "Connection refused" on `api.nuget.org`.** This is a network problem on the machine running the command,
not a code problem. CI (`.github/workflows/ci.yml`) runs on GitHub's runners and has NuGet access.

Check connectivity and sources:

```
curl -I https://api.nuget.org/v3/index.json
dotnet nuget list source
```

Clean rebuild:

```
dotnet clean
dotnet restore --no-cache Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

`/p:NuGetAudit=false` skips vulnerability auditing and is only a temporary workaround for offline or restricted networks. Do not commit it
(the repo wants audit warnings visible).

## فارسی

**خطای `NU1301` / «Connection refused» روی `api.nuget.org` هنگام restore.** این مشکل شبکهٔ ماشینی است که دستور را اجرا می‌کند، نه مشکل
کد. CI (`.github/workflows/ci.yml`) روی runnerهای GitHub اجرا می‌شود و به NuGet دسترسی دارد.

بررسی اتصال و منابع:

```
curl -I https://api.nuget.org/v3/index.json
dotnet nuget list source
```

ساخت تمیز:

```
dotnet clean
dotnet restore --no-cache Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

`/p:NuGetAudit=false` فقط ممیزی آسیب‌پذیری را رد می‌کند و راه‌حل موقت برای شبکه‌های آفلاین یا محدود است؛ آن را commit نکنید
(ریپو می‌خواهد هشدارهای audit دیده شوند).
