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