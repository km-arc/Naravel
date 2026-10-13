#!/usr/bin/env bash
# Run the whole solution locally, including the env-gated provider tests, against the same
# service containers CI uses (docker-compose.yml). Reproduce CI failures here before pushing.
#
#   scripts/test-local.sh                 # start services, run all tests, keep services up
#   scripts/test-local.sh --down          # ...and stop the services afterwards
#   scripts/test-local.sh --no-services   # fast loop: skip Docker, provider tests are skipped
#   scripts/test-local.sh -- --filter "FullyQualifiedName~Redis"   # extra args go to `dotnet test`
set -euo pipefail
cd "$(dirname "$0")/.."

use_services=1
tear_down=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --down) tear_down=1; shift ;;
    --no-services) use_services=0; shift ;;
    --) shift; break ;;
    *) echo "unknown option: $1 (use -- to pass arguments to dotnet test)" >&2; exit 2 ;;
  esac
done

command -v dotnet >/dev/null || { echo "dotnet SDK not found (need .NET 10, see global.json)" >&2; exit 1; }

if [[ $use_services -eq 1 ]]; then
  command -v docker >/dev/null || { echo "docker not found; use --no-services to skip provider tests" >&2; exit 1; }
  docker compose up -d --wait
  # Same endpoints as .github/workflows/ci.yml (job "services").
  export NARAVEL_TEST_REDIS=localhost:6379
  export NARAVEL_TEST_RABBITMQ='amqp://naravel:naravel@localhost:5672/%2f'
  export NARAVEL_TEST_KAFKA=localhost:9092
  export NARAVEL_TEST_MEMCACHED=localhost:11211
fi

status=0
dotnet restore Naravel.slnx
dotnet build Naravel.slnx -c Release --no-restore
dotnet test Naravel.slnx -c Release --no-build "$@" || status=$?

if [[ $tear_down -eq 1 && $use_services -eq 1 ]]; then
  docker compose down
fi
exit $status
