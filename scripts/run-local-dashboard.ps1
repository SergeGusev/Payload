[CmdletBinding()]
param(
    [int] $PostgresPort = 54328,
    [switch] $NoPostgres
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")

if (-not $NoPostgres) {
    & (Join-Path $PSScriptRoot "start-local-postgres.ps1") -Port $PostgresPort
}

$env:POLYCOPYTRADER_POSTGRES_CONNECTION = "Host=127.0.0.1;Port=$PostgresPort;Database=polycopytrader;Username=polycopytrader;Password=polycopytrader_local_password;SSL Mode=Disable;Include Error Detail=true"
$env:Bot__Mode = "Paper"
$env:Bot__EnableLiveTrading = "false"
$env:PolymarketAuth__Enabled = "false"
$env:PolymarketAuth__DryRunSigningEnabled = "false"
$env:LiveTrading__ManualEnableCode = ""

dotnet run --project (Join-Path $root "src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj")
