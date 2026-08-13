Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom

function Read-RequirementHookInput {
    $raw = [Console]::In.ReadToEnd()
    if ($raw.Length -gt 0 -and $raw[0] -eq [char]0xFEFF) {
        $raw = $raw.Substring(1)
    }
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw 'Codex hook input is empty.'
    }

    return $raw | ConvertFrom-Json -ErrorAction Stop
}

function Get-RequirementRepositoryRoot {
    param([Parameter(Mandatory)]$HookInput)

    $cwd = [string]$HookInput.cwd
    if ([string]::IsNullOrWhiteSpace($cwd)) {
        $cwd = (Get-Location).Path
    }

    $root = (& git -C $cwd rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "Cannot resolve the Git root from '$cwd'."
    }

    return [System.IO.Path]::GetFullPath(([string]$root).Trim())
}

function Get-Sha256Text {
    param([AllowEmptyString()][string]$Text)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
    }
    finally {
        $sha.Dispose()
    }

    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-RequirementContractDigest {
    param([Parameter(Mandatory)]$Contract)

    $originalRequests = @($Contract.originalRequests | ForEach-Object {
        [ordered]@{
            source = [string]$_.source
            text   = [string]$_.text
        }
    })

    $scope = [ordered]@{
        goal              = [string]$Contract.scope.goal
        inScope           = @($Contract.scope.inScope | ForEach-Object { [string]$_ })
        outOfScope        = @($Contract.scope.outOfScope | ForEach-Object { [string]$_ })
        mode              = [string]$Contract.scope.mode
        periodOrFilter    = [string]$Contract.scope.periodOrFilter
        firstVerification = [string]$Contract.scope.firstVerification
    }

    $requirements = @($Contract.requirements | ForEach-Object {
        $requirement = $_
        [ordered]@{
            id                   = [string]$requirement.id
            text                 = [string]$requirement.text
            sourceRequestIndexes = @($requirement.sourceRequestIndexes | ForEach-Object { [int64]$_ })
            sourceQuote          = [string]$requirement.sourceQuote
            acceptanceCriteria   = @($requirement.acceptanceCriteria | ForEach-Object { [string]$_ })
            implementationPaths  = @($requirement.implementationPaths | ForEach-Object { [string]$_ })
            verification         = @($requirement.verification | ForEach-Object {
                [ordered]@{
                    id       = [string]$_.id
                    kind     = [string]$_.kind
                    command  = [string]$_.command
                    expected = [string]$_.expected
                }
            })
        }
    })

    $assumptions = @($Contract.assumptions | ForEach-Object {
        [ordered]@{
            id     = [string]$_.id
            text   = [string]$_.text
            impact = [string]$_.impact
        }
    })

    $deviations = @($Contract.deviations | ForEach-Object {
        [ordered]@{
            id     = [string]$_.id
            text   = [string]$_.text
            impact = [string]$_.impact
        }
    })

    $projection = [ordered]@{
        originalRequests = $originalRequests
        scope           = $scope
        requirements    = $requirements
        assumptions     = $assumptions
        deviations      = $deviations
    }

    $json = ConvertTo-RequirementCanonicalJson -Value $projection
    return 'sha256:' + (Get-Sha256Text -Text $json)
}

function ConvertTo-RequirementCanonicalJsonString {
    param([AllowEmptyString()][string]$Value)

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    foreach ($character in $Value.ToCharArray()) {
        $code = [int][char]$character
        $escaped = $null
        switch ($code) {
            8 { $escaped = '\b' }
            9 { $escaped = '\t' }
            10 { $escaped = '\n' }
            12 { $escaped = '\f' }
            13 { $escaped = '\r' }
            34 { $escaped = '\"' }
            92 { $escaped = '\\' }
        }
        if ($null -ne $escaped) {
            [void]$builder.Append($escaped)
            continue
        }
        if ($code -lt 32) {
            [void]$builder.Append('\u')
            [void]$builder.Append($code.ToString('x4', [System.Globalization.CultureInfo]::InvariantCulture))
        }
        else {
            [void]$builder.Append($character)
        }
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function ConvertTo-RequirementCanonicalJson {
    param($Value)

    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return ConvertTo-RequirementCanonicalJsonString -Value ([string]$Value) }
    if ($Value -is [bool]) { if ($Value) { return 'true' }; return 'false' }
    if (
        $Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or
        $Value -is [uint16] -or $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]
    ) {
        return [System.Convert]::ToString($Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $members = @()
        foreach ($key in $Value.Keys) {
            $members += (ConvertTo-RequirementCanonicalJsonString -Value ([string]$key)) + ':' + (ConvertTo-RequirementCanonicalJson -Value $Value[$key])
        }
        return '{' + ($members -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) {
            $items += ConvertTo-RequirementCanonicalJson -Value $item
        }
        return '[' + ($items -join ',') + ']'
    }
    throw "Semantic payload contains unsupported type '$($Value.GetType().FullName)'."
}

function Get-RequirementContracts {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $directory = Join-Path $RepositoryRoot 'Codex\Requirements\Contracts'
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        return @()
    }

    $contracts = @()
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.json' -File) {
        try {
            $contract = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json -ErrorAction Stop
            $contracts += [pscustomobject]@{
                Path     = $file.FullName
                Relative = 'Codex/Requirements/Contracts/' + $file.Name
                Contract = $contract
            }
        }
        catch {
            # A draft can be temporarily invalid while apply_patch is writing it.
        }
    }

    return @($contracts)
}

