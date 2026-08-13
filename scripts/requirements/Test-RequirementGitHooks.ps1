[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TempRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0
if (Test-Path Variable:\PSNativeCommandUseErrorActionPreference) {
    # Expected negative hook/installer cases must be captured as process results,
    # not promoted to terminating PowerShell errors by the caller's preference.
    Set-Variable -Name PSNativeCommandUseErrorActionPreference -Value $false -Scope Script
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "ASSERTION_FAILED: $Message"
    }
}

function Invoke-Git {
    param([string]$Repository, [string[]]$Arguments)
    $output = @(& git -C $Repository @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$Repository':`n$($output -join "`n")"
    }
    return ($output -join "`n").Trim()
}

function Invoke-Hook {
    param(
        [string]$Shell,
        [string]$Repository,
        [string[]]$InputRecords
    )

    $hook = Join-Path $Repository ".githooks\pre-push"
    Push-Location $Repository
    $priorErrorActionPreference = $ErrorActionPreference
    $hadNativePreference = Test-Path variable:global:PSNativeCommandUseErrorActionPreference
    if ($hadNativePreference) { $priorNativePreference = $global:PSNativeCommandUseErrorActionPreference }
    try {
        $ErrorActionPreference = "Continue"
        if ($hadNativePreference) { $global:PSNativeCommandUseErrorActionPreference = $false }
        $output = @($InputRecords | & $Shell $hook origin unused 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorErrorActionPreference
        if ($hadNativePreference) { $global:PSNativeCommandUseErrorActionPreference = $priorNativePreference }
        Pop-Location
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join "`n")
    }
}

function Invoke-Installer {
    param([string]$Repository, [string]$Installer)
    Push-Location $Repository
    $priorErrorActionPreference = $ErrorActionPreference
    $hadNativePreference = Test-Path variable:global:PSNativeCommandUseErrorActionPreference
    if ($hadNativePreference) { $priorNativePreference = $global:PSNativeCommandUseErrorActionPreference }
    try {
        $ErrorActionPreference = "Continue"
        if ($hadNativePreference) { $global:PSNativeCommandUseErrorActionPreference = $false }
        $output = @(& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $Installer 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorErrorActionPreference
        if ($hadNativePreference) { $global:PSNativeCommandUseErrorActionPreference = $priorNativePreference }
        Pop-Location
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join "`n")
    }
}

$resolvedTempRoot = [System.IO.Path]::GetFullPath($TempRoot)
$allowedRoot = [System.IO.Path]::GetFullPath("D:\CodexTemp\runs")
if (-not $resolvedTempRoot.StartsWith($allowedRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "TempRoot must be below '$allowedRoot'."
}

$sourceRootLines = @(& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or $sourceRootLines.Count -ne 1) {
    throw "Run this test from inside the PolyCopyTrader repository."
}
$sourceRoot = ([string]$sourceRootLines[0]).Trim()

$gitCommand = Get-Command git.exe -ErrorAction Stop
$gitInstallRoot = Split-Path (Split-Path $gitCommand.Source -Parent) -Parent
$shell = Join-Path $gitInstallRoot "bin\sh.exe"
if (-not (Test-Path -LiteralPath $shell -PathType Leaf)) {
    throw "Git shell was not found at '$shell'."
}

$fixture = Join-Path $resolvedTempRoot ("requirement-git-hooks-" + [guid]::NewGuid().ToString("N"))
$repository = Join-Path $fixture "repo"
New-Item -ItemType Directory -Path $repository -Force | Out-Null

& git init --quiet --initial-branch=master $repository
if ($LASTEXITCODE -ne 0) { throw "Failed to initialize fixture repository." }
Invoke-Git $repository @("config", "user.name", "Requirement Gate Test") | Out-Null
Invoke-Git $repository @("config", "user.email", "requirement-gate@example.invalid") | Out-Null
Invoke-Git $repository @("config", "core.autocrlf", "false") | Out-Null

$directories = @(
    (Join-Path $repository ".githooks"),
    (Join-Path $repository "scripts\requirements")
)
foreach ($directory in $directories) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $sourceRoot ".githooks\pre-commit") -Destination (Join-Path $repository ".githooks\pre-commit")
Copy-Item -LiteralPath (Join-Path $sourceRoot ".githooks\pre-push") -Destination (Join-Path $repository ".githooks\pre-push")
Copy-Item -LiteralPath (Join-Path $sourceRoot "scripts\requirements\Validate-RequirementContract.ps1") -Destination (Join-Path $repository "scripts\requirements\Validate-RequirementContract.ps1")
Copy-Item -LiteralPath (Join-Path $sourceRoot "scripts\requirements\Install-RequirementGitHooks.ps1") -Destination (Join-Path $repository "scripts\requirements\Install-RequirementGitHooks.ps1")

