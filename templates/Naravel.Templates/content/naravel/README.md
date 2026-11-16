# AppProjectName

A [Naravel](https://github.com/km-arc/Naravel) application. It runs with no database server, no Redis and no message broker.

## Run it

```sh
dotnet run
```

`dotnet run` prints the address it listens on. Open it for the welcome page, and `/up` for the health check. Run the command from this folder: the application reads `config/` and `resources/` relative to the folder it is started in.

Try the example flow, with the address `dotnet run` printed:

```sh
curl -i -X POST http://localhost:5000/register -H "Content-Type: application/json" -d '{"email":"ada@example.com","name":"Ada"}'
```

The controller dispatches the `UserRegistered` event, a listener queues the `SendWelcomeEmail` job in `storage/queue`, the queue worker that runs inside the application takes it, and the `log` mail transport writes the welcome mail to the log.

## Tests

```sh
dotnet test tests/AppProjectName.Tests
```

The tests use the fakes that ship with the packages (`EventFake`, `QueueFake`, `MailFake`), so they need no queue and no mail server.

## Where things are

| Folder | What it holds |
|---|---|
| `app/` | Your code: controllers, events, listeners, jobs and mailables |
| `bootstrap/` | How the application starts: how `config/` is read |
| `config/` | One JSON file per module: `cache`, `database`, `logging`, `mail`, `queue` |
| `routes/` | `WebRoutes.cs`: the routes of the application |
| `resources/` | Files the application serves, such as `views/welcome.html` |
| `storage/` | Written while the application runs (the queue, the SQLite file). Created on the first run and ignored by git |
| `tests/` | The test project |

## Configuration and secrets

Each file in `config/` configures one module and holds the driver that is active. A value in the environment replaces the same key in a file, with `__` for each `:`: `Mail__From=team@example.com` replaces `Mail:From`. The command line does the same: `dotnet run -- --Mail:From=team@example.com`.

Keep secrets, such as a connection string or a password, out of the files. In development, use user secrets, which the application reads after `config/`. `Mail:From` stands in here for any key:

```sh
dotnet user-secrets init
dotnet user-secrets set "Mail:From" "team@example.com"
```

In production, set the environment variables on the server. There is no `.env` file and no application key to generate.

## Learn more

- [Naravel guides](https://github.com/km-arc/Naravel/tree/main/docs/en): routing, events, queue, mail, cache and database
- [Getting started](https://github.com/km-arc/Naravel/blob/main/docs/en/getting-started.md): which package to install and which call registers it
