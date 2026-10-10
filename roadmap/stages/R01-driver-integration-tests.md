# R01 — Driver integration tests, CI services, Redis/Kafka fixes, benchmarks

Parity section: `R01` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: none (OD-01 settled the dependency question).
Read: `src/Naravel.Queue*/**`, `src/Naravel.Cache*/**` (provider classes only), `tests/Naravel.Queue.Tests/DriverTests.cs`, `tests/Naravel.Cache.Tests/{CacheStoreTests,RedisProviderTests,MemcachedProviderTests,ProviderProxySupport}.cs`, `Directory.Packages.props`, `.github/workflows/ci.yml`, `roadmap/AUDIT.md` rows D-12, D-14, D-17.
Touch: new `tests/Naravel.Queue.Providers.Tests/**`, `tests/Naravel.Cache.Tests/**`, `benchmarks/Naravel.Benchmarks/**`, provider driver classes, `Directory.Packages.props`, `Directory.Build.props`, `Naravel.slnx`, `.github/workflows/ci.yml`, `samples/Naravel.Sample/**`, `CHANGELOG.md`, `roadmap/{AUDIT,PARITY-MATRIX}.md`, `docs/en|fa/{queue,cache,benchmarks,laravel-parity}.md`, `ROADMAP.md`.
Out of scope: new features (R07/R04); shared Redis module (R03); RabbitMQ async rewrite (R07.T05).
Decisions: OD-01 (env-gated tests, service containers, SQLite, BenchmarkDotNet).

## Tasks
- [x] **R01.T01 — Driver contract suite:** shared Queue and Cache contracts cover operations, order, priority, delay, ack, release, failure, size, isolation and concurrent consumers; Memory, File, SQLite, Redis, RabbitMQ and Kafka use the queue base. **Accept:** local suite passed for Memory/File/SQLite/Redis; RabbitMQ/Kafka are wired to the CI service job.
- [x] **R01.T02 — Env-gated fixtures:** shared `[ServiceFact]` skips when its service variable is empty; Redis/RabbitMQ/Kafka/Memcached use unique names and cleanup. **Accept:** missing services are reported skipped, never failed.
- [x] **R01.T03 — Provider tests:** provider project includes Redis, RabbitMQ, Kafka and always-on SQLite; Cache has Redis/Memcached contracts. SQLite and Redis passed locally; remaining live services are configured in CI. **Accept:** SQLite and Redis contract tests pass; external providers are env-gated and wired to CI.
- [x] **R01.T04 — CI (D-17):** fast Ubuntu/Windows/macOS matrix and Ubuntu service job added; package audit and SDK SourceLink settings added. **Accept:** YAML parsed; source build has zero warnings; package audit is clean. Service image startup remains for CI execution.
- [x] **R01.T05 — Reproduce then fix drivers (D-12, D-14):** Redis state changes use Lua; Kafka consumer operations are serialized and commits advance only through contiguous acknowledged offsets. Unit regressions cover script use and out-of-order acks. **Accept:** live Redis/SQLite contracts, Redis script assertions and Kafka tracker tests pass; Kafka live service remains CI-gated. Memory/File code is untouched.
- [x] **R01.T06 — Benchmarks (OD-06):** non-packable BenchmarkDotNet project runs five queue/cache/serializer workloads; bilingual baseline tables are recorded. **Accept:** full `dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*'` run completed and values are documented in EN/FA.
- [x] **R01.T07 — Validate:** full restore/build/test passed (totals are in the R01 row of ROADMAP.md); `check.py` passed. Local services exercised: Redis and SQLite; RabbitMQ, Kafka and Memcached live tests are configured in CI. **Accept:** roadmap status names local and CI-only services accurately.

## Exit evidence
`DONE (date, counts, services exercised)`; `NEXT` → R07.
