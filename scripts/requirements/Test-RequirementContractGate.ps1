[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TempRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:ValidatorPath = Join-Path $PSScriptRoot "Validate-RequirementContract.ps1"
$script:PowerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:PassCount = 0
$script:TestCount = 0

function Assert-TestCondition {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw "TEST_ASSERTION_FAILED: $Message"
    }
}

function Write-Utf8File {
    param([string]$Path, [string]$Content)

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $parent -Force)
    }
    [System.IO.File]::WriteAllText($Path, $Content, $script:Utf8NoBom)
}

function Invoke-GitTest {
    param([string]$Repository, [string[]]$Arguments)

    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = @(& git -C $Repository @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorPreference
    }
    if ($exitCode -ne 0) {
        throw "Git failed in '$Repository': git $($Arguments -join ' '): $(($output | ForEach-Object { [string]$_ }) -join ' | ')"
    }
    return @($output | ForEach-Object { [string]$_ })
}

function Invoke-Gate {
    param([string[]]$Arguments)

    $engineArguments = @(
        "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
        "-File", $script:ValidatorPath
    ) + $Arguments
    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = @(& $script:PowerShell @engineArguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorPreference
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = (($output | ForEach-Object { [string]$_ }) -join "`n")
    }
}

function Assert-GatePass {
    param([string]$Name, [string[]]$Arguments)

    $script:TestCount++
    $result = Invoke-Gate $Arguments
    Assert-TestCondition ($result.ExitCode -eq 0) "$Name expected success but got exit $($result.ExitCode): $($result.Text)"
    Assert-TestCondition ($result.Text.Contains("REQUIREMENT_GATE_OK")) "$Name did not emit REQUIREMENT_GATE_OK: $($result.Text)"
    $script:PassCount++
    Write-Host "PASS $Name"
    return $result
}

function Assert-GateFail {
    param([string]$Name, [string[]]$Arguments, [string]$Code)

    $script:TestCount++
    $result = Invoke-Gate $Arguments
    Assert-TestCondition ($result.ExitCode -ne 0) "$Name expected failure but succeeded: $($result.Text)"
    Assert-TestCondition ($result.Text.Contains("REQUIREMENT_GATE_ERROR [$Code]")) "$Name expected error [$Code] but got: $($result.Text)"
    $script:PassCount++
    Write-Host "PASS $Name"
    return $result
}

function Assert-GateInProcessPass {
    param([string]$Name, [string]$Repository)

    $script:TestCount++
    $global:LASTEXITCODE = 128
    & $script:ValidatorPath -Mode WorkingTree -RepositoryRoot $Repository -AllowPendingEvidence
    $nativeExitCode = $LASTEXITCODE
    Assert-TestCondition ($? -and $nativeExitCode -eq 0) "$Name expected successful in-process invocation but LASTEXITCODE was $nativeExitCode."
    $script:PassCount++
    Write-Host "PASS $Name"
}

function New-TestRepository {
    param([string]$Root, [string]$Name)

    $repository = Join-Path $Root $Name
    [void](New-Item -ItemType Directory -Path $repository -Force)
    [void](Invoke-GitTest $repository @("init", "--quiet"))
    [void](Invoke-GitTest $repository @("config", "user.name", "Requirement Gate Tests"))
    [void](Invoke-GitTest $repository @("config", "user.email", "requirement-gate-tests@localhost"))
    [void](Invoke-GitTest $repository @("config", "core.autocrlf", "false"))
    Write-Utf8File (Join-Path $repository "src/app.txt") "baseline`n"
    Write-Utf8File (Join-Path $repository "Codex/Contexts/ContextPolyCopyTrader.md") "baseline context`n"
    [void](Invoke-GitTest $repository @("add", "--", "."))
    [void](Invoke-GitTest $repository @("commit", "--quiet", "-m", "baseline"))
    $baselineLines = @(Invoke-GitTest $repository @("rev-parse", "HEAD"))
    $baseline = $baselineLines[0]
    return [pscustomobject]@{ Path = $repository; Baseline = $baseline }
}

