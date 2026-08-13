[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TempRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedTempRoot = [System.IO.Path]::GetFullPath($TempRoot)
if (-not $resolvedTempRoot.StartsWith('D:\CodexTemp\runs\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "TempRoot must be inside D:\CodexTemp\runs: $resolvedTempRoot"
}
if (-not (Test-Path -LiteralPath $resolvedTempRoot -PathType Container)) {
    throw "TempRoot does not exist: $resolvedTempRoot"
}

$testRoot = Join-Path $resolvedTempRoot ('requirement-hook-tests-' + [guid]::NewGuid().ToString('N'))
$sourceHookRoot = $PSScriptRoot
$passes = 0

function Assert-Hook {
    param([bool]$Condition, [string]$Name)
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:passes++
    Write-Output "PASS: $Name"
}

function Assert-HookProperty {
    param($Object, [string]$PropertyName, [string]$Name)
    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$PropertyName]) {
        $json = if ($null -eq $Object) { '<null>' } else { $Object | ConvertTo-Json -Depth 20 -Compress }
        throw "FAIL: $Name. Missing property '$PropertyName'. Object: $json"
    }
}

function Invoke-TestHook {
    param([string]$Repository, [string]$ScriptName, [System.Collections.IDictionary]$Payload)
    $scriptPath = Join-Path $Repository ('.codex\hooks\' + $ScriptName)
    $json = [string](ConvertTo-Json -InputObject $Payload -Depth 100 -Compress)
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'powershell.exe'
    $startInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $scriptPath + '"'
    $startInfo.WorkingDirectory = $Repository
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $inputBytes = [System.Text.Encoding]::UTF8.GetBytes([string]$json)
    $process.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length)
    $process.StandardInput.BaseStream.Flush()
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Hook process failed: $ScriptName. $errorOutput" }
    try {
        return $output | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Hook output parse failed for $ScriptName. Output: $($output -join ' | '). Error: $($_.Exception.Message)"
    }
}

