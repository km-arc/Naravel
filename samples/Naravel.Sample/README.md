# Naravel Sample

The sample combines the queue demo with an executable showcase of `Naravel.Routing`.

Run it with:

```sh
dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

Open `/routing/` for the route index. Routing examples include HTTP verbs, match/any, redirects and fallback; nested prefix/name/domain groups; route constraints and named URL generation; explicit route-model binding; resource and API-resource routes with `Only`, `Except`, and custom parameter names; aliases, argument values, middleware groups, priority, exclusions, inline middleware, `IMiddleware`, controller middleware attributes, terminable middleware, and Naravel middleware attached to native ASP.NET Core endpoints.

The queue demo at `/queue` uses the `file` connection by default. Set `NaravelQueue__SampleConnection` to `redis`, `rabbitmq`, or `kafka` to run the same dispatch and worker flow against that configured provider. For example, with Redis available at the endpoint in `config/appsettings.json`:

```sh
NaravelQueue__SampleConnection=redis dotnet run --project samples/Naravel.Sample/Naravel.Sample.csproj
```

Protected examples accept the request header `X-Sample-Key: naravel-demo`. The role example additionally expects `X-Sample-Role: admin`. The domain-scoped examples require the host `tenant.example.test`.

The sample does not claim full Laravel routing parity. Controller resource mapping, `route:list`, view-route helpers, string controller actions, route caching, scoped binding, missing-model callbacks, and authorization policies are not implemented by the current routing package. See `docs/en/routing.md`, `PROGRESS-ROUTING.md`, and PDR-009 for the current decisions and follow-ups.