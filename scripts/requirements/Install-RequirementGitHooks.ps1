[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$rootLines = @(& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or $rootLines.Count -ne 1 -or
    [string]::IsNullOrWhiteSpace([string]$rootLines[0])) {
    throw "Run this script from inside the PolyCopyTrader Git repository."
}

$root = ([string]$rootLines[0]).Trim()
$required = @(
    (Join-Path $root ".githooks\pre-commit"),
    (Join-Path $root ".githooks\pre-push"),
    (Join-Path $root "scripts\requirements\Validate-RequirementContract.ps1")
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required gate file is missing: $path"
    }
    if ((Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Required gate file is empty: $path"
    }
}

$existingLines = @(& git -C $root config --local --get core.hooksPath 2>$null)
$existingExitCode = $LASTEXITCODE
if ($existingExitCode -eq 0) {
    if ($existingLines.Count -ne 1) {
        throw "core.hooksPath has multiple local values; refusing to replace them."
    }
    $existing = ([string]$existingLines[0]).Trim()
    if ($existing -cne ".githooks") {
        throw "core.hooksPath is already '$existing'. Refusing to overwrite it."
    }
}
elseif ($existingExitCode -ne 1) {
    throw "Failed to inspect the local core.hooksPath setting (git exit $existingExitCode)."
}

& git -C $root config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    throw "Failed to set core.hooksPath."
}

$actualLines = @(& git -C $root config --local --get core.hooksPath 2>$null)
if ($LASTEXITCODE -ne 0 -or $actualLines.Count -ne 1 -or
    ([string]$actualLines[0]).Trim() -cne ".githooks") {
    $actual = $actualLines -join ", "
    throw "Hook installation verification failed: $actual"
}

$originLines = @(& git -C $root config --show-origin --get core.hooksPath 2>$null)
if ($LASTEXITCODE -ne 0 -or $originLines.Count -ne 1) {
    throw "Hook installation origin verification failed."
}

Write-Host "Requirement Git hooks installed for this clone (including linked worktrees)."
Write-Host ([string]$originLines[0])
Write-Warning "Hooks are a guardrail and can be bypassed with --no-verify. Keep the requirement-contract CI check required in branch protection."
