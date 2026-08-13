[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RequirementGate.Common.ps1')

function Test-ReadOnlyShellCommand {
    param([AllowEmptyString()][string]$Command)

    if ([string]::IsNullOrWhiteSpace($Command)) { return $false }
    $text = $Command.Trim()

    # Fail closed on composition, redirection, invocation, substitution, and static method syntax.
    if ($text -match '[\r\n;&|><`()$]' -or $text -match '@\(|::') { return $false }
    if ($text -match '(?i)\b(powershell|pwsh|cmd(?:\.exe)?|bash|sh|wsl|Invoke-Expression|Start-Process)\b') { return $false }
    $gitPrefix = '(?i)^git(?:\s+-C\s+(?:"[^"]+"|''[^'']+''|[^\s]+))?\s+'
    if ($text -match $gitPrefix) {
        # Presentation commands are not intrinsically side-effect free: --output
        # writes a file, --help may launch a viewer, and diff/textconv helpers can
        # execute repository-configured programs. Reject risky switches (including
        # common long-option abbreviations) before applying the read allowlists.
        if ($text -match '(?i)(?:^|\s)(?:--out[^\s]*|--ext[^\s]*|--text[^\s]*|--exec-path[^\s]*|--config-env[^\s]*|--help|-h)(?:\s|=|$)') { return $false }

        # `git cat-file --filters` can invoke repository-configured clean/smudge
        # filters. Reject every long `--f...` cat-file switch as a fail-closed
        # guard against both the full option and Git-supported abbreviations.
        $gitCatFileFilterPattern = '(?i)^git(?:\s+-C\s+(?:"[^"]+"|''[^'']+''|[^\s]+))?\s+cat-file(?=\s|$).*?(?:^|\s)--f[^\s]*(?:\s|=|$)'
        if ($text -match $gitCatFileFilterPattern) { return $false }

        $gitReadPattern = $gitPrefix + '(?:status|rev-parse|ls-files|ls-tree|cat-file|describe|name-rev)(?:\s+[^\r\n;&|><`]*)?$'
        if ($text -match $gitReadPattern) { return $true }

        # Disable both external diff mechanisms explicitly. Omitting their
        # positive switches is insufficient when attributes/configuration exist.
        $gitDiffPattern = $gitPrefix + '(?:diff|show|log)(?=\s|$)(?=.*(?:^|\s)--no-ext-diff(?:\s|$))(?=.*(?:^|\s)--no-textconv(?:\s|$))(?:\s+[^\r\n;&|><`]*)?$'
        if ($text -match $gitDiffPattern) { return $true }
    }
    if ($text -match '(?i)^git(?:\s+-C\s+(?:"[^"]+"|''[^'']+''|[^\s]+))?\s+branch\s+--show-current\s*$') { return $true }
    if ($text -match '(?i)^git(?:\s+-C\s+(?:"[^"]+"|''[^'']+''|[^\s]+))?\s+remote\s+-v\s*$') { return $true }

    $powerShellReadPattern = '(?i)^(?:Get-Content|Get-ChildItem|Get-Item|Get-Command|Get-Date|Get-Process|Get-Service|Select-String|Test-Path|Resolve-Path)(?:\s+[^\r\n;&|><`]*)?$'
    if ($text -match $powerShellReadPattern) { return $true }
    if ($text -match '(?i)^(?:rg|rg\.exe)(?:\s+[^\r\n;&|><`]*)?$') {
        # ripgrep can execute external commands through --pre and
        # --hostname-bin, including when those switches arrive from its config.
        # Require --no-config and reject every `--p...` / `--h...` long option
        # so supported abbreviations cannot bypass the gate.
        if ($text -notmatch '(?i)(?:^|\s)--no-config(?:\s|$)') { return $false }
        if ($text -match '(?i)(?:^|\s)--[ph][^\s]*(?:\s|=|$)') { return $false }
        return $true
    }
    if ($text -match '(?i)^where\.exe(?:\s+[^\r\n;&|><`]*)?$') { return $true }
    if ($text -match '(?i)^(?:pwd|Get-Location)\s*$') { return $true }

    return $false
}

function Test-NonRepositoryToolAllowed {
    param([Parameter(Mandatory)][string]$ToolName)

    return $ToolName -in @(
        'Agent',
        'update_plan',
        'request_user_input',
        'view_image',
        'get_goal',
        'list_agents',
        'wait_agent',
        'send_message',
        'followup_task',
        'interrupt_agent',
        'wait',
        'list_mcp_resources',
        'list_mcp_resource_templates',
        'read_mcp_resource',
        'codex_app__load_workspace_dependencies',
        'codex_app__read_thread_terminal'
    )
}

function Get-PatchTargets {
    param(
        [Parameter(Mandatory)][string]$Patch,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    $targets = @()
    $matches = [regex]::Matches($Patch, '(?m)^\*\*\* (?:Add|Update|Delete) File:\s*(.+?)\s*$|^\*\*\* Move to:\s*(.+?)\s*$')
    foreach ($match in $matches) {
        $rawPath = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
        $targets += ConvertTo-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -WorkingDirectory $WorkingDirectory -Path $rawPath
    }
    return @($targets | Select-Object -Unique)
}

function Test-ApprovalCaptureShellCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)]$Receipt
    )

    $contractPath = ([string]$Receipt.contractPath).Replace('\', '/')
    $escapedPath = [regex]::Escape($contractPath)
    $addPattern = '(?i)^\s*git\s+add\s+(?:--\s+)?[''"]?' + $escapedPath + '[''"]?\s*$'
    if ($Command -match $addPattern) {
        $workingContractPath = Join-Path $RepositoryRoot $contractPath
        if (-not (Test-Path -LiteralPath $workingContractPath -PathType Leaf)) { return $false }
        $workingContract = Get-Content -LiteralPath $workingContractPath -Raw -Encoding utf8 | ConvertFrom-Json -ErrorAction Stop
        return Test-ContractMatchesReceiptApproval -Contract $workingContract -Receipt $Receipt
    }

    if ($Command -notmatch '(?i)^\s*git\s+commit\s+-m\s+(?:"[^"]+"|''[^'']+'')\s*$') {
        return $false
    }

    $stagedPaths = @(& git -C $RepositoryRoot diff --cached --name-only --diff-filter=ACMR 2>$null | ForEach-Object { ([string]$_).Replace('\', '/') })
    if ($LASTEXITCODE -ne 0 -or $stagedPaths.Count -ne 1 -or $stagedPaths[0] -cne $contractPath) {
        return $false
    }
    $stagedJson = @(& git -C $RepositoryRoot show (":" + $contractPath) 2>$null) -join "`n"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($stagedJson)) { return $false }
    $stagedContract = $stagedJson | ConvertFrom-Json -ErrorAction Stop
    return Test-ContractMatchesReceiptApproval -Contract $stagedContract -Receipt $Receipt
}

function Test-ContractPathAllowed {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Contract
    )

    if (Test-DraftAllowedPath -Path $Path) { return $true }
    foreach ($pattern in @($Contract.requirements | ForEach-Object { @($_.implementationPaths) })) {
        if (Test-RequirementPathMatch -Path $Path -Pattern ([string]$pattern)) { return $true }
    }
    return $false
}

function Test-ApprovedShellCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)]$Contract
    )

    $trimmed = $Command.Trim()
    foreach ($verification in @($Contract.requirements | ForEach-Object { @($_.verification) })) {
        if ($trimmed -ceq ([string]$verification.command).Trim()) { return $true }
    }

    $singleAddPattern = '(?i)^git\s+add\s+--\s+(?:"([^"]+)"|''([^'']+)''|([A-Za-z0-9_.\\/:-]+))\s*$'
    $addMatch = [regex]::Match($trimmed, $singleAddPattern)
    if ($addMatch.Success) {
        $rawPath = @($addMatch.Groups[1].Value, $addMatch.Groups[2].Value, $addMatch.Groups[3].Value) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -First 1
        $relativePath = ConvertTo-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -WorkingDirectory $RepositoryRoot -Path $rawPath
        return Test-ContractPathAllowed -Path $relativePath -Contract $Contract
    }

    if ($trimmed -match '(?i)^git\s+commit\s+-m\s+(?:"[^"]+"|''[^'']+'')\s*$') {
        $validator = Join-Path $RepositoryRoot 'scripts\requirements\Validate-RequirementContract.ps1'
        if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) { return $false }
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -Mode Staged *> $null
            $validatorExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        return $validatorExitCode -eq 0
    }

    return $false
}

