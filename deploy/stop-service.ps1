param(
    [string]$ServiceName = "PolyCopyTrader.Service",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if ($Force) {
    Stop-Service -Name $ServiceName -Force
}
else {
    Stop-Service -Name $ServiceName
}

Get-Service -Name $ServiceName