function Get-RequirementContractAtHead {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $gitPath = $RelativePath.Replace('\', '/')
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& git -C $RepositoryRoot show ("HEAD:$gitPath") 2>$null)
        $gitExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($gitExitCode -ne 0 -or $lines.Count -eq 0) {
        return $null
    }

    $json = ($lines -join "`n")
    return $json | ConvertFrom-Json -ErrorAction Stop
}

function Test-ContractMatchesReceiptApproval {
    param(
        [Parameter(Mandatory)]$Contract,
        [Parameter(Mandatory)]$Receipt
    )

    if ([int]$Contract.schemaVersion -ne 1) { return $false }
    if ([string]$Contract.contractId -cne [string]$Receipt.contractId) { return $false }
    if ([string]$Contract.status -cne 'approved') { return $false }
    $approvalStatus = [string]$Contract.approval.status
    $bootstrap = ([string]$Contract.contractId -eq 'RC-20260813-project-requirement-gates' -and $approvalStatus -eq 'bootstrap-approved')
    if ($approvalStatus -ne 'approved' -and -not $bootstrap) { return $false }
    if ((Get-Sha256Text -Text ([string]$Contract.approval.evidenceText)) -cne [string]$Receipt.promptSha256) { return $false }
    $digest = Get-RequirementContractDigest -Contract $Contract
    return (
        $digest -ceq [string]$Receipt.semanticDigest -and
        $digest -ceq [string]$Contract.approval.semanticDigest
    )
}

function Test-ApprovedRequirementContract {
    param(
        [Parameter(Mandatory)]$Contract,
        [Parameter(Mandatory)][string]$Prompt
    )

    if ([int]$Contract.schemaVersion -ne 1) { return $false }
    if ([string]$Contract.status -cne 'approved') { return $false }
    if ([string]$Contract.approval.evidenceText -cne $Prompt) { return $false }

    $approvalStatus = [string]$Contract.approval.status
    $bootstrap = ([string]$Contract.contractId -eq 'RC-20260813-project-requirement-gates' -and $approvalStatus -eq 'bootstrap-approved')
    if ($approvalStatus -ne 'approved' -and -not $bootstrap) { return $false }

    $computedDigest = Get-RequirementContractDigest -Contract $Contract
    return ([string]$Contract.approval.semanticDigest -ceq $computedDigest)
}

function Get-RequirementReceiptPath {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$SessionId,
        [Parameter(Mandatory)][string]$TurnId,
        [switch]$CreateDirectory
    )

    $statePath = (& git -C $RepositoryRoot rev-parse --git-path codex-requirement-gate 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($statePath)) {
        throw 'Cannot resolve the worktree-local requirement-gate state path.'
    }
    $statePath = ([string]$statePath).Trim()
    if (-not [System.IO.Path]::IsPathRooted($statePath)) {
        $statePath = Join-Path $RepositoryRoot $statePath
    }
    $statePath = [System.IO.Path]::GetFullPath($statePath)

    if ($CreateDirectory -and -not (Test-Path -LiteralPath $statePath)) {
        New-Item -ItemType Directory -Path $statePath -Force | Out-Null
    }

    $key = Get-Sha256Text -Text ($SessionId + "`n" + $TurnId)
    return Join-Path $statePath ($key + '.json')
}

function Write-RequirementReceipt {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Receipt
    )

    $temporaryPath = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $Receipt | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temporaryPath -Encoding utf8
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Read-RequirementReceipt {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -ErrorAction Stop
}

function ConvertTo-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Path
    )

    $candidate = $Path.Trim().Trim('"').Trim("'")
    if ([System.IO.Path]::IsPathRooted($candidate)) {
        $full = [System.IO.Path]::GetFullPath($candidate)
    }
    else {
        $full = [System.IO.Path]::GetFullPath((Join-Path $WorkingDirectory $candidate))
    }

    $rootWithSeparator = $RepositoryRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Patch target is outside the repository: $Path"
    }

    return $full.Substring($rootWithSeparator.Length).Replace('\', '/')
}

function Test-RequirementPathMatch {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Pattern
    )

    $normalizedPath = $Path.Replace('\', '/').TrimStart('./')
    $normalizedPattern = $Pattern.Replace('\', '/').TrimStart('./')
    $regex = [regex]::Escape($normalizedPattern)
    $regex = $regex.Replace('\*\*', '.*').Replace('\*', '[^/]*').Replace('\?', '[^/]')
    if ($normalizedPattern.EndsWith('/')) {
        $regex += '.*'
    }
    return $normalizedPath -match ('(?i)^' + $regex + '$')
}

function Test-DraftAllowedPath {
    param([Parameter(Mandatory)][string]$Path)

    return (
        $Path -like 'Codex/Requirements/Contracts/*.json' -or
        $Path -eq 'Codex/Contexts/ContextPolyCopyTrader.md' -or
        $Path -like 'Codex/Contexts/History/*'
    )
}

function Write-PreToolDecision {
    param(
        [Parameter(Mandatory)][ValidateSet('allow', 'deny')][string]$Decision,
        [string]$Reason,
        [string]$AdditionalContext
    )

    $specific = [ordered]@{
        hookEventName     = 'PreToolUse'
        permissionDecision = $Decision
    }
    if ($Reason) { $specific.permissionDecisionReason = $Reason }
    if ($AdditionalContext) { $specific.additionalContext = $AdditionalContext }
    [ordered]@{ hookSpecificOutput = $specific } | ConvertTo-Json -Depth 10 -Compress
}
