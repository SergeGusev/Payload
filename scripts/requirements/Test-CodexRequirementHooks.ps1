[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TempRoot
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryRoot)) {
    throw "Run this test from inside the PolyCopyTrader repository."
}

$testScript = Join-Path $repositoryRoot.Trim() ".codex\hooks\Test-RequirementHooks.ps1"
if (-not (Test-Path -LiteralPath $testScript -PathType Leaf)) {
    throw "Codex requirement hook test is missing: $testScript"
}

& $testScript -TempRoot $TempRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
