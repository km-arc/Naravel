# Password hashing

**Naravel has no hashing package.** .NET already does what Laravel's `Hash` facade does, so nothing is ported ([PDR-019](../pdr/en/PDR-019-hashing.md), status `DECLINED`). This page is the recipe that replaces it. Persian version: [docs/fa/hashing.md](../fa/hashing.md).

## Quickstart

```sh
dotnet add package Microsoft.Extensions.Identity.Core   # no web dependency
```

```csharp
services.Configure<PasswordHasherOptions>(o => o.IterationCount = 250_000);   // see "Work factor"
services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();   // AppUser: any class

var hash   = hasher.HashPassword(user, password);                              // Laravel: Hash::make
var result = hasher.VerifyHashedPassword(user, hash, password);                // Hash::check and Hash::needsRehash in one call
if (result == PasswordVerificationResult.SuccessRehashNeeded) hash = hasher.HashPassword(user, password);   // store the new hash
```

`PasswordHasher<TUser>` and `PasswordHasherOptions` are in the `Microsoft.AspNetCore.Identity` namespace. `VerifyHashedPassword` returns `Failed`, `Success` or `SuccessRehashNeeded`. The `user` argument is accepted but not read by the implementation, so any class works.

## Laravel to .NET

| Laravel | .NET |
|---|---|
| `Hash::make($value)` | `HashPassword(user, password)` |
| `Hash::check($plain, $hashed)` | `VerifyHashedPassword(...)` is `Success` or `SuccessRehashNeeded` |
| `Hash::needsRehash($hashed)` | `SuccessRehashNeeded`. It is reported while verifying, which is the only time the plain password is available to rehash anyway. |
| `rounds` (bcrypt) | `PasswordHasherOptions.IterationCount` |
| `argon`, `argon2id` | Not in the .NET base library, so not offered |
| Algorithm check (`HASH_VERIFY`) | Not needed for hashes made here: the hash carries its own format marker, and an unknown marker returns `Failed`. A value that is not Base64 at all, such as a Laravel `$2y$` bcrypt hash, is different: `VerifyHashedPassword` throws `FormatException` for it, so look at the stored value's prefix first (see "What this does not cover"). |

## Work factor

The format is PBKDF2 with HMAC-SHA512, a 16-byte random salt and a 32-byte key, and the iteration count is stored inside the hash. The default is **100,000 iterations**, which is below the current [OWASP guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) for PBKDF2-HMAC-SHA512 (220,000). Set the count yourself, as in the quickstart. Choose it by measuring on production hardware (a few hundred milliseconds per hash is typical).

Raising the count later is safe: old hashes still verify, `SuccessRehashNeeded` tells you to store a new one, and users are upgraded at their next login.

## Complete example

Registration and login with a rehash on sign-in. `IUserStore` is your own data access; the endpoints return `401` for both an unknown user and a wrong password.

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

## What this does not cover

- **Argon2id and bcrypt.** OWASP ranks Argon2id first and PBKDF2 as the choice when FIPS-140 is required. Neither Argon2id nor bcrypt is in the .NET base library; using them means a third-party package, which needs its own PDR in this project. That includes verifying existing Laravel `$2y$` hashes after a migration: until such a package is approved, keep the old hashes and rehash each user to the Identity format at their next login with a small adapter of your own. The adapter must branch on the stored value **before** it calls `VerifyHashedPassword`: a `$2y$` hash is not Base64, so passing it to the Identity hasher throws `FormatException` instead of returning `Failed`.
- **Peppering, password policy and breach checks.** Not part of hashing; none is provided.
- **Logging.** Never log a password or a hash.
