# PDR-019 — Password hashing (`Hasher`)

**Native-first verdict:** yes, plain .NET does this in three lines: `PasswordHasher<TUser>` from `Microsoft.Extensions.Identity.Core` hashes, verifies, and says when a stored hash should be upgraded. A Naravel wrapper would add a second API for the same three calls. **Proposed outcome: `DECLINED`, with a documented recipe.**

Status: **Proposed, awaiting owner approval.** Nothing is built before the owner answers; see *Approval*.

## Context

Laravel's `Hash` facade (`Illuminate\Contracts\Hashing\Hasher`) has three calls the application uses: `Hash::make($value, $options)`, `Hash::check($plain, $hashed)` and `Hash::needsRehash($hashed)`. It supports three drivers (`bcrypt`, the default; `argon`; `argon2id`), takes work-factor options (`rounds`; `memory`, `time`, `threads`), and `check` throws when the hash was made by another algorithm than the selected one (switched off with `HASH_VERIFY=false` while migrating). Source: [Laravel 12 hashing](https://laravel.com/docs/12.x/hashing).

What the .NET base class library and ASP.NET Core already offer:

- `PasswordHasher<TUser>` (`Microsoft.Extensions.Identity.Core`, no web dependency): `HashPassword(user, password)` and `VerifyHashedPassword(user, hash, password)`, which returns `Failed`, `Success` or `SuccessRehashNeeded`.
- Format "Identity V3": PBKDF2 with HMAC-SHA512, a 16-byte random salt, a 32-byte key, and a header that stores the format marker and the iteration count. The default is 100,000 iterations (`PasswordHasherOptions.IterationCount`). Because the count is stored in the hash, a hash made with an older, lower count still verifies and is reported as `SuccessRehashNeeded`. Sources: [PasswordHasher.cs](https://source.dot.net/Microsoft.Extensions.Identity.Core/PasswordHasher.cs.html), [PasswordHasherOptions.cs](https://source.dot.net/Microsoft.Extensions.Identity.Core/PasswordHasherOptions.cs.html).
- The `user` argument is accepted but not read by the implementation, so the type only has to be a class.
- No bcrypt or Argon2 exists in the BCL. Both need a third-party library.

## The recipe that replaces a package

```csharp
// dotnet add package Microsoft.Extensions.Identity.Core
services.Configure<PasswordHasherOptions>(o => o.IterationCount = 250_000);   // see "Work factor"
services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

var hash   = hasher.HashPassword(user, password);                              // Hash::make
var result = hasher.VerifyHashedPassword(user, hash, password);                // Hash::check + Hash::needsRehash
if (result == PasswordVerificationResult.SuccessRehashNeeded) hash = hasher.HashPassword(user, password); // store it
```

| Laravel | .NET |
|---|---|
| `Hash::make` | `HashPassword` |
| `Hash::check` | `VerifyHashedPassword` returns `Success` or `SuccessRehashNeeded` |
| `Hash::needsRehash` | `SuccessRehashNeeded`. It is reported while verifying, which is the only moment the plain password is available to rehash anyway. |
| `rounds` | `PasswordHasherOptions.IterationCount` |
| `argon`, `argon2id` | Not in the BCL. Not offered (see alternative 2). |
| Algorithm verification (`HASH_VERIFY`) | Not needed: the Identity hash carries its own format marker, and an unknown marker returns `Failed`. |

## Work factor (the one thing worth saying out loud)

The default of 100,000 iterations is below the current OWASP guidance for PBKDF2-HMAC-SHA512 (220,000; 600,000 for HMAC-SHA256). OWASP ranks Argon2id first and PBKDF2 as the choice when FIPS-140 is required. The recipe therefore sets the count explicitly. Pick a number by measuring on production hardware (a few hundred milliseconds is typical), and raise it later: existing hashes keep verifying and are upgraded at the next login. Source: [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).

## Proposed decision

1. Do not add a `Naravel.Hashing` package. Record `DECLINED` with this PDR as the reason.
2. Document the recipe above in `docs/en|fa/hashing.md` (EN and FA together), including the work-factor note and the upgrade-on-login pattern.
3. Update the parity matrix row to *Native*, the changelog and the backlog.
4. No code, no tests and no benchmark are added: there is no new behaviour to test, and a test of `PasswordHasher` would test Microsoft's code.

## Alternatives considered

1. **A thin `IHasher` with `Make`, `Check`, `NeedsRehash` over `IPasswordHasher`.** Rejected. It renames three calls, adds a package and a third API shape next to `IPasswordHasher` that ASP.NET Core Identity users already know, and gives no behaviour the recipe lacks. It fails rule zero.
2. **Add bcrypt or Argon2id through a third-party package** (for example for Laravel-to-.NET migrations that must verify existing `$2y$` hashes, or to meet an Argon2id policy). Rejected for now: a new dependency needs its own PDR (OD-01/OD-02), and the need is narrow. It is recorded in the backlog and can be revisited if an owner-approved migration scenario needs it.
3. **Our own PBKDF2 wrapper over `Rfc2898DeriveBytes`.** Rejected. It would duplicate the Identity format and is exactly the kind of security-sensitive code the project does not write (OD-03).

## Contracts and quality

If the owner picks alternative 1 or 2 instead, the stage would need: no plaintext password or hash in logs, exceptions or telemetry tags; async-free API (hashing is CPU-bound, so no fake async); a `Meter` with bounded outcomes only; an in-memory fake that does not pretend to be secure; known-answer, verify and rehash tests.

## Approval

Pending. The owner is asked to approve one of: **A.** `DECLINED` with the recipe (recommended), **B.** a thin `IHasher`, **C.** a third-party bcrypt/Argon2id package under a new PDR. Until then the stage stays blocked.
