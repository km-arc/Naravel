# Naravel.Templates

[![NuGet](https://img.shields.io/nuget/v/Naravel.Templates.svg?label=nuget)](https://www.nuget.org/packages/Naravel.Templates)
[![Downloads](https://img.shields.io/nuget/dt/Naravel.Templates.svg)](https://www.nuget.org/packages/Naravel.Templates)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/km-arc/Naravel/blob/main/LICENSE)

`dotnet new` templates for Naravel: a ready application with a Laravel-like folder structure and one sample configuration file per module, and generators for the classes you write most.

| Fact | Detail |
|---|---|
| **Install** | `dotnet new install Naravel.Templates` |
| **Targets** | the .NET 10 SDK; the generated application targets .NET 10 |
| **Needs** | nothing else: the default application uses no database server, no Redis and no broker |
| **Templates** | `naravel` (the application) and `naravel-job`, `naravel-event`, `naravel-listener`, `naravel-mailable` and `naravel-controller` (one class each) |
| **Version** | the same as every Naravel package; the generated application uses the Naravel packages of that version |

## What you get

- **A working application in one command.** `app/`, `config/`, `routes/`, `resources/`, `storage/` and `tests/`, with a welcome page at `/` and a health check at `/up`.
- **No service to run.** The defaults are the memory cache, the `log` mail transport, the file queue and a SQLite file; parameters pick PostgreSQL, MySQL, SQL Server, Redis, RabbitMQ, Kafka or SMTP instead.
- **One configuration file per module.** The driver you chose is active; secrets come from environment variables or `dotnet user-secrets`, and there is no `.env` file and no application key.
- **Generators.** The item templates add a job, an event, a listener, a mailable or a controller in the folder you name.
- **Tests that run.** The generated tests project uses the fakes that ship with the packages.

## Quick start

```sh
dotnet new install Naravel.Templates
dotnet new naravel -n Blog
cd Blog
dotnet run
```

`dotnet run` prints the URL it listens on. Add `--database postgres --cache redis --queue redis` to `dotnet new naravel` to choose other drivers, and `dotnet new naravel-job -n SendWelcome -o app/Jobs` to add a job class.

## Related packages

| Package | Adds |
|---|---|
| [`Naravel.Routing`](https://www.nuget.org/packages/Naravel.Routing) | Route groups, named routes, resource routes and a middleware engine for ASP.NET Core |
| [`Naravel.Cache`](https://www.nuget.org/packages/Naravel.Cache) | Named cache stores, tagging, scoping and locks; the memory store by default |
| [`Naravel.Queue`](https://www.nuget.org/packages/Naravel.Queue) | Jobs, retries with backoff, chaining, batching and the background worker; the file driver by default |
| [`Naravel.Events`](https://www.nuget.org/packages/Naravel.Events) | A typed event dispatcher with listener ordering and stop propagation |
| [`Naravel.Mail`](https://www.nuget.org/packages/Naravel.Mail) | Mailable classes and named transports; the `log` transport by default |
| [`Naravel.Database`](https://www.nuget.org/packages/Naravel.Database) | The query builder on named connections; SQLite by default |

## Learn more

- [Getting started](https://github.com/km-arc/Naravel/blob/main/docs/en/getting-started.md): which package to install and which call registers it
- [Decision record](https://github.com/km-arc/Naravel/blob/main/docs/pdr/en/PDR-025-project-starter-template.md): why a template pack and not a command-line tool
- [Changelog](https://github.com/km-arc/Naravel/blob/main/CHANGELOG.md) · [Source and issues](https://github.com/km-arc/Naravel) · MIT license

Part of [Naravel](https://github.com/km-arc/Naravel): Laravel's names, .NET's type system. All Naravel packages share one version.