function New-ContractObject {
    param(
        [string]$ContractId,
        [string[]]$ImplementationPaths,
        [string]$RequestText = "Implement the exact requested change",
        [string]$SourceQuote = "exact requested change",
        [switch]$WithAssumption,
        [switch]$WithDeviation,
        [switch]$Bootstrap
    )

    $placeholderDigest = "sha256:" + ("0" * 64)
    $approvalStatus = if ($Bootstrap.IsPresent) { "bootstrap-approved" } else { "pending" }
    $assumptions = @()
    if ($WithAssumption.IsPresent) {
        $assumptions = @(
            [ordered]@{
                id = "ASM-001"
                text = "An assumption"
                impact = "Changes observable behavior"
                approval = [ordered]@{
                    status = "pending"
                    approvedBy = "user"
                    evidenceText = "pending approval"
                    semanticDigest = $placeholderDigest
                }
            }
        )
    }
    $deviations = @()
    if ($WithDeviation.IsPresent) {
        $deviations = @(
            [ordered]@{
                id = "DEV-001"
                text = "A proposed deviation"
                impact = "Changes observable behavior"
                approval = [ordered]@{
                    status = "pending"
                    approvedBy = "user"
                    evidenceText = "pending approval"
                    semanticDigest = $placeholderDigest
                }
            }
        )
    }
    return [ordered]@{
        schemaVersion = 1
        contractId = $ContractId
        title = "Requirement gate test contract"
        author = "agent:test-author"
        status = "draft"
        originalRequests = @(
            [ordered]@{ source = "user"; text = $RequestText }
        )
        scope = [ordered]@{
            goal = "Implement only the requested test change"
            inScope = @("Test fixture files")
            outOfScope = @("All other files")
            mode = "local-edit"
            periodOrFilter = "not-applicable"
            firstVerification = "Inspect the exact changed paths"
        }
        requirements = @(
            [ordered]@{
                id = "REQ-001"
                text = "Implement the exact requested change"
                sourceRequestIndexes = @(0)
                sourceQuote = $SourceQuote
                acceptanceCriteria = @("Every changed governed path is mapped")
                implementationPaths = @($ImplementationPaths)
                verification = @(
                    [ordered]@{
                        id = "VER-001"
                        kind = "test"
                        command = "Invoke the test assertion"
                        expected = "The assertion passes"
                        result = "pending"
                        evidence = ""
                    }
                )
            }
        )
        assumptions = $assumptions
        deviations = $deviations
        approval = [ordered]@{
            status = $approvalStatus
            approvedBy = "user"
            evidenceText = if ($Bootstrap.IsPresent) { $RequestText } else { "pending approval" }
            semanticDigest = $placeholderDigest
        }
        independentReview = [ordered]@{
            reviewer = "agent:test-reviewer"
            comparedOriginalRequests = $false
            verdict = "pending"
            findings = @()
        }
    }
}

function Get-ContractRelativePath {
    param([System.Collections.IDictionary]$Contract)

    return "Codex/Requirements/Contracts/$($Contract.contractId).json"
}

