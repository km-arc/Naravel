<#
Run the whole solution locally, including the env-gated provider tests, against the service
containers from docker-compose.yml (same endpoints as CI). Windows counterpart of test-local.sh.

  scripts/test-local.ps1                  # start services, run all tests, keep services up
  scripts/test-local.ps1 -Down            # ...and stop the services afterwards
  scripts/test-local.ps1 -NoServices      # fast loop: skip Docker, provider tests are skipped
  scripts/test-local.ps1 -Db              # also start MySQL, PostgreSQL and SQL Server (about 2 GB RAM) for the database contract tests
  scripts/test-local.ps1 -TestArgs '--filter','FullyQualifiedName~Redis'
#>
param(
    [switch]$Down,
    [switch]$NoServices,
    [switch]$Db,
    [string[]]$TestArgs = @()
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet SDK not found (need .NET 10, see global.json)' }

if (-not $NoServices) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'docker not found; use -NoServices to skip provider tests' }
    if ($Db) { docker compose --profile db up -d --wait } else { docker compose up -d --wait }
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed' }
    $env:NARAVEL_TEST_REDIS = 'localhost:6379'
    $env:NARAVEL_TEST_RABBITMQ = 'amqp://naravel:naravel@localhost:5672/%2f'
    $env:NARAVEL_TEST_KAFKA = 'localhost:9092'
    $env:NARAVEL_TEST_MEMCACHED = 'localhost:11211'
    if ($Db) {
        # Same connection strings as the `database` job in .github/workflows/ci.yml.
        $env:NARAVEL_TEST_MYSQL = 'Server=127.0.0.1;Port=3306;Database=naravel;User ID=root;Password=naravel;AllowPublicKeyRetrieval=true'
        $env:NARAVEL_TEST_POSTGRES = 'Host=127.0.0.1;Port=5432;Database=naravel;Username=postgres;Password=naravel'
        $env:NARAVEL_TEST_SQLSERVER = 'Server=127.0.0.1,1433;Database=master;User Id=sa;Password=Naravel_Passw0rd!;TrustServerCertificate=true;Encrypt=false'
    }
}

dotnet restore Naravel.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build Naravel.slnx -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test Naravel.slnx -c Release --no-build @TestArgs
$status = $LASTEXITCODE

if ($Down -and -not $NoServices) { docker compose --profile db down }
exit $status
