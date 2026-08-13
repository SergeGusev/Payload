[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RequirementGate.Common.ps1')
$inputObject = $null

function Write-StopResult {
    param(
        [bool]$Continue = $true,
        [string]$Decision,
        [string]$Reason,
        [string]$SystemMessage
    )

    $result = [ordered]@{ continue = $Continue }
    if ($Decision) { $result.decision = $Decision }
    if ($Reason) { $result.reason = $Reason }
    if ($SystemMessage) { $result.systemMessage = $SystemMessage }
    $result | ConvertTo-Json -Compress
}

try {
    $inputObject = Read-RequirementHookInput
    $root = Get-RequirementRepositoryRoot -HookInput $inputObject
    $receiptPath = Get-RequirementReceiptPath -RepositoryRoot $root -SessionId ([string]$inputObject.session_id) -TurnId ([string]$inputObject.turn_id)
    $receipt = Read-RequirementReceipt -Path $receiptPath
    if ($null -ne $receipt -and [string]$receipt.state -in @('approval-capture', 'approved') -and $receipt.contractPath) {
        $headContract = Get-RequirementContractAtHead -RepositoryRoot $root -RelativePath ([string]$receipt.contractPath)
        if ($null -ne $headContract -and (Test-ContractMatchesReceiptApproval -Contract $headContract -Receipt $receipt)) {
            $receipt.state = 'approved'
            Write-RequirementReceipt -Path $receiptPath -Receipt $receipt
        }
    }
    if ($null -eq $receipt -or -not [bool]$receipt.mutationObserved) {
        Write-StopResult
        exit 0
    }

    if ([bool]$inputObject.stop_hook_active) {
        Write-StopResult -SystemMessage 'Requirement gate already requested one continuation in this turn; it will not loop. Any remaining validation failure must be reported explicitly to the user.'
        exit 0
    }

    if ([string]$receipt.state -ne 'approved') {
        Write-StopResult -Decision block -Reason 'Checkpoint B is still required. Continue once only to present the task contract and semantic digest, then stop and ask the user for exact approval. Do not claim implementation completion and do not edit product files.'
        exit 0
    }

    $validator = Join-Path $root 'scripts\requirements\Validate-RequirementContract.ps1'
    if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
        Write-StopResult -Decision block -Reason 'The approved turn made repository changes, but the WorkingTree requirement validator is unavailable. Continue once to report this validation blocker; do not claim completion.'
        exit 0
    }

    $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -Mode WorkingTree 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $tail = ($output | Select-Object -Last 12) -join "`n"
        Write-StopResult -Decision block -Reason ("Requirement validation failed. Continue once to fix only within the approved contract or report the blocker. Validator tail:`n" + $tail)
        exit 0
    }

    Write-StopResult -SystemMessage ('Requirement WorkingTree validation passed for ' + [string]$receipt.contractId + '.')
}
catch {
    if ($null -ne $inputObject -and [bool]$inputObject.stop_hook_active) {
        Write-StopResult -SystemMessage ("Requirement Stop hook still fails after one continuation; no loop will be created. Report: $($_.Exception.Message)")
    }
    else {
        Write-StopResult -Decision block -Reason ("Requirement Stop hook failed closed. Continue once to report or resolve: $($_.Exception.Message)")
    }
}
