# PDR-016 — Auth-lite route middleware (`auth`, `guest`, `can`, `verified`)

**Native-first verdict:** yes, plain .NET does this. ASP.NET Core's authentication and authorization already attach to a route as endpoint metadata: `RequireAuthorization()` and `RequireAuthorization("policy")` on the endpoint, with `AllowAnonymous()` and `MapGroup(...).RequireAuthorization()` for groups. `Naravel.Routing` already exposes the native builder (`NaravelRoute.Configure(b => b.RequireAuthorization("policy"))`), and it already ships a `guest` alias. **Proposed outcome: `DECLINED`, with a documented recipe and a table.**

Status: **Proposed, awaiting owner approval.**

## Context

Laravel route middleware names: `auth` (redirect or 401 when not logged in), `auth:sanctum` (a guard), `guest` (redirect when logged in), `can:update,post` (a policy check), `verified` (email verified), `password.confirm`. They are string aliases of the HTTP kernel, used as `Route::middleware('auth')`.

What ASP.NET Core provides, all built in:

- **Authentication:** `AddAuthentication().AddCookie()/AddJwtBearer()` and `UseAuthentication()`; Identity (`AddIdentity`/`AddIdentityApiEndpoints`) adds users, roles, email confirmation and token endpoints. A failed request is **challenged** (401 or a redirect to the login path) or **forbidden** (403) by the authentication scheme, which knows how to answer for its kind of client (a cookie redirect, a bearer `WWW-Authenticate` header).
- **Authorization:** `AddAuthorization(o => o.AddPolicy("CanEdit", p => p.RequireClaim(...)))`, `IAuthorizationService` for resource-based checks (`can:update,post`), `[Authorize]` and `RequireAuthorization` metadata, evaluated by `UseAuthorization()`.
- **Anonymous only (`guest`):** no built-in, but one line: an authorization requirement that fails for an authenticated user.
- **`verified`:** a policy requiring a claim your login code sets (Identity can require a confirmed email at sign-in, `RequireConfirmedEmail`).

The decisive points from `Naravel.Routing`:

- Route middleware runs inside **one** ASP.NET middleware, `NaravelRoutingMiddleware`, placed after routing. ASP.NET's `UseAuthorization()` also runs after routing, reading endpoint metadata. A Naravel alias named `auth` that checked `context.User` itself would be a **second, parallel authorization mechanism**: it would answer 401 in its own way (not through the configured scheme's challenge, so no cookie redirect and no `WWW-Authenticate`), could run before or after `UseAuthorization()` depending on the order the application chose, and would not show up in OpenAPI or endpoint metadata. An alias that only **adds metadata** (`auth` = `RequireAuthorization()`) is correct but is exactly `Configure(b => b.RequireAuthorization())`, with the string form hiding which policy runs.
- The built-in `guest` alias exists and stays: it returns 403 for an authenticated user and does not redirect (documented already).

## Recipe (the documentation that replaces a package)

```csharp
builder.Services.AddAuthentication().AddCookie();                       // or AddJwtBearer(...)
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("verified", p => p.RequireClaim("email_verified", "true"));
    o.AddPolicy("can-edit-post", p => p.RequireAssertion(ctx => ctx.User.IsInRole("editor")));
});
app.UseAuthentication(); app.UseAuthorization(); app.UseNaravelRouting();

app.MapNaravel(r =>
{
    r.Get("/dashboard", Dashboard).Configure(b => b.RequireAuthorization());                 // auth
    r.Get("/billing", Billing).Configure(b => b.RequireAuthorization("verified"));           // auth + verified
    r.Post("/posts/{post}", Update).Configure(b => b.RequireAuthorization("can-edit-post")); // can:update
    r.Get("/login", Login).Middleware("guest");                                               // guest (built in)
});
```

For a resource-based check (`can:update,post`) call `IAuthorizationService.AuthorizeAsync(user, post, "policy")` inside the handler, where the resource is already loaded. Route groups use the native `MapGroup("/admin").RequireAuthorization()`.

## Proposed decision

1. Do not add `auth`, `can`, `verified` aliases or a `Naravel.Auth` package. Record `DECLINED` with this PDR as the reason. The existing `guest` alias is kept and documented.
2. Document the recipe in `docs/en|fa/auth.md` (EN and FA together): the registration order (`UseAuthentication`, `UseAuthorization`, then `UseNaravelRouting`), a table from Laravel names (`auth`, `auth:guard`, `guest`, `can`, `verified`, `password.confirm`) to .NET, the 401 versus 403 versus redirect behaviour of the chosen scheme, and a note that password hashing is in the hashing recipe.
3. Update the parity matrix row to *Native*, the changelog and the backlog (a metadata-only `auth` alias is recorded as an idea).
4. No code, tests or benchmark are added: a test of `RequireAuthorization` would test Microsoft's code.

## Alternatives considered

1. **Aliases that add endpoint metadata** (`.Middleware("auth")`, `.Middleware("can:edit-post")` → `RequireAuthorization(...)`). Technically sound and small. Rejected as a default: it renames one native call into a string, hides which policy runs, and invites the incorrect version below. It can be added later if the owner wants the Laravel spelling.
2. **Aliases that check `context.User` and return 401/403.** Rejected: a second authorization path with its own status codes, bypassing the challenge of the configured scheme and the policy evaluator, and invisible to OpenAPI.
3. **A guard system** (`auth:web`, `auth:api`). Rejected: ASP.NET Core authentication schemes are the guards (`AddCookie`, `AddJwtBearer`; `RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "..." })`).

## Decisions for the owner

- **D1:** `DECLINED` with the recipe (recommended), or the metadata-only aliases of alternative 1.
- **D2:** keep `guest` as it is (recommended).

## Approval

Not yet approved. If approved as proposed, the stage is closed as `DECLINED` once the EN/FA recipe docs exist.