function Write-Contract {
    param([string]$Repository, [System.Collections.IDictionary]$Contract)

    $relativePath = Get-ContractRelativePath $Contract
    $json = $Contract | ConvertTo-Json -Depth 100
    Write-Utf8File (Join-Path $Repository ($relativePath.Replace('/', '\'))) ($json + "`n")
    return $relativePath
}

function Get-DraftDigest {
    param([string]$Repository, [System.Collections.IDictionary]$Contract)

    $relativePath = Write-Contract $Repository $Contract
    $result = Invoke-Gate @(
        "-Mode", "Contract", "-RepositoryRoot", $Repository,
        "-ContractPath", $relativePath, "-AllowDraft", "-PrintSemanticDigest"
    )
    Assert-TestCondition ($result.ExitCode -eq 0) "Digest calculation failed: $($result.Text)"
    $match = [System.Text.RegularExpressions.Regex]::Match($result.Text, 'sha256:[0-9a-f]{64}')
    Assert-TestCondition $match.Success "Digest calculation did not return SHA-256: $($result.Text)"
    return $match.Value
}

function Set-ContractApproved {
    param([System.Collections.IDictionary]$Contract, [string]$Digest, [switch]$Bootstrap)

    $Contract.status = "approved"
    $Contract.approval.status = if ($Bootstrap.IsPresent) { "bootstrap-approved" } else { "approved" }
    $Contract.approval.semanticDigest = $Digest
    $Contract.approval.evidenceText = if ($Bootstrap.IsPresent) {
        [string]$Contract.originalRequests[0].text
    }
    else {
        "APPROVE $($Contract.contractId) $Digest"
    }
    foreach ($item in @($Contract.assumptions) + @($Contract.deviations)) {
        $item.approval.status = "approved"
        $item.approval.approvedBy = "user"
        $item.approval.semanticDigest = $Digest
        $item.approval.evidenceText = "APPROVE $($Contract.contractId) $Digest"
    }
}

function Set-ContractCompleted {
    param([System.Collections.IDictionary]$Contract)

    $Contract.status = "completed"
    foreach ($requirement in @($Contract.requirements)) {
        foreach ($verification in @($requirement.verification)) {
            $verification.result = "passed"
            $verification.evidence = "Focused test passed"
        }
    }
    $Contract.independentReview.comparedOriginalRequests = $true
    $Contract.independentReview.verdict = "pass"
    $Contract.independentReview.findings = @()
}

function Add-ApprovedContractCommit {
    param([string]$Repository, [System.Collections.IDictionary]$Contract, [switch]$SkipGate)

    $digest = Get-DraftDigest $Repository $Contract
    Set-ContractApproved $Contract $digest
    $relativePath = Write-Contract $Repository $Contract
    [void](Invoke-GitTest $Repository @("add", "--", $relativePath))
    if (-not $SkipGate.IsPresent) {
        [void](Assert-GatePass "approval-only staged contract" @("-Mode", "Staged", "-RepositoryRoot", $Repository))
    }
    [void](Invoke-GitTest $Repository @("commit", "--quiet", "-m", "approve requirement contract"))
    $commitLines = @(Invoke-GitTest $Repository @("rev-parse", "HEAD"))
    return [pscustomobject]@{
        Digest = $digest
        Path = $relativePath
        Commit = $commitLines[0]
    }
}

function Add-CompletedImplementationCommit {
    param([string]$Repository, [System.Collections.IDictionary]$Contract, [string]$ChangedPath = "src/app.txt", [switch]$SkipGate)

    Write-Utf8File (Join-Path $Repository ($ChangedPath.Replace('/', '\'))) "implemented`n"
    Set-ContractCompleted $Contract
    $relativePath = Write-Contract $Repository $Contract
    [void](Invoke-GitTest $Repository @("add", "--", $ChangedPath, $relativePath))
    if (-not $SkipGate.IsPresent) {
        [void](Assert-GatePass "implementation staged transition" @("-Mode", "Staged", "-RepositoryRoot", $Repository))
    }
    [void](Invoke-GitTest $Repository @("commit", "--quiet", "-m", "implement approved requirement"))
    $commitLines = @(Invoke-GitTest $Repository @("rev-parse", "HEAD"))
    return $commitLines[0]
}

$resolvedTempRoot = [System.IO.Path]::GetFullPath($TempRoot).TrimEnd('\', '/')
Assert-TestCondition ($resolvedTempRoot.StartsWith("D:\CodexTemp\runs\", [System.StringComparison]::OrdinalIgnoreCase)) "TempRoot must be under D:\CodexTemp\runs."
Assert-TestCondition (Test-Path -LiteralPath $resolvedTempRoot -PathType Container) "TempRoot '$resolvedTempRoot' must already exist."
Assert-TestCondition (Test-Path -LiteralPath $script:ValidatorPath -PathType Leaf) "Validator '$script:ValidatorPath' is missing."

$testRoot = Join-Path $resolvedTempRoot ("requirement-gate-tests-" + [guid]::NewGuid().ToString("N"))
[void](New-Item -ItemType Directory -Path $testRoot)
$processTemp = Join-Path $testRoot "temp"
[void](New-Item -ItemType Directory -Path $processTemp)
$env:TEMP = $processTemp
$env:TMP = $processTemp
$env:TMPDIR = $processTemp

Write-Host "Requirement gate test root: $testRoot"

# Missing contracts fail closed for governed work.
$missing = New-TestRepository $testRoot "missing-contract"
Write-Utf8File (Join-Path $missing.Path "src/app.txt") "changed without contract`n"
[void](Assert-GateFail "governed change without contract" @("-Mode", "WorkingTree", "-RepositoryRoot", $missing.Path) "CONTRACT_REQUIRED")

# Full two-commit lifecycle, WorkingTree preflight, strict staged validation, and commit-by-commit range validation.
$lifecycle = New-TestRepository $testRoot "valid-lifecycle"
$contract = New-ContractObject "RC-20260813-valid-lifecycle" @("src/app.txt")
$contractPath = Write-Contract $lifecycle.Path $contract
[void](Assert-GatePass "draft contract digest" @("-Mode", "Contract", "-RepositoryRoot", $lifecycle.Path, "-ContractPath", $contractPath, "-AllowDraft", "-PrintSemanticDigest"))
$approval = Add-ApprovedContractCommit $lifecycle.Path $contract
Write-Utf8File (Join-Path $lifecycle.Path "src/app.txt") "implementation in progress`n"
[void](Assert-GatePass "working tree uses pre-approved contract" @("-Mode", "WorkingTree", "-RepositoryRoot", $lifecycle.Path, "-AllowPendingEvidence"))
# Force Git to emit an LF/CRLF conversion warning on stderr. Successful Git
# stderr must never be parsed as a changed path by the validator.
[void](Invoke-GitTest $lifecycle.Path @("config", "core.autocrlf", "true"))
[void](Assert-GatePass "working tree ignores successful Git stderr warnings" @("-Mode", "WorkingTree", "-RepositoryRoot", $lifecycle.Path, "-AllowPendingEvidence"))
[void](Assert-GateInProcessPass "successful in-process validation resets native exit code" $lifecycle.Path)
[void](Invoke-GitTest $lifecycle.Path @("config", "core.autocrlf", "false"))
[void](Assert-GateFail "strict working tree requires completion transition" @("-Mode", "WorkingTree", "-RepositoryRoot", $lifecycle.Path) "CONTRACT_REQUIRED")
$implementationCommit = Add-CompletedImplementationCommit $lifecycle.Path $contract
[void](Assert-GatePass "commit-by-commit range" @("-Mode", "Range", "-RepositoryRoot", $lifecycle.Path, "-BaseRef", $lifecycle.Baseline, "-HeadRef", $implementationCommit))

# A same-change normal contract cannot authorize its own implementation.
$sameChange = New-TestRepository $testRoot "same-change"
$sameContract = New-ContractObject "RC-20260813-same-change" @("src/app.txt")
$sameDigest = Get-DraftDigest $sameChange.Path $sameContract
Set-ContractApproved $sameContract $sameDigest
Set-ContractCompleted $sameContract
$samePath = Write-Contract $sameChange.Path $sameContract
Write-Utf8File (Join-Path $sameChange.Path "src/app.txt") "same-change implementation`n"
[void](Invoke-GitTest $sameChange.Path @("add", "--", "src/app.txt", $samePath))
[void](Assert-GateFail "same-change contract is not pre-approved" @("-Mode", "Staged", "-RepositoryRoot", $sameChange.Path) "CONTRACT_NOT_PREAPPROVED")
[void](Invoke-GitTest $sameChange.Path @("commit", "--quiet", "-m", "bypass local gate for range negative"))
$sameHead = @(Invoke-GitTest $sameChange.Path @("rev-parse", "HEAD"))[0]
[void](Assert-GateFail "range catches same-change bypass" @("-Mode", "Range", "-RepositoryRoot", $sameChange.Path, "-BaseRef", $sameChange.Baseline, "-HeadRef", $sameHead) "CONTRACT_NOT_PREAPPROVED")

# Every governed path must match an approved implementationPaths entry.
$uncovered = New-TestRepository $testRoot "uncovered-path"
$uncoveredContract = New-ContractObject "RC-20260813-uncovered-path" @("src/app.txt")
[void](Add-ApprovedContractCommit $uncovered.Path $uncoveredContract)
Set-ContractCompleted $uncoveredContract
Write-Utf8File (Join-Path $uncovered.Path "src/other.txt") "not mapped`n"
$uncoveredContractPath = Write-Contract $uncovered.Path $uncoveredContract
[void](Invoke-GitTest $uncovered.Path @("add", "--", "src/other.txt", $uncoveredContractPath))
[void](Assert-GateFail "uncovered governed path" @("-Mode", "Staged", "-RepositoryRoot", $uncovered.Path) "PATH_NOT_COVERED")

# Completion requires passing verification evidence.
$pending = New-TestRepository $testRoot "pending-verification"
$pendingContract = New-ContractObject "RC-20260813-pending-verification" @("src/app.txt")
[void](Add-ApprovedContractCommit $pending.Path $pendingContract)
$pendingContract.status = "completed"
$pendingContract.independentReview.comparedOriginalRequests = $true
$pendingContract.independentReview.verdict = "pass"
Write-Utf8File (Join-Path $pending.Path "src/app.txt") "implemented without verification`n"
$pendingPath = Write-Contract $pending.Path $pendingContract
[void](Invoke-GitTest $pending.Path @("add", "--", "src/app.txt", $pendingPath))
[void](Assert-GateFail "pending verification blocks completion" @("-Mode", "Staged", "-RepositoryRoot", $pending.Path) "VERIFICATION_RESULT")

# Assumptions need their own digest-bound user approval object.
$assumption = New-TestRepository $testRoot "assumption-approval"
$assumptionContract = New-ContractObject "RC-20260813-assumption-approval" @("src/app.txt") -WithAssumption
$assumptionDigest = Get-DraftDigest $assumption.Path $assumptionContract
Set-ContractApproved $assumptionContract $assumptionDigest
$assumptionContract.assumptions[0].approval.status = "pending"
$assumptionPath = Write-Contract $assumption.Path $assumptionContract
[void](Invoke-GitTest $assumption.Path @("add", "--", $assumptionPath))
[void](Assert-GateFail "unapproved assumption blocks approval commit" @("-Mode", "Staged", "-RepositoryRoot", $assumption.Path) "APPROVAL_STATUS")

$deviation = New-TestRepository $testRoot "deviation-approval"
$deviationContract = New-ContractObject "RC-20260813-deviation-approval" @("src/app.txt") -WithDeviation
$deviationDigest = Get-DraftDigest $deviation.Path $deviationContract
Set-ContractApproved $deviationContract $deviationDigest
$deviationContract.deviations[0].approval.status = "pending"
$deviationPath = Write-Contract $deviation.Path $deviationContract
[void](Invoke-GitTest $deviation.Path @("add", "--", $deviationPath))
[void](Assert-GateFail "unapproved deviation blocks approval commit" @("-Mode", "Staged", "-RepositoryRoot", $deviation.Path) "APPROVAL_STATUS")

# Source quotes are checked as exact substrings.
$badQuote = New-TestRepository $testRoot "bad-source-quote"
$badQuoteContract = New-ContractObject "RC-20260813-bad-source-quote" @("src/app.txt") -SourceQuote "not present verbatim"
$badQuotePath = Write-Contract $badQuote.Path $badQuoteContract
[void](Assert-GateFail "source quote must be verbatim" @("-Mode", "Contract", "-RepositoryRoot", $badQuote.Path, "-ContractPath", $badQuotePath, "-AllowDraft") "SOURCE_QUOTE")

# Only the active context and daily history are ordinary repository-file exemptions.
$contextOnly = New-TestRepository $testRoot "context-only"
Write-Utf8File (Join-Path $contextOnly.Path "Codex/Contexts/ContextPolyCopyTrader.md") "new context`n"
Write-Utf8File (Join-Path $contextOnly.Path "Codex/Contexts/History/ContextPolyCopyTrader-2026-08-13.md") "daily history`n"
[void](Invoke-GitTest $contextOnly.Path @("add", "--", "Codex/Contexts/ContextPolyCopyTrader.md", "Codex/Contexts/History/ContextPolyCopyTrader-2026-08-13.md"))
[void](Assert-GatePass "active context and history staged changes" @("-Mode", "Staged", "-RepositoryRoot", $contextOnly.Path))

$otherContext = New-TestRepository $testRoot "other-context-governed"
Write-Utf8File (Join-Path $otherContext.Path "Codex/Contexts/Other.md") "not an exempt context journal`n"
[void](Invoke-GitTest $otherContext.Path @("add", "--", "Codex/Contexts/Other.md"))
[void](Assert-GateFail "arbitrary context file remains governed" @("-Mode", "Staged", "-RepositoryRoot", $otherContext.Path) "CONTRACT_REQUIRED")

# Staged validation reads the index rather than a later working-tree edit.
$indexIsolation = New-TestRepository $testRoot "index-isolation"
$indexContract = New-ContractObject "RC-20260813-index-isolation" @("src/app.txt")
[void](Add-ApprovedContractCommit $indexIsolation.Path $indexContract)
Write-Utf8File (Join-Path $indexIsolation.Path "src/app.txt") "valid staged implementation`n"
Set-ContractCompleted $indexContract
$indexPath = Write-Contract $indexIsolation.Path $indexContract
[void](Invoke-GitTest $indexIsolation.Path @("add", "--", "src/app.txt", $indexPath))
$indexContract.status = "draft"
[void](Write-Contract $indexIsolation.Path $indexContract)
[void](Assert-GatePass "staged mode ignores unstaged contract mutation" @("-Mode", "Staged", "-RepositoryRoot", $indexIsolation.Path))

# Completed records are immutable.
$immutable = New-TestRepository $testRoot "completed-immutable"
$immutableContract = New-ContractObject "RC-20260813-completed-immutable" @("src/app.txt")
[void](Add-ApprovedContractCommit $immutable.Path $immutableContract)
[void](Add-CompletedImplementationCommit $immutable.Path $immutableContract)
$immutableContract.requirements[0].verification[0].evidence = "edited after completion"
$immutablePath = Write-Contract $immutable.Path $immutableContract
[void](Invoke-GitTest $immutable.Path @("add", "--", $immutablePath))
[void](Assert-GateFail "completed contract is immutable" @("-Mode", "Staged", "-RepositoryRoot", $immutable.Path) "CONTRACT_IMMUTABLE")

# The one-time bootstrap contract is the only same-change exception and needs an explicit switch.
$bootstrap = New-TestRepository $testRoot "bootstrap"
$bootstrapRequest = "Внедряй контрольные точки."
$bootstrapContract = New-ContractObject "RC-20260813-project-requirement-gates" @("scripts/gate.txt") -RequestText $bootstrapRequest -SourceQuote "контрольные точки" -Bootstrap
$bootstrapDigest = Get-DraftDigest $bootstrap.Path $bootstrapContract
Set-ContractApproved $bootstrapContract $bootstrapDigest -Bootstrap
Set-ContractCompleted $bootstrapContract
$bootstrapPath = Write-Contract $bootstrap.Path $bootstrapContract
Write-Utf8File (Join-Path $bootstrap.Path "scripts/gate.txt") "bootstrap gate`n"
[void](Invoke-GitTest $bootstrap.Path @("add", "--", "scripts/gate.txt", $bootstrapPath))
[void](Assert-GateFail "bootstrap requires explicit switch" @("-Mode", "Staged", "-RepositoryRoot", $bootstrap.Path) "CONTRACT_NOT_PREAPPROVED")
[void](Assert-GatePass "explicit bootstrap exception" @("-Mode", "Staged", "-RepositoryRoot", $bootstrap.Path, "-AllowBootstrapContract"))

Assert-TestCondition ($script:PassCount -eq $script:TestCount) "Only $($script:PassCount) of $($script:TestCount) assertions passed."
Write-Host "REQUIREMENT_GATE_TESTS_OK passed=$($script:PassCount) root=$testRoot"