Invoke-Git $repository @("commit", "--allow-empty", "-m", "base") | Out-Null
$base = Invoke-Git $repository @("rev-parse", "HEAD")
Invoke-Git $repository @("commit", "--allow-empty", "-m", "existing update") | Out-Null
$existingHead = Invoke-Git $repository @("rev-parse", "HEAD")
Invoke-Git $repository @("update-ref", "refs/remotes/origin/master", $base) | Out-Null
Invoke-Git $repository @("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/master") | Out-Null

Invoke-Git $repository @("checkout", "--quiet", "--detach", $base) | Out-Null
Invoke-Git $repository @("commit", "--allow-empty", "-m", "new branch") | Out-Null
$newHead = Invoke-Git $repository @("rev-parse", "HEAD")
Invoke-Git $repository @("checkout", "--quiet", "master") | Out-Null

$zero = "0" * 40
$passed = 0

$multi = Invoke-Hook $shell $repository @(
    "refs/heads/master $existingHead refs/heads/master $base",
    "refs/heads/new $newHead refs/heads/new $zero",
    "(delete) $zero refs/heads/old $existingHead"
)
Assert-True ($multi.ExitCode -eq 0) "multiple-ref validation should pass: $($multi.Output)"
Assert-True (([regex]::Matches($multi.Output, "mode=Range")).Count -eq 2) "exactly two non-delete ranges should reach the validator."
Assert-True ($multi.Output.Contains("skipping deleted ref 'refs/heads/old'")) "deleted refs should be explicitly skipped."
Assert-True ($multi.Output.Contains("$base..$existingHead")) "an existing ref should validate from its advertised remote object id."
Assert-True ($multi.Output.Contains("$base..$newHead")) "a new ref should validate from the fetched default branch."
$passed += 5

Invoke-Git $repository @("checkout", "--quiet", "--detach", $base) | Out-Null
Invoke-Git $repository @("commit", "--allow-empty", "-m", "divergent force") | Out-Null
$divergentHead = Invoke-Git $repository @("rev-parse", "HEAD")
Invoke-Git $repository @("checkout", "--quiet", "master") | Out-Null
$force = Invoke-Hook $shell $repository @(
    "refs/heads/master $divergentHead refs/heads/master $existingHead"
)
Assert-True ($force.ExitCode -ne 0) "a non-ancestor force push must fail."
Assert-True ($force.Output.Contains("force/non-ancestor pushes are blocked")) "force-push failure should explain the gate."
$passed += 2

Invoke-Git $repository @("symbolic-ref", "--delete", "refs/remotes/origin/HEAD") | Out-Null
$masterFallback = Invoke-Hook $shell $repository @(
    "refs/heads/new $newHead refs/heads/new $zero"
)
Assert-True ($masterFallback.ExitCode -eq 0) "a new ref should fall back to refs/remotes/origin/master: $($masterFallback.Output)"
Assert-True ($masterFallback.Output.Contains("$base..$newHead")) "master fallback should use the exact fetched master commit."
$passed += 2