function New-TestContract {
    param(
        [string]$Prompt,
        [string]$ContractId = 'RC-20990101-hook-test'
    )
    return [ordered]@{
        schemaVersion = 1
        contractId = $ContractId
        title = 'Hook lifecycle test'
        author = 'agent:test'
        status = 'draft'
        originalRequests = @([ordered]@{ source = 'user'; text = $Prompt })
        scope = [ordered]@{
            goal = 'Test hooks'
            inScope = @('src/**')
            outOfScope = @('production')
            mode = 'local-edit'
            periodOrFilter = 'not-applicable'
            firstVerification = 'hook harness'
        }
        requirements = @([ordered]@{
            id = 'REQ-001'
            text = 'Allow only mapped changes after approval.'
            sourceRequestIndexes = @(0)
            sourceQuote = $Prompt
            acceptanceCriteria = @('Mapped edit allowed; unmapped edit denied.')
            implementationPaths = @('src/**')
            verification = @([ordered]@{
                id = 'VER-001'
                kind = 'test'
                command = '.codex/hooks/Test-RequirementHooks.ps1'
                expected = 'All hook cases pass.'
                result = 'pending'
                evidence = ''
            })
        })
        assumptions = @()
        deviations = @()
        approval = [ordered]@{
            status = 'pending'
            approvedBy = 'user'
            evidenceText = 'pending'
            semanticDigest = 'pending'
        }
        independentReview = [ordered]@{
            reviewer = 'agent:reviewer'
            comparedOriginalRequests = $false
            verdict = 'pending'
            findings = @()
        }
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    & git -C $testRoot init --quiet
    & git -C $testRoot config user.email 'hook-tests@example.invalid'
    & git -C $testRoot config user.name 'Requirement Hook Tests'
    New-Item -ItemType Directory -Path (Join-Path $testRoot '.codex\hooks') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceHookRoot 'RequirementGate.Common.ps1') -Destination (Join-Path $testRoot '.codex\hooks\RequirementGate.Common.ps1')
    Copy-Item -LiteralPath (Join-Path $sourceHookRoot 'Invoke-RequirementLifecycle.ps1') -Destination (Join-Path $testRoot '.codex\hooks\Invoke-RequirementLifecycle.ps1')
    Copy-Item -LiteralPath (Join-Path $sourceHookRoot 'Invoke-RequirementPreToolUse.ps1') -Destination (Join-Path $testRoot '.codex\hooks\Invoke-RequirementPreToolUse.ps1')
    Copy-Item -LiteralPath (Join-Path $sourceHookRoot 'Invoke-RequirementStop.ps1') -Destination (Join-Path $testRoot '.codex\hooks\Invoke-RequirementStop.ps1')
    New-Item -ItemType Directory -Path (Join-Path $testRoot 'Codex\Requirements\Contracts') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $testRoot 'src') -Force | Out-Null
    'baseline' | Set-Content -LiteralPath (Join-Path $testRoot 'README.md') -Encoding utf8

    $session = 'hook-harness-session'
    $initialTurn = 'turn-initial'
    $initialPrompt = 'Implement the mapped hook test change.'
    $contract = New-TestContract -Prompt $initialPrompt
    $contractPath = 'Codex/Requirements/Contracts/RC-20990101-hook-test.json'
    $contractFullPath = Join-Path $testRoot $contractPath
    $contract | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $contractFullPath -Encoding utf8
    & git -C $testRoot add README.md $contractPath
    & git -C $testRoot commit --quiet -m 'baseline draft contract'

    $sessionStart = Invoke-TestHook $testRoot 'Invoke-RequirementLifecycle.ps1' ([ordered]@{
        session_id = $session; cwd = $testRoot; hook_event_name = 'SessionStart'; source = 'startup'
    })
    Assert-HookProperty $sessionStart 'hookSpecificOutput' 'SessionStart output shape'
    Assert-Hook ($sessionStart.hookSpecificOutput.additionalContext -match 'Requirement gate') 'SessionStart injects mandatory context'

    $subagent = Invoke-TestHook $testRoot 'Invoke-RequirementLifecycle.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'SubagentStart'; agent_id = 'a'; agent_type = 'worker'
    })
    Assert-Hook ($subagent.hookSpecificOutput.additionalContext -match 'may not broaden scope') 'SubagentStart injects scope guard'

    $noReceiptRead = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = 'turn-without-receipt'; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'git status --short' }
    })
    $noReceiptWrite = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = 'turn-without-receipt'; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'Set-Content README.md changed' }
    })
    Assert-Hook ($noReceiptRead.hookSpecificOutput.permissionDecision -eq 'allow') 'Read-only shell allowed when receipt is absent'
    Assert-Hook ($noReceiptWrite.hookSpecificOutput.permissionDecision -eq 'deny') 'Mutation denied when receipt is absent'

    $knownReadTool = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = 'turn-without-receipt'; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'read_mcp_resource'; tool_input = @{ server = 'test'; uri = 'test://resource' }
    })
    $unknownMcpTool = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = 'turn-without-receipt'; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'mcp__db__execute'; tool_input = @{ sql = 'UPDATE x SET y = 1' }
    })
    Assert-Hook ($knownReadTool.hookSpecificOutput.permissionDecision -eq 'allow') 'Explicit read-only local tool is allowed'
    Assert-Hook ($unknownMcpTool.hookSpecificOutput.permissionDecision -eq 'deny') 'Unknown MCP/local tool is fail-closed'

    $initial = Invoke-TestHook $testRoot 'Invoke-RequirementLifecycle.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'UserPromptSubmit'; prompt = $initialPrompt
    })
    Assert-Hook ($initial.systemMessage -match 'not a separate approval turn') 'Initial prompt remains draft-only'

    $read = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'git status --short' }
    })
    Assert-Hook ($read.hookSpecificOutput.permissionDecision -eq 'allow') 'Read-only shell allowed before approval'

    $shellWrite = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'Set-Content README.md changed' }
    })
    Assert-Hook ($shellWrite.hookSpecificOutput.permissionDecision -eq 'deny') 'Mutating shell denied before approval'

    $bypassCommands = @(
        "[IO.File]::WriteAllText('README.md','changed')",
        "git -C `"$testRoot`" add .",
        'powershell.exe -NoProfile -Command "Set-Content README.md changed"',
        'Get-Content README.md | Set-Content copied.md',
        'git diff --output=README.md HEAD',
        'git diff --ext-diff -- README.md',
        'git diff --textconv -- README.md',
        'git show --output=README.md HEAD',
        'git show --ext-diff HEAD',
        'git show --textconv HEAD',
        'git log --output=README.md -1',
        'git log --ext-diff -1',
        'git log --textconv -1',
        'git diff --out=README.md HEAD',
        'git show --help',
        'rg --pre=Set-Content README.md pattern .',
        'git cat-file --filters HEAD:README.md',
        'Get-Content (Set-Content README.md pwn)',
        'rg --no-config --hostname-bin=mutator.exe pattern .',
        'rg pattern .'
    )
    foreach ($bypassCommand in $bypassCommands) {
        $bypass = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
            session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = $bypassCommand }
        })
        Assert-Hook ($bypass.hookSpecificOutput.permissionDecision -eq 'deny') "Shell bypass denied: $bypassCommand"
    }

    $hardenedRipgrep = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'rg --no-config pattern .' }
    })
    Assert-Hook ($hardenedRipgrep.hookSpecificOutput.permissionDecision -eq 'allow') 'Ripgrep remains available with config loading disabled'

    $hardenedDiff = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'git diff --no-ext-diff --no-textconv -- README.md' }
    })
    $hardenedShow = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'git show --no-ext-diff --no-textconv HEAD' }
    })
    $hardenedLog = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'git log --no-ext-diff --no-textconv -1' }
    })
    Assert-Hook ($hardenedDiff.hookSpecificOutput.permissionDecision -eq 'allow') 'Hardened read-only git diff remains allowed'
    Assert-Hook ($hardenedShow.hookSpecificOutput.permissionDecision -eq 'allow') 'Hardened read-only git show remains allowed'
    Assert-Hook ($hardenedLog.hookSpecificOutput.permissionDecision -eq 'allow') 'Hardened read-only git log remains allowed'

    $draftPatch = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Write'; tool_input = @{ command = "*** Begin Patch`n*** Update File: $contractPath`n@@`n-a`n+b`n*** End Patch" }
    })
    Assert-Hook ($draftPatch.hookSpecificOutput.permissionDecision -eq 'allow') 'Write alias allows only draft contract path'

    $productPatch = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Edit'; tool_input = @{ command = "*** Begin Patch`n*** Update File: src/Test.cs`n@@`n-a`n+b`n*** End Patch" }
    })
    Assert-Hook ($productPatch.hookSpecificOutput.permissionDecision -eq 'deny') 'Edit alias denies product path before approval'

    $stopFirst = Invoke-TestHook $testRoot 'Invoke-RequirementStop.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'Stop'; stop_hook_active = $false
    })
    $stopSecond = Invoke-TestHook $testRoot 'Invoke-RequirementStop.ps1' ([ordered]@{
        session_id = $session; turn_id = $initialTurn; cwd = $testRoot; hook_event_name = 'Stop'; stop_hook_active = $true
    })
    Assert-Hook ($stopFirst.decision -eq 'block') 'Stop requests approval continuation once'
    Assert-Hook ([bool]$stopSecond.continue) 'Stop avoids continuation loop'

    . (Join-Path $testRoot '.codex\hooks\RequirementGate.Common.ps1')

    $untrackedPrompt = 'Create a new untracked draft contract.'
    $untrackedContract = New-TestContract -Prompt $untrackedPrompt -ContractId 'RC-20990102-untracked-hook-test'
    $untrackedPath = 'Codex/Requirements/Contracts/RC-20990102-untracked-hook-test.json'
    $untrackedContract | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $testRoot $untrackedPath) -Encoding utf8
    $untrackedDigest = Get-RequirementContractDigest -Contract $untrackedContract
    $untrackedCapture = Invoke-TestHook $testRoot 'Invoke-RequirementLifecycle.ps1' ([ordered]@{
        session_id = $session; turn_id = 'turn-untracked-approval'; cwd = $testRoot; hook_event_name = 'UserPromptSubmit'; prompt = "APPROVE $($untrackedContract.contractId) $untrackedDigest"
    })
    Assert-Hook ($untrackedCapture.systemMessage -match 'Exact approval captured') 'Untracked draft contract can enter approval-capture without HEAD lookup failure'

    $digest = Get-RequirementContractDigest -Contract $contract
    $approvalPrompt = "APPROVE $($contract.contractId) $digest"
    $approvalTurn = 'turn-approval'
    $capture = Invoke-TestHook $testRoot 'Invoke-RequirementLifecycle.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'UserPromptSubmit'; prompt = $approvalPrompt
    })
    Assert-Hook ($capture.systemMessage -match 'Exact approval captured') 'Exact APPROVE prompt enters approval-capture'

    $captureProduct = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'apply_patch'; tool_input = @{ command = "*** Begin Patch`n*** Update File: src/Test.cs`n@@`n-a`n+b`n*** End Patch" }
    })
    Assert-Hook ($captureProduct.hookSpecificOutput.permissionDecision -eq 'deny') 'Product path remains denied during approval-capture'

    $captureContract = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'apply_patch'; tool_input = @{ command = "*** Begin Patch`n*** Update File: $contractPath`n@@`n-a`n+b`n*** End Patch" }
    })
    Assert-Hook ($captureContract.hookSpecificOutput.permissionDecision -eq 'allow') 'Approval-capture allows only its contract patch'

    $contract.status = 'approved'
    $contract.approval.status = 'approved'
    $contract.approval.evidenceText = $approvalPrompt
    $contract.approval.semanticDigest = $digest
    $contract | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $contractFullPath -Encoding utf8

    $broadAdd = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'git add .' }
    })
    Assert-Hook ($broadAdd.hookSpecificOutput.permissionDecision -eq 'deny') 'Approval-capture denies broad git add'

    $exactAddCommand = "git add -- $contractPath"
    $exactAdd = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = $exactAddCommand }
    })
    Assert-Hook ($exactAdd.hookSpecificOutput.permissionDecision -eq 'allow') 'Approval-capture allows exact validated contract add'
    & git -C $testRoot add -- $contractPath

    $commitCheck = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'Bash'; tool_input = @{ command = 'git commit -m "approve contract"' }
    })
    Assert-Hook ($commitCheck.hookSpecificOutput.permissionDecision -eq 'allow') 'Approval-capture allows single validated contract commit'
    & git -C $testRoot commit --quiet -m 'approve contract'

    $validatorDirectory = Join-Path $testRoot 'scripts\requirements'
    New-Item -ItemType Directory -Path $validatorDirectory -Force | Out-Null
    "param([string]`$Mode)`nWrite-Output 'validator pass'`nexit 0" | Set-Content -LiteralPath (Join-Path $validatorDirectory 'Validate-RequirementContract.ps1') -Encoding utf8
    $approvedStop = Invoke-TestHook $testRoot 'Invoke-RequirementStop.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'Stop'; stop_hook_active = $false
    })
    Assert-Hook ([bool]$approvedStop.continue -and $approvedStop.systemMessage -match 'validation passed') 'Stop promotes approval-capture from HEAD and runs WorkingTree validator'

    $mapped = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'apply_patch'; tool_input = @{ command = "*** Begin Patch`n*** Add File: src/Test.cs`n+x`n*** End Patch" }
    })
    Assert-Hook ($mapped.hookSpecificOutput.permissionDecision -eq 'allow') 'Approved HEAD unlocks mapped product path in same turn'

    $unmapped = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'apply_patch'; tool_input = @{ command = "*** Begin Patch`n*** Update File: README.md`n@@`n-a`n+b`n*** End Patch" }
    })
    Assert-Hook ($unmapped.hookSpecificOutput.permissionDecision -eq 'deny') 'Approved HEAD still denies unmapped path'

    "param([string]`$Mode)`nWrite-Output 'validator failed'`nexit 9" | Set-Content -LiteralPath (Join-Path $validatorDirectory 'Validate-RequirementContract.ps1') -Encoding utf8
    $failedStop = Invoke-TestHook $testRoot 'Invoke-RequirementStop.ps1' ([ordered]@{
        session_id = $session; turn_id = $approvalTurn; cwd = $testRoot; hook_event_name = 'Stop'; stop_hook_active = $false
    })
    Assert-Hook ($failedStop.decision -eq 'block' -and $failedStop.reason -match 'validator failed') 'Stop continues once when WorkingTree validator fails'

    $contract.approval.semanticDigest = 'sha256:' + ('0' * 64)
    $contract | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $contractFullPath -Encoding utf8
    & git -C $testRoot add -- $contractPath
    & git -C $testRoot commit --quiet -m 'corrupt digest for negative test'
    $invalidTurn = 'turn-invalid-digest'
    $invalidLifecycle = Invoke-TestHook $testRoot 'Invoke-RequirementLifecycle.ps1' ([ordered]@{
        session_id = $session; turn_id = $invalidTurn; cwd = $testRoot; hook_event_name = 'UserPromptSubmit'; prompt = $approvalPrompt
    })
    $invalidPatch = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $invalidTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'apply_patch'; tool_input = @{ command = "*** Begin Patch`n*** Add File: src/Invalid.cs`n+x`n*** End Patch" }
    })
    Assert-Hook ($invalidLifecycle.systemMessage -match 'no separately approved') 'Invalid digest does not mint approved receipt'
    Assert-Hook ($invalidPatch.hookSpecificOutput.permissionDecision -eq 'deny') 'Invalid digest blocks mapped mutation'

    $contract.approval.semanticDigest = $digest
    $contract.status = 'completed'
    $contract.independentReview.comparedOriginalRequests = $true
    $contract.independentReview.verdict = 'pass'
    $contract.requirements[0].verification[0].result = 'passed'
    $contract.requirements[0].verification[0].evidence = 'completed contract activation negative test'
    $contract | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $contractFullPath -Encoding utf8
    & git -C $testRoot add -- $contractPath
    & git -C $testRoot commit --quiet -m 'complete contract'
    $completedTurn = 'turn-completed-contract'
    $completedLifecycle = Invoke-TestHook $testRoot 'Invoke-RequirementLifecycle.ps1' ([ordered]@{
        session_id = $session; turn_id = $completedTurn; cwd = $testRoot; hook_event_name = 'UserPromptSubmit'; prompt = $approvalPrompt
    })
    $completedPatch = Invoke-TestHook $testRoot 'Invoke-RequirementPreToolUse.ps1' ([ordered]@{
        session_id = $session; turn_id = $completedTurn; cwd = $testRoot; hook_event_name = 'PreToolUse'; tool_name = 'apply_patch'; tool_input = @{ command = "*** Begin Patch`n*** Add File: src/CompletedReuse.cs`n+x`n*** End Patch" }
    })
    Assert-Hook ($completedLifecycle.systemMessage -match 'no separately approved') 'Completed contract does not reactivate from an old approval prompt'
    Assert-Hook ($completedPatch.hookSpecificOutput.permissionDecision -eq 'deny') 'Completed contract cannot authorize new mapped mutation'

    Write-Output "Requirement hook tests passed: $passes"
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $verified = [System.IO.Path]::GetFullPath($testRoot)
        if ($verified.StartsWith($resolvedTempRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $verified -Recurse -Force
        }
    }
}
