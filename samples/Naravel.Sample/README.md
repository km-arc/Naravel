# Naravel Sample

The sample combines a queue demo, an events demo, a database (query builder) demo, an executable showcase of `Naravel.Routing`, and small demos of Cache with the rate limiter, Encryption, Mail, Notifications and Scheduling.

Run it with:

```sh
dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

Open `/routing/` for the route index. Routing examples include HTTP verbs, match/any, redirects and fallback; nested prefix/name/domain groups; route constraints and named URL generation; explicit route-model binding; resource and API-resource routes with `Only`, `Except`, and custom parameter names; aliases, argument values, middleware groups, priority, exclusions, inline middleware, `IMiddleware`, controller middleware attributes, terminable middleware, and Naravel middleware attached to native ASP.NET Core endpoints.

The queue demo at `/queue` uses the `file` connection by default. Set `NaravelQueue__SampleConnection` to `redis`, `rabbitmq`, or `kafka` to run the same dispatch and worker flow against that configured provider. For example, with Redis available at the endpoint in `config/appsettings.json`:

```sh
NaravelQueue__SampleConnection=redis dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

The events demo at `/events` dispatches an `OrderPlaced` event through `Naravel.Events` and shows which listeners ran, in order:

1. `FraudScreenListener` (DI listener, registered first) passes normal orders; for a total above 10,000 it calls `context.Stop()` and nothing after it runs.
2. `RecordOrderListener` (DI listener) records the order synchronously.
3. A `Listen` callback registered for the request adds an audit entry after the DI listeners.
4. `SendReceiptListener` is a queued listener (`IQueuedEventListener<OrderPlaced>` registered with `AddQueuedEventListener`). Dispatch only enqueues a job; the sample queue worker runs it about a second later and the result appears in the trace.

The page calls `POST /events/orders?total=120&email=ali@example.com`, `GET /events/trace` and `DELETE /events/trace`. The Events adapter always dispatches on the default queue connection and the `default` queue, while the sample worker consumes `NaravelQueue:SampleConnection`, so keep both equal when you switch providers:

```sh
NaravelQueue__Default=redis NaravelQueue__SampleConnection=redis dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

The database demo at `/database` uses an in-memory SQLite database (`Database:Connections:main` in `config/appsettings.json`), so nothing is installed and no file is written. The `people` table is created on first use with five rows; `POST /database/seed` recreates it. `GET /database/people` builds one query from the query string and returns the SQL with its `@p` markers, the bound values and the rows:

- `?minVotes=20&active=true` adds `Where` conditions only for the parameters that are present.
- `?bornYear=2024` uses `WhereYear`, which is `strftime` on SQLite and `extract`, `year()` or `datepart` on the other engines.
- `?name=x' or '1'='1` returns no rows: the text is a bound value, never SQL.
- `?orderBy=id; drop table people` returns `400`: a column name that is not a plain identifier is rejected when `OrderBy` is called, before any SQL exists.
- `GET /database/people/3` uses `FindAsync`; `GET /database/log` shows the queries recorded by the opt-in `QueryLog` (it keeps bound values, so use it in development only).

To run the same demo on another server, change the connection to `{ "Driver": "mysql" | "pgsql" | "sqlserver", "ConnectionString": "..." }` and register the matching `AddNaravel...Database` call in `config/DatabaseSampleConfig.cs`; the demo's SQL text is the builder's, but the `create table` statement in `app/Data/PeopleSeeder.cs` is SQLite syntax and needs the server's column types. See `docs/en/database.md`.

The module demos need no server: the cache is in memory, mail uses the `log` transport and encryption uses Data Protection's default key ring. They are plain minimal-API endpoints in `app/Demos/ModuleDemoEndpoints.cs`, registered in `config/ModulesSampleConfig.cs`. Try them with `curl` (add `-X POST` or `-X DELETE` where shown):

| Demo | Call | What to look at |
|---|---|---|
| Cache | `GET /cache/settings` twice, then `DELETE /cache/settings` | `RememberAsync` loads once for 30 seconds; the load number only changes after the delete or the ttl. |
| Rate limiter | `POST /cache/attempt?subject=ali` four times | 3 attempts per 30 seconds; the 4th answers `429` with `Retry-After`. `DELETE /cache/attempt?subject=ali` resets the bucket. |
| Encryption | `GET /encryption/text?value=hello`, `/encryption/typed`, `/encryption/purpose` | Round trip of text and of a typed value; a value written under the purpose `billing` cannot be read by the default encrypter. |
| Mail | `POST /mail/welcome?name=Ali` | The `log` transport writes the message to the console of `dotnet run`; nothing is delivered. Set `Mail:Default` to a real transport to send. |
| Notifications | `POST /notifications/invoice?id=7` | One notification through the channels `log` and `trace`, in that order. |
| Scheduling | wait about a minute, then `GET /demos/trace` | The `sample-heartbeat` task runs once a minute with `WithoutOverlapping`. |

`GET /demos/trace` returns the last 50 lines the mail, notification and scheduler demos wrote. It is an in-memory list for the sample only.

For Redis-backed limiting with several instances, register `AddNaravelRedisCache` and call `AddNaravelRateLimiter("redis")`; see `docs/en/cache.md`.

Protected examples accept the request header `X-Sample-Key: naravel-demo`. The role example additionally expects `X-Sample-Role: admin`. The domain-scoped examples require the host `tenant.example.test`.

The sample does not claim full Laravel routing parity. `route:list`, view-route helpers, string controller actions, route caching, scoped binding, missing-model callbacks, and authorization policies are not implemented by the current routing package (route authorization is native `RequireAuthorization`; see `docs/en/auth.md`). See `docs/en/routing.md` and PDR-009 for the current decisions and limits; `PROGRESS-ROUTING.md` is the historical record.