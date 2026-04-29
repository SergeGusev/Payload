param(
    [string]$ServiceName = "PolyCopyTrader.Service"
)

$ErrorActionPreference = "Stop"

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
