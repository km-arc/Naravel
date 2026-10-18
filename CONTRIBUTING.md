# Testing

Run the solution checks with the .NET 10 SDK:

```sh
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release
dotnet test Naravel.slnx -c Release
```

## Provider integration tests

Start the local services with `docker compose up -d --wait`. Set the same service endpoints used by CI, then run the
solution tests so environment-gated provider cases execute:

```sh
NARAVEL_TEST_REDIS=localhost:6379 \
NARAVEL_TEST_RABBITMQ='amqp://naravel:naravel@localhost:5672/%2f' \
NARAVEL_TEST_KAFKA=localhost:9092 \
NARAVEL_TEST_MEMCACHED=localhost:11211 \
dotnet test Naravel.slnx -c Release
```

Stop the services with `docker compose down` when finished.

### One-command local run (same as CI)

`scripts/test-local.sh` (Linux/macOS) or `scripts/test-local.ps1` (Windows) starts the services, exports the
`NARAVEL_TEST_*` variables, then restores, builds and tests the whole solution, so a CI failure can be reproduced
before pushing:

```sh
scripts/test-local.sh                                   # services + all tests
scripts/test-local.sh --down                            # ...then stop the services
scripts/test-local.sh --no-services                     # fast loop, provider tests skipped
scripts/test-local.sh --db                              # also MySQL, PostgreSQL, SQL Server (about 2 GB RAM)
scripts/test-local.sh --db -- --filter "Dialect=Postgres"   # one database dialect only
scripts/test-local.sh -- --filter "FullyQualifiedName~Redis"
```

### Dev container

Open the repository in VS Code or Codespaces ("Reopen in Container") for a ready .NET 10 SDK with Docker access
(`.devcontainer/`). `global.json` pins the SDK feature band, so local and CI builds use the same compiler.