Invoke-Git $repository @("update-ref", "-d", "refs/remotes/origin/master") | Out-Null
$noBase = Invoke-Hook $shell $repository @(
    "refs/heads/new $newHead refs/heads/new $zero"
)
Assert-True ($noBase.ExitCode -ne 0) "a new ref without a fetched default base must fail."
Assert-True ($noBase.Output.Contains("cannot resolve a trusted base")) "missing-base failure should be explicit."
$passed += 2

$malformed = Invoke-Hook $shell $repository @("refs/heads/master $existingHead refs/heads/master")
Assert-True ($malformed.ExitCode -ne 0) "malformed pre-push input must fail."
Assert-True ($malformed.Output.Contains("malformed pre-push input record")) "malformed-input failure should be explicit."
$passed += 2

$empty = Invoke-Hook $shell $repository @()
Assert-True ($empty.ExitCode -ne 0) "empty pre-push input must fail closed."
Assert-True ($empty.Output.Contains("no pre-push ref records")) "empty-input failure should be explicit."
$passed += 2

$installerPath = Join-Path $repository "scripts\requirements\Install-RequirementGitHooks.ps1"
$install = Invoke-Installer $repository $installerPath
Assert-True ($install.ExitCode -eq 0) "installer should succeed: $($install.Output)"
Assert-True ((Invoke-Git $repository @("config", "--local", "--get", "core.hooksPath")) -ceq ".githooks") "installer should set the exact local hooks path."
$passed += 2

$installAgain = Invoke-Installer $repository $installerPath
Assert-True ($installAgain.ExitCode -eq 0) "installer should be idempotent: $($installAgain.Output)"
$passed += 1

Invoke-Git $repository @("config", "--local", "core.hooksPath", "other-hooks") | Out-Null
$refusal = Invoke-Installer $repository $installerPath
Assert-True ($refusal.ExitCode -ne 0) "installer must refuse to replace a different hooks path."
Assert-True ($refusal.Output.Contains("Refusing to overwrite")) "installer refusal should be explicit."
Assert-True ((Invoke-Git $repository @("config", "--local", "--get", "core.hooksPath")) -ceq "other-hooks") "installer must preserve a different existing hooks path."
$passed += 3

$workflow = Get-Content -Raw (Join-Path $sourceRoot ".github\workflows\requirement-contract.yml")
Assert-True ($workflow.Contains("pull_request_target:")) "CI must execute the trusted default-branch workflow."
Assert-True ($workflow.Contains('ref: ${{ github.event.pull_request.base.sha }}')) "CI must check out the trusted base SHA."
Assert-True (-not $workflow.Contains('ref: ${{ github.event.pull_request.head.sha }}')) "CI must not check out the untrusted candidate SHA."
Assert-True ($workflow.Contains("-Mode Range")) "CI must validate the exact commit range."
Assert-True (-not $workflow.Contains("AllowBootstrapContract")) "post-bootstrap CI must not enable the one-time bootstrap exception."
Assert-True ($workflow.Contains("allow_squash_merge")) "CI must fail closed while GitHub squash merging is enabled."
Assert-True ($workflow.Contains("It cannot centrally validate its own bootstrap merge.")) "CI bootstrap limitation must be stated in the workflow."
$passed += 7

$attributes = Get-Content -Raw (Join-Path $sourceRoot ".gitattributes")
Assert-True ($attributes -match '(?m)^\.githooks/\*\s+text\s+eol=lf\s*$') "Git hook scripts must be forced to LF in every checkout."
$hookEolRows = @(& git -C $sourceRoot ls-files --eol -- ".githooks/pre-commit" ".githooks/pre-push")
Assert-True ($LASTEXITCODE -eq 0 -and $hookEolRows.Count -eq 2) "Git must report both tracked hook scripts."
Assert-True (@($hookEolRows | Where-Object { [string]$_ -notmatch 'attr/text eol=lf' }).Count -eq 0) "Both Git hooks must carry the eol=lf attribute."
$passed += 3

Write-Host "REQUIREMENT_GIT_HOOK_TESTS_OK passed=$passed root=$fixture"
