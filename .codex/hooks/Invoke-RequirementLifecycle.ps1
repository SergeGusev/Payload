[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RequirementGate.Common.ps1')

try {
    $inputObject = Read-RequirementHookInput
    $eventName = [string]$inputObject.hook_event_name
    $root = Get-RequirementRepositoryRoot -HookInput $inputObject
    $context = 'Requirement gate: read Codex/Rules/RequirementGate.md. Before any repository mutation, capture the user prompt verbatim in a task contract. Never add assumptions or deviations without separate user approval of the exact semantic digest.'

    if ($eventName -eq 'SessionStart') {
        [ordered]@{
            continue = $true
            hookSpecificOutput = [ordered]@{
                hookEventName    = 'SessionStart'
                additionalContext = $context
            }
        } | ConvertTo-Json -Depth 10 -Compress
        exit 0
    }

    if ($eventName -eq 'SubagentStart') {
        [ordered]@{
            systemMessage = 'Subagent requirement-fidelity context injected.'
            hookSpecificOutput = [ordered]@{
                hookEventName    = 'SubagentStart'
                additionalContext = $context + ' A subagent may not broaden scope or approve a contract. Report exact requirement-to-evidence findings to the parent.'
            }
        } | ConvertTo-Json -Depth 10 -Compress
        exit 0
    }

    if ($eventName -ne 'UserPromptSubmit') {
        exit 0
    }

    $sessionId = [string]$inputObject.session_id
    $turnId = [string]$inputObject.turn_id
    $prompt = [string]$inputObject.prompt
    if ([string]::IsNullOrWhiteSpace($sessionId) -or [string]::IsNullOrWhiteSpace($turnId)) {
        throw 'UserPromptSubmit input is missing session_id or turn_id.'
    }

    $contracts = @(Get-RequirementContracts -RepositoryRoot $root)
    $approvalMatches = @($contracts | Where-Object {
        $headContract = Get-RequirementContractAtHead -RepositoryRoot $root -RelativePath $_.Relative
        $null -ne $headContract -and (Test-ApprovedRequirementContract -Contract $headContract -Prompt $prompt)
    })
    $captureMatches = @($contracts | Where-Object {
        $digest = Get-RequirementContractDigest -Contract $_.Contract
        [string]$_.Contract.status -eq 'draft' -and
        [string]$_.Contract.approval.status -eq 'pending' -and
        $prompt -ceq ("APPROVE {0} {1}" -f [string]$_.Contract.contractId, $digest)
    })
    $originalMatches = @($contracts | Where-Object {
        @($_.Contract.originalRequests | Where-Object { [string]$_.text -ceq $prompt }).Count -gt 0
    })

    $state = 'contract-required'
    $contractPath = $null
    $contractId = $null
    $semanticDigest = $null
    $message = 'This exact prompt has no separately approved requirement contract. Only the task contract and context/history files may be edited. Create or update the contract, present its semantic digest, and stop for user approval.'

    if ($approvalMatches.Count -eq 1) {
        $match = $approvalMatches[0]
        $headContract = Get-RequirementContractAtHead -RepositoryRoot $root -RelativePath $match.Relative
        $state = 'approved'
        $contractPath = [string]$match.Relative
        $contractId = [string]$headContract.contractId
        $semanticDigest = Get-RequirementContractDigest -Contract $headContract
        $message = "Approved requirement contract $contractId is active for this exact turn. Implement only its mapped paths and requirements; semantic changes require a new digest and approval."
    }
    elseif ($approvalMatches.Count -gt 1) {
        $message = 'More than one approved contract uses this exact approval evidence. Ambiguous authorization is fail-closed; only contract and context/history edits are allowed.'
    }
    elseif ($captureMatches.Count -eq 1) {
        $match = $captureMatches[0]
        $state = 'approval-capture'
        $contractPath = [string]$match.Relative
        $contractId = [string]$match.Contract.contractId
        $semanticDigest = Get-RequirementContractDigest -Contract $match.Contract
        $message = "Exact approval captured for $contractId. Only that contract may be updated and committed. Product edits remain blocked until HEAD contains the approved contract with this evidence and digest."
    }
    elseif ($captureMatches.Count -gt 1) {
        $message = 'The approval prompt matches more than one draft contract. Ambiguous authorization is fail-closed; resolve the contracts before any product edit.'
    }
    elseif ($originalMatches.Count -eq 1) {
        $contractPath = [string]$originalMatches[0].Relative
        $contractId = [string]$originalMatches[0].Contract.contractId
        $message = "Prompt is captured by contract $contractId, but this is not a separate approval turn. Only contract and context/history edits are allowed. Present the digest and obtain exact user approval."
    }

    $receiptPath = Get-RequirementReceiptPath -RepositoryRoot $root -SessionId $sessionId -TurnId $turnId -CreateDirectory
    $receipt = [ordered]@{
        schemaVersion    = 1
        sessionId        = $sessionId
        turnId           = $turnId
        promptSha256     = Get-Sha256Text -Text $prompt
        state            = $state
        contractPath     = $contractPath
        contractId       = $contractId
        semanticDigest   = $semanticDigest
        mutationObserved = $false
        lastMutationTool = $null
        lastMutationAtUtc = $null
        createdAtUtc     = [DateTime]::UtcNow.ToString('o')
    }
    Write-RequirementReceipt -Path $receiptPath -Receipt $receipt

    [ordered]@{
        continue = $true
        systemMessage = $message
        hookSpecificOutput = [ordered]@{
            hookEventName    = 'UserPromptSubmit'
            additionalContext = $context + ' ' + $message
        }
    } | ConvertTo-Json -Depth 10 -Compress
}
catch {
    $reason = "Requirement lifecycle hook failed closed: $($_.Exception.Message)"
    [ordered]@{ continue = $false; stopReason = $reason; systemMessage = $reason } | ConvertTo-Json -Compress
}