try {
    $inputObject = Read-RequirementHookInput
    $root = Get-RequirementRepositoryRoot -HookInput $inputObject
    $sessionId = [string]$inputObject.session_id
    $turnId = [string]$inputObject.turn_id
    $toolName = [string]$inputObject.tool_name
    $command = ''
    if ($null -ne $inputObject.tool_input -and $inputObject.tool_input.PSObject.Properties.Name -contains 'command') {
        $command = [string]$inputObject.tool_input.command
    }
    $isPatchTool = $toolName -in @('apply_patch', 'Edit', 'Write')
    if (Test-NonRepositoryToolAllowed -ToolName $toolName) {
        Write-PreToolDecision -Decision allow -AdditionalContext "Non-repository control/read tool '$toolName' allowed; repository mutations remain gated."
        exit 0
    }
    if ($toolName -ne 'Bash' -and -not $isPatchTool) {
        Write-PreToolDecision -Decision deny -Reason "Tool '$toolName' is not on the explicit non-repository/read allowlist and cannot be mapped to contract paths."
        exit 0
    }
    if ($toolName -eq 'Bash' -and (Test-ReadOnlyShellCommand -Command $command)) {
        Write-PreToolDecision -Decision allow -AdditionalContext 'Single-command read-only shell allowlist matched; repository mutations remain gated.'
        exit 0
    }
    $receiptPath = Get-RequirementReceiptPath -RepositoryRoot $root -SessionId $sessionId -TurnId $turnId
    $receipt = Read-RequirementReceipt -Path $receiptPath
    if ($null -eq $receipt) {
        Write-PreToolDecision -Decision deny -Reason 'Shell/edit call is not provably read-only and no turn-scoped requirement receipt exists. Resubmit the user prompt so UserPromptSubmit can establish fail-closed state.'
        exit 0
    }

    $activeContract = $null
    if ([string]$receipt.state -in @('approval-capture', 'approved') -and $receipt.contractPath) {
        $headContract = Get-RequirementContractAtHead -RepositoryRoot $root -RelativePath ([string]$receipt.contractPath)
        if ($null -ne $headContract -and (Test-ContractMatchesReceiptApproval -Contract $headContract -Receipt $receipt)) {
            $activeContract = $headContract
            $receipt.state = 'approved'
            Write-RequirementReceipt -Path $receiptPath -Receipt $receipt
        }
    }

    if ($toolName -eq 'Bash') {
        if ($null -eq $activeContract) {
            if ([string]$receipt.state -eq 'approval-capture' -and (Test-ApprovalCaptureShellCommand -Command $command -RepositoryRoot $root -Receipt $receipt)) {
                $receipt.mutationObserved = $true
                $receipt | Add-Member -NotePropertyName lastMutationTool -NotePropertyValue 'Bash-approval-capture' -Force
                $receipt | Add-Member -NotePropertyName lastMutationAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
                Write-RequirementReceipt -Path $receiptPath -Receipt $receipt
                Write-PreToolDecision -Decision allow -AdditionalContext 'Approval-capture shell action is restricted to staging or committing the single validated contract.'
                exit 0
            }
            Write-PreToolDecision -Decision deny -Reason 'Shell command is not on the read-only allowlist and this exact turn lacks a valid approved contract receipt. Use apply_patch only to draft Codex/Requirements/Contracts/** or context/history, then obtain separate user approval.'
            exit 0
        }

        if (-not (Test-ApprovedShellCommand -Command $command -RepositoryRoot $root -Contract $activeContract)) {
            Write-PreToolDecision -Decision deny -Reason 'Approved turns allow shell mutation only for an exact contract verification command, one mapped `git add -- <path>`, or a staged-validator-approved `git commit -m <message>`.'
            exit 0
        }

        $receipt.mutationObserved = $true
        $receipt | Add-Member -NotePropertyName lastMutationTool -NotePropertyValue 'Bash' -Force
        $receipt | Add-Member -NotePropertyName lastMutationAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
        Write-RequirementReceipt -Path $receiptPath -Receipt $receipt
        Write-PreToolDecision -Decision allow -AdditionalContext ("Constrained shell command authorized by contract {0}." -f $activeContract.contractId)
        exit 0
    }

    $workingDirectory = [string]$inputObject.cwd
    $targets = @(Get-PatchTargets -Patch $command -RepositoryRoot $root -WorkingDirectory $workingDirectory)
    if ($targets.Count -eq 0) {
        Write-PreToolDecision -Decision deny -Reason 'apply_patch was blocked because no target file headers could be parsed.'
        exit 0
    }

    $disallowed = @()
    if ([string]$receipt.state -eq 'approval-capture' -and $null -eq $activeContract) {
        $disallowed = @($targets | Where-Object { $_ -cne [string]$receipt.contractPath })
    }
    elseif ($null -eq $activeContract) {
        $disallowed = @($targets | Where-Object { -not (Test-DraftAllowedPath -Path $_) })
    }
    else {
        $patterns = @($activeContract.requirements | ForEach-Object { @($_.implementationPaths) })
        foreach ($target in $targets) {
            if (Test-DraftAllowedPath -Path $target) { continue }
            $matched = $false
            foreach ($pattern in $patterns) {
                if (Test-RequirementPathMatch -Path $target -Pattern ([string]$pattern)) {
                    $matched = $true
                    break
                }
            }
            if (-not $matched) { $disallowed += $target }
        }
    }

    if ($disallowed.Count -gt 0) {
        $reason = if ($null -eq $activeContract) {
            'apply_patch blocked before user approval. Only Codex/Requirements/Contracts/** and the active context/history may be edited. Blocked: '
        }
        else {
            'apply_patch includes paths not mapped by the approved contract. Blocked: '
        }
        Write-PreToolDecision -Decision deny -Reason ($reason + ($disallowed -join ', '))
        exit 0
    }

    $receipt.mutationObserved = $true
    $receipt | Add-Member -NotePropertyName lastMutationTool -NotePropertyValue 'apply_patch' -Force
    $receipt | Add-Member -NotePropertyName lastMutationAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
    Write-RequirementReceipt -Path $receiptPath -Receipt $receipt
    Write-PreToolDecision -Decision allow -AdditionalContext ('Patch paths passed requirement-gate mapping: ' + ($targets -join ', '))
}
catch {
    Write-PreToolDecision -Decision deny -Reason ("Requirement PreToolUse hook failed closed: $($_.Exception.Message)")
}
