[CmdletBinding()]
param(
    [switch] $DeleteData
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$composeFile = Join-Path $root "docker-compose.local.yml"

if ($DeleteData) {
    docker compose -f $composeFile down -v
}
else {
    docker compose -f $composeFile down
}

if ($LASTEXITCODE -ne 0) {
    throw "docker compose failed to stop local PostgreSQL."
}
