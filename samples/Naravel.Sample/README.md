# Naravel Sample

The sample combines a queue demo, an events demo, and an executable showcase of `Naravel.Routing`.

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

Protected examples accept the request header `X-Sample-Key: naravel-demo`. The role example additionally expects `X-Sample-Role: admin`. The domain-scoped examples require the host `tenant.example.test`.

The sample does not claim full Laravel routing parity. `route:list`, view-route helpers, string controller actions, route caching, scoped binding, missing-model callbacks, and authorization policies are not implemented by the current routing package. See `docs/en/routing.md`, `PROGRESS-ROUTING.md`, and PDR-009 for the current decisions and follow-ups.