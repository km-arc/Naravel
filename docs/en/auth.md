# Auth middleware (`auth`, `guest`, `can`, `verified`)

**Naravel has no auth package and no `auth`, `can` or `verified` aliases.** ASP.NET Core authentication and authorization already attach to a route as endpoint metadata, and `Naravel.Routing` exposes that builder ([PDR-016](../pdr/en/PDR-016-auth-lite.md), status `DECLINED`). The built-in `guest` alias stays. This page is the recipe. Persian version: [docs/fa/auth.md](../fa/auth.md).

## Quickstart

```csharp
builder.Services.AddAuthentication().AddCookie();                       // or AddJwtBearer(...)
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("verified", p => p.RequireClaim("email_verified", "true"));
    o.AddPolicy("can-edit-post", p => p.RequireAssertion(ctx => ctx.User.IsInRole("editor")));
});
app.UseAuthentication(); app.UseAuthorization(); app.UseNaravelRouting();   // this order

app.MapNaravel(r =>
{
    r.Get("/dashboard", Dashboard).Configure(b => b.RequireAuthorization());                 // auth
    r.Get("/billing", Billing).Configure(b => b.RequireAuthorization("verified"));           // auth + verified
    r.Post("/posts/{post}", Update).Configure(b => b.RequireAuthorization("can-edit-post")); // can:update
    r.Get("/login", Login).Middleware("guest");                                               // guest (built in)
});
```

## Laravel to .NET

| Laravel | .NET |
|---|---|
| `auth` | `RequireAuthorization()` |
| `auth:sanctum`, `auth:api` (a guard) | An authentication scheme (`AddCookie`, `AddJwtBearer`) and `RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "..." })` |
| `guest` | `.Middleware("guest")` (built in, answers 403 to a signed-in user, no redirect) |
| `can:update,post` | A named policy with `RequireAuthorization("policy")`; for a loaded resource call `IAuthorizationService.AuthorizeAsync(user, post, "policy")` inside the handler |
| `verified` | A policy that requires the claim your sign-in code sets; Identity can also require a confirmed email at sign-in (`RequireConfirmedEmail`) |
| `password.confirm` | A policy that requires a recent-sign-in claim, set by your confirm endpoint |
| Route groups with middleware | `MapGroup("/admin").RequireAuthorization()` |

## Complete example

Cookie sign-in, a `guest` login page, one policy per route, a resource-based check and a native group. `Credentials` and `IPostStore` are your own types; `Claim`, `ClaimsPrincipal` and `ClaimsIdentity` come from `System.Security.Claims`. `Configure` is per route; for one rule on many routes use `app.MapGroup(...)`.

```csharp
// Program.cs
builder.Services.AddAuthentication("cookie").AddCookie("cookie", o => { o.LoginPath = "/login"; o.AccessDeniedPath = "/denied"; });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("verified", p => p.RequireClaim("email_verified", "true"));
    o.AddPolicy("editor",   p => p.RequireRole("editor"));
});

app.UseAuthentication();
app.UseAuthorization();
app.UseNaravelRouting();                                   // this order

app.MapNaravel(r =>
{
    r.Get("/login", () => Results.Ok("login form")).Middleware("guest");            // guest (403 for a signed-in user)

    r.Post("/login", async (Credentials body, HttpContext http) =>
    {
        // check the password first (see hashing.md), then sign in with the claims your policies read
        var claims = new[] { new Claim(ClaimTypes.Name, body.Email), new Claim("email_verified", "true") };
        await http.SignInAsync("cookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "cookie")));
        return Results.Redirect("/dashboard");
    }).Middleware("guest");

    r.Get("/dashboard", (ClaimsPrincipal user) => Results.Ok($"hello {user.Identity!.Name}"))
        .Configure(b => b.RequireAuthorization());                                   // auth

    r.Get("/billing", () => Results.Ok("billing"))
        .Configure(b => b.RequireAuthorization("verified"));                         // auth + verified

    r.Post("/posts/{id}", (string id) => Results.Ok(id))
        .Configure(b => b.RequireAuthorization("editor"));                           // can:update

    r.Post("/logout", async (HttpContext http) =>
    {
        await http.SignOutAsync("cookie");
        return Results.Redirect("/login");
    });
});

// Resource-based check inside a handler (Laravel: can:update,post). Needs a policy named "can-edit-post".
app.MapPut("/posts/{id}", async (string id, ClaimsPrincipal user, IAuthorizationService authz, IPostStore posts) =>
{
    var post = await posts.FindAsync(id);
    if (post is null) return Results.NotFound();
    return (await authz.AuthorizeAsync(user, post, "can-edit-post")).Succeeded ? Results.Ok() : Results.Forbid();
});

// Many routes with the same rule: use a native group.
var admin = app.MapGroup("/admin").RequireAuthorization("editor");
admin.MapGet("/stats", () => Results.Ok("stats"));
```

## 401, 403 or redirect

The configured authentication scheme decides, not Naravel: a cookie scheme redirects to the login path (challenge) or the access-denied path (forbid), a bearer scheme answers 401 with `WWW-Authenticate` or 403. This is why there is no `auth` alias: one that checked `context.User` itself would be a second authorization path with its own status codes, outside the scheme's challenge and the policy evaluator, and invisible to OpenAPI.

## What this does not cover

- **Identity.** Users, roles, email confirmation and token endpoints are ASP.NET Core Identity (`AddIdentity`, `AddIdentityApiEndpoints`).
- **Password hashing** is in [docs/en/hashing.md](hashing.md); **sessions** are in [docs/en/session.md](session.md).
- **A metadata-only `auth` alias** (the Laravel spelling for `RequireAuthorization`) is in the backlog as an idea, not planned.
