[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TempRoot
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-RequirementHooks.ps1') -TempRoot $TempRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
