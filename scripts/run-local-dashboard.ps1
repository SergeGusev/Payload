[CmdletBinding()]
param(
    [int] $PostgresPort = 54328,
    [string] $ConnectionString = "",
    [switch] $NoPostgres
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")

if (-not $NoPostgres) {
    & (Join-Path $PSScriptRoot "start-local-postgres.ps1") -Port $PostgresPort
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = $env:POLYCOPYTRADER_POSTGRES_CONNECTION
}

if ([string]::IsNullOrWhiteSpace($ConnectionString) -and -not $NoPostgres) {
    $ConnectionString = "Host=127.0.0.1;Port=$PostgresPort;Database=polycopytrader;Username=polycopytrader;Password=polycopytrader_local_password;SSL Mode=Disable;Include Error Detail=true"
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Set POLYCOPYTRADER_POSTGRES_CONNECTION or pass -ConnectionString when using -NoPostgres."
}

$env:POLYCOPYTRADER_POSTGRES_CONNECTION = $ConnectionString
$env:Bot__Mode = "Paper"
$env:Bot__EnableLiveTrading = "false"
$env:PolymarketAuth__Enabled = "false"
$env:PolymarketAuth__DryRunSigningEnabled = "false"
$env:LiveTrading__ManualEnableCode = ""

dotnet run --project (Join-Path $root "src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj")
