[CmdletBinding()]
param(
    [ValidateSet("Contract", "WorkingTree", "Staged", "Range")]
    [string]$Mode = "WorkingTree",

    [string]$RepositoryRoot,

    [string]$ContractPath,

    [string]$BaseRef,

    [string]$HeadRef = "HEAD",

    [switch]$AllowDraft,

    [switch]$AllowPendingEvidence,

    [switch]$AllowBootstrapContract,

    [switch]$PrintSemanticDigest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:BootstrapContractId = "RC-20260813-project-requirement-gates"
$script:ContractPathPattern = '^Codex/Requirements/Contracts/([^/]+)\.json$'

function Stop-Gate {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Message
    )

    throw [System.InvalidOperationException]::new("REQUIREMENT_GATE_ERROR [$Code] $Message")
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        Stop-Gate -Code $Code -Message $Message
    }
}

function Test-ObjectProperty {
    param($Object, [string]$Name)

    return $null -ne $Object -and $null -ne $Object.PSObject -and
        $null -ne $Object.PSObject.Properties[$Name]
}

function Assert-ObjectShape {
    param(
        $Object,
        [string[]]$Required,
        [string[]]$Allowed,
        [string]$Location
    )

    Assert-Condition ($null -ne $Object -and -not ($Object -is [string]) -and
        $null -ne $Object.PSObject) "JSON_OBJECT" "$Location must be a JSON object."

    $names = @($Object.PSObject.Properties | ForEach-Object { $_.Name })
    foreach ($name in $Required) {
        Assert-Condition ($names -ccontains $name) "JSON_REQUIRED_PROPERTY" "$Location is missing required property '$name'."
    }
    foreach ($name in $names) {
        Assert-Condition ($Allowed -ccontains $name) "JSON_ADDITIONAL_PROPERTY" "$Location contains unsupported property '$name'."
    }
}

function Assert-String {
    param($Value, [string]$Location, [switch]$AllowEmpty)

    Assert-Condition ($Value -is [string]) "JSON_STRING" "$Location must be a string."
    if (-not $AllowEmpty) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$Value)) "JSON_EMPTY_STRING" "$Location must not be empty."
    }
}

function Test-IsArray {
    param($Value)

    return $null -ne $Value -and $Value -is [System.Collections.IList] -and -not ($Value -is [string])
}

function Assert-StringArray {
    param($Value, [string]$Location, [switch]$AllowEmpty)

    Assert-Condition (Test-IsArray $Value) "JSON_ARRAY" "$Location must be an array."
    $items = @($Value)
    if (-not $AllowEmpty) {
        Assert-Condition ($items.Count -gt 0) "JSON_EMPTY_ARRAY" "$Location must contain at least one item."
    }
    for ($index = 0; $index -lt $items.Count; $index++) {
        Assert-String $items[$index] "$Location[$index]"
    }
}

function Assert-Enum {
    param($Value, [string[]]$Allowed, [string]$Location)

    Assert-String $Value $Location
    Assert-Condition ($Allowed -ccontains [string]$Value) "JSON_ENUM" "$Location has unsupported value '$Value'."
}

function Normalize-RepoPath {
    param([string]$Path)

    return $Path.Replace('\', '/')
}

function Test-IsContractRecordPath {
    param([string]$Path)

    return (Normalize-RepoPath $Path) -cmatch $script:ContractPathPattern
}

function Test-IsExemptPath {
    param([string]$Path)

    $normalized = Normalize-RepoPath $Path
    if ($normalized -ceq 'Codex/Contexts/ContextPolyCopyTrader.md') {
        return $true
    }
    if ($normalized -cmatch '^Codex/Contexts/History/.+') {
        return $true
    }
    return Test-IsContractRecordPath $normalized
}

function Assert-ImplementationPath {
    param([string]$Path, [string]$Location)

    Assert-String $Path $Location
    Assert-Condition ($Path -ceq (Normalize-RepoPath $Path)) "PATH_SEPARATOR" "$Location must use forward slashes."
    Assert-Condition ($Path -ceq $Path.Trim()) "PATH_WHITESPACE" "$Location must not have leading or trailing whitespace."
    Assert-Condition (-not [System.IO.Path]::IsPathRooted($Path)) "PATH_ROOTED" "$Location must be repository-relative."
    Assert-Condition (-not $Path.StartsWith("./", [System.StringComparison]::Ordinal)) "PATH_DOT_PREFIX" "$Location must not start with './'."
    Assert-Condition (-not $Path.StartsWith("/", [System.StringComparison]::Ordinal)) "PATH_ROOTED" "$Location must be repository-relative."
    Assert-Condition (-not ($Path -cmatch '(^|/)\.\.(/|$)')) "PATH_TRAVERSAL" "$Location must not contain '..' segments."
    Assert-Condition (-not ($Path -cmatch '(^|/)\.(/|$)')) "PATH_DOT_SEGMENT" "$Location must not contain '.' segments."
    Assert-Condition (-not $Path.Contains("[")) "PATH_GLOB_SYNTAX" "$Location supports only '*', '**', and '?' wildcards."
    Assert-Condition (-not $Path.Contains("]")) "PATH_GLOB_SYNTAX" "$Location supports only '*', '**', and '?' wildcards."
    Assert-Condition (-not $Path.Contains("{")) "PATH_GLOB_SYNTAX" "$Location supports only '*', '**', and '?' wildcards."
    Assert-Condition (-not $Path.Contains("}")) "PATH_GLOB_SYNTAX" "$Location supports only '*', '**', and '?' wildcards."
}

function Convert-GlobToRegex {
    param([string]$Pattern)

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('^')
    for ($index = 0; $index -lt $Pattern.Length; $index++) {
        $character = $Pattern[$index]
        if ($character -eq '*') {
            if (($index + 1) -lt $Pattern.Length -and $Pattern[$index + 1] -eq '*') {
                $index++
                if (($index + 1) -lt $Pattern.Length -and $Pattern[$index + 1] -eq '/') {
                    $index++
                    [void]$builder.Append('(?:.*/)?')
                }
                else {
                    [void]$builder.Append('.*')
                }
            }
            else {
                [void]$builder.Append('[^/]*')
            }
        }
        elseif ($character -eq '?') {
            [void]$builder.Append('[^/]')
        }
        else {
            [void]$builder.Append([System.Text.RegularExpressions.Regex]::Escape([string]$character))
        }
    }
    [void]$builder.Append('$')
    return $builder.ToString()
}

function Test-PathCovered {
    param([string]$Path, [string]$Pattern)

    $regex = Convert-GlobToRegex $Pattern
    return [System.Text.RegularExpressions.Regex]::IsMatch(
        $Path,
        $regex,
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Get-GitLines {
    param([string]$Root, [string[]]$Arguments, [string]$Operation)

    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = @(& git -C $Root @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorPreference
    }
    if ($exitCode -ne 0) {
        $details = ($output | ForEach-Object { [string]$_ }) -join " | "
        Stop-Gate -Code "GIT_FAILURE" -Message "$Operation failed (exit $exitCode): $details"
    }
    # Windows PowerShell represents native stderr records as ErrorRecord values
    # when streams are merged. Keep them only for failure diagnostics; on
    # success, callers must receive stdout alone so Git warnings cannot be
    # mistaken for paths, refs, or object contents.
    return @($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } |
        ForEach-Object { [string]$_ })
}

function Test-GitObjectExists {
    param([string]$Root, [string]$ObjectSpec)

    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & git -C $Root cat-file -e $ObjectSpec 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorPreference
    }
    return $exitCode -eq 0
}

function Get-GitText {
    param([string]$Root, [string]$ObjectSpec)

    $lines = Get-GitLines -Root $Root -Arguments @("show", $ObjectSpec) -Operation "Read '$ObjectSpec'"
    return $lines -join "`n"
}

function Resolve-RepositoryRoot {
    param([string]$RequestedRoot)

    if ([string]::IsNullOrWhiteSpace($RequestedRoot)) {
        $RequestedRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    }
    Assert-Condition (Test-Path -LiteralPath $RequestedRoot -PathType Container) "REPOSITORY_ROOT" "Repository root '$RequestedRoot' does not exist."
    $resolved = (Resolve-Path -LiteralPath $RequestedRoot).Path
    $gitRootLines = @(Get-GitLines -Root $resolved -Arguments @("rev-parse", "--show-toplevel") -Operation "Resolve repository root")
    Assert-Condition ($gitRootLines.Count -eq 1) "REPOSITORY_ROOT" "Git returned an ambiguous repository root."
    $gitRoot = [System.IO.Path]::GetFullPath($gitRootLines[0])
    $expected = [System.IO.Path]::GetFullPath($resolved)
    Assert-Condition ($gitRoot.TrimEnd('\', '/') -ieq $expected.TrimEnd('\', '/')) "REPOSITORY_ROOT" "'$resolved' is not the Git worktree root ('$gitRoot')."
    return $expected.TrimEnd('\', '/')
}

function Assert-GitRef {
    param([string]$Root, [string]$Ref, [string]$ParameterName)

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($Ref)) "GIT_REF" "$ParameterName is required."
    [void](Get-GitLines -Root $Root -Arguments @("rev-parse", "--verify", "$Ref^{commit}") -Operation "Resolve $ParameterName '$Ref'")
}

function Get-ChangedPaths {
    param([string]$Root, [string]$ValidationMode, [string]$RangeBase, [string]$RangeHead)

    if ($ValidationMode -eq "WorkingTree") {
        $tracked = Get-GitLines -Root $Root -Arguments @("-c", "core.quotepath=false", "diff", "--no-renames", "--name-only", "--diff-filter=ACMRD", "HEAD", "--") -Operation "Read working-tree diff"
        $untracked = Get-GitLines -Root $Root -Arguments @("-c", "core.quotepath=false", "ls-files", "--others", "--exclude-standard", "--") -Operation "Read untracked files"
        $paths = @($tracked) + @($untracked)
    }
    elseif ($ValidationMode -eq "Staged") {
        $paths = Get-GitLines -Root $Root -Arguments @("-c", "core.quotepath=false", "diff", "--cached", "--no-renames", "--name-only", "--diff-filter=ACMRD", "HEAD", "--") -Operation "Read staged diff"
    }
    else {
        $paths = Get-GitLines -Root $Root -Arguments @("-c", "core.quotepath=false", "diff", "--no-renames", "--name-only", "--diff-filter=ACMRD", $RangeBase, $RangeHead, "--") -Operation "Read range diff"
    }

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($path in @($paths)) {
        if (-not [string]::IsNullOrEmpty($path)) {
            [void]$set.Add((Normalize-RepoPath $path))
        }
    }
    $result = @($set)
    [System.Array]::Sort($result, [System.StringComparer]::Ordinal)
    return $result
}

function Get-ContractSource {
    param([string]$Root, [string]$ValidationMode, [string]$Path, [string]$RangeHead)

    if ($ValidationMode -eq "WorkingTree" -or $ValidationMode -eq "Contract") {
        $absolute = Join-Path $Root ($Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        Assert-Condition (Test-Path -LiteralPath $absolute -PathType Leaf) "CONTRACT_MISSING" "Contract '$Path' does not exist in the working tree."
        return Get-Content -LiteralPath $absolute -Raw -Encoding UTF8
    }
    if ($ValidationMode -eq "Staged") {
        Assert-Condition (Test-GitObjectExists -Root $Root -ObjectSpec ":$Path") "CONTRACT_MISSING" "Contract '$Path' does not exist in the Git index."
        return Get-GitText -Root $Root -ObjectSpec ":$Path"
    }
    Assert-Condition (Test-GitObjectExists -Root $Root -ObjectSpec "$RangeHead`:$Path") "CONTRACT_MISSING" "Contract '$Path' does not exist at '$RangeHead'."
    return Get-GitText -Root $Root -ObjectSpec "$RangeHead`:$Path"
}

function Resolve-ContractRelativePath {
    param([string]$Root, [string]$RequestedPath)

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($RequestedPath)) "CONTRACT_PATH" "-ContractPath is required in Contract mode."
    if ([System.IO.Path]::IsPathRooted($RequestedPath)) {
        $absolute = [System.IO.Path]::GetFullPath($RequestedPath)
    }
    else {
        $absolute = [System.IO.Path]::GetFullPath((Join-Path $Root $RequestedPath))
    }
    $rootPrefix = $Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition ($absolute.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) "CONTRACT_PATH" "Contract path must be inside the repository root."
    $relative = Normalize-RepoPath $absolute.Substring($rootPrefix.Length)
    Assert-Condition (Test-IsContractRecordPath $relative) "CONTRACT_PATH" "Contract must be a direct JSON file under Codex/Requirements/Contracts."
    return $relative
}

function Read-ContractJson {
    param([string]$Json, [string]$Path)

    try {
        return $Json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Stop-Gate -Code "CONTRACT_JSON" -Message "Contract '$Path' is not valid JSON: $($_.Exception.Message)"
    }
}

function Assert-ApprovalShape {
    param($Approval, [string]$Location)

    $properties = @("status", "approvedBy", "evidenceText", "semanticDigest")
    Assert-ObjectShape $Approval $properties $properties $Location
    Assert-Enum $Approval.status @("pending", "approved", "bootstrap-approved") "$Location.status"
    Assert-String $Approval.approvedBy "$Location.approvedBy"
    Assert-Condition ($Approval.approvedBy -ceq "user") "APPROVAL_ACTOR" "$Location.approvedBy must be exactly 'user'."
    Assert-String $Approval.evidenceText "$Location.evidenceText"
    Assert-String $Approval.semanticDigest "$Location.semanticDigest"
    Assert-Condition ($Approval.semanticDigest -ceq "pending" -or $Approval.semanticDigest -cmatch '^sha256:[0-9a-f]{64}$') "APPROVAL_DIGEST_FORMAT" "$Location.semanticDigest must be 'pending' or a lowercase SHA-256 value."
}

function Assert-ExceptionArray {
    param($Items, [string]$Kind, [string]$Prefix)

    Assert-Condition (Test-IsArray $Items) "JSON_ARRAY" "$Kind must be an array."
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $values = @($Items)
    for ($index = 0; $index -lt $values.Count; $index++) {
        $location = "$Kind[$index]"
        $properties = @("id", "text", "impact", "approval")
        Assert-ObjectShape $values[$index] $properties $properties $location
        Assert-String $values[$index].id "$location.id"
        Assert-Condition ($values[$index].id -cmatch "^$Prefix-[0-9]{3}$") "EXCEPTION_ID" "$location.id has an invalid format."
        Assert-Condition ($seen.Add([string]$values[$index].id)) "DUPLICATE_ID" "$location.id duplicates '$($values[$index].id)'."
        Assert-String $values[$index].text "$location.text"
        Assert-String $values[$index].impact "$location.impact"
        Assert-ApprovalShape $values[$index].approval "$location.approval"
    }
}

function Assert-ContractStructure {
    param($Contract, [string]$Path)

    $topProperties = @(
        "schemaVersion", "contractId", "title", "author", "status", "originalRequests",
        "scope", "requirements", "assumptions", "deviations", "approval", "independentReview"
    )
    Assert-ObjectShape $Contract $topProperties $topProperties "contract"

    Assert-Condition ($Contract.schemaVersion -is [ValueType] -and [int64]$Contract.schemaVersion -eq 1) "SCHEMA_VERSION" "schemaVersion must be 1."
    Assert-String $Contract.contractId "contractId"
    Assert-Condition ($Contract.contractId -cmatch '^RC-[0-9]{8}-[a-z0-9-]+$') "CONTRACT_ID" "contractId has an invalid format."
    $expectedFileName = "$($Contract.contractId).json"
    Assert-Condition ([System.IO.Path]::GetFileName($Path) -ceq $expectedFileName) "CONTRACT_FILENAME" "Contract filename must be '$expectedFileName'."
    Assert-String $Contract.title "title"
    Assert-String $Contract.author "author"
    Assert-Enum $Contract.status @("draft", "approved", "completed") "status"

    Assert-Condition (Test-IsArray $Contract.originalRequests) "JSON_ARRAY" "originalRequests must be an array."
    $requests = @($Contract.originalRequests)
    Assert-Condition ($requests.Count -gt 0) "JSON_EMPTY_ARRAY" "originalRequests must contain at least one request."
    for ($index = 0; $index -lt $requests.Count; $index++) {
        $properties = @("source", "text")
        Assert-ObjectShape $requests[$index] $properties $properties "originalRequests[$index]"
        Assert-String $requests[$index].source "originalRequests[$index].source"
        Assert-Condition ($requests[$index].source -ceq "user") "REQUEST_SOURCE" "originalRequests[$index].source must be exactly 'user'."
        Assert-String $requests[$index].text "originalRequests[$index].text"
    }

    $scopeProperties = @("goal", "inScope", "outOfScope", "mode", "periodOrFilter", "firstVerification")
    Assert-ObjectShape $Contract.scope $scopeProperties $scopeProperties "scope"
    Assert-String $Contract.scope.goal "scope.goal"
    Assert-StringArray $Contract.scope.inScope "scope.inScope"
    Assert-StringArray $Contract.scope.outOfScope "scope.outOfScope"
    Assert-Enum $Contract.scope.mode @("read-only", "local-edit", "mutation") "scope.mode"
    Assert-String $Contract.scope.periodOrFilter "scope.periodOrFilter"
    Assert-String $Contract.scope.firstVerification "scope.firstVerification"

    Assert-Condition (Test-IsArray $Contract.requirements) "JSON_ARRAY" "requirements must be an array."
    $requirements = @($Contract.requirements)
    Assert-Condition ($requirements.Count -gt 0) "JSON_EMPTY_ARRAY" "requirements must contain at least one requirement."
    $requirementIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $verificationIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    for ($requirementIndex = 0; $requirementIndex -lt $requirements.Count; $requirementIndex++) {
        $requirement = $requirements[$requirementIndex]
        $location = "requirements[$requirementIndex]"
        $properties = @("id", "text", "sourceRequestIndexes", "sourceQuote", "acceptanceCriteria", "implementationPaths", "verification")
        Assert-ObjectShape $requirement $properties $properties $location
        Assert-String $requirement.id "$location.id"
        Assert-Condition ($requirement.id -cmatch '^REQ-[0-9]{3}$') "REQUIREMENT_ID" "$location.id has an invalid format."
        Assert-Condition ($requirementIds.Add([string]$requirement.id)) "DUPLICATE_ID" "$location.id duplicates '$($requirement.id)'."
        Assert-String $requirement.text "$location.text"

        Assert-Condition (Test-IsArray $requirement.sourceRequestIndexes) "JSON_ARRAY" "$location.sourceRequestIndexes must be an array."
        $sourceIndexes = @($requirement.sourceRequestIndexes)
        Assert-Condition ($sourceIndexes.Count -gt 0) "JSON_EMPTY_ARRAY" "$location.sourceRequestIndexes must not be empty."
        $seenIndexes = New-Object 'System.Collections.Generic.HashSet[int]'
        foreach ($sourceIndex in $sourceIndexes) {
            $isInteger = $sourceIndex -is [byte] -or $sourceIndex -is [int16] -or $sourceIndex -is [int32] -or $sourceIndex -is [int64]
            Assert-Condition $isInteger "SOURCE_INDEX" "$location.sourceRequestIndexes contains a non-integer value."
            $numericIndex = [int]$sourceIndex
            Assert-Condition ($numericIndex -ge 0 -and $numericIndex -lt $requests.Count) "SOURCE_INDEX" "$location.sourceRequestIndexes contains out-of-range index $numericIndex."
            Assert-Condition ($seenIndexes.Add($numericIndex)) "DUPLICATE_SOURCE_INDEX" "$location.sourceRequestIndexes repeats index $numericIndex."
        }
        Assert-String $requirement.sourceQuote "$location.sourceQuote"
        $quoteFound = $false
        foreach ($sourceIndex in $sourceIndexes) {
            if (([string]$requests[[int]$sourceIndex].text).Contains([string]$requirement.sourceQuote)) {
                $quoteFound = $true
                break
            }
        }
        Assert-Condition $quoteFound "SOURCE_QUOTE" "$location.sourceQuote is not an exact substring of any referenced original request."

        Assert-StringArray $requirement.acceptanceCriteria "$location.acceptanceCriteria"
        Assert-StringArray $requirement.implementationPaths "$location.implementationPaths"
        $paths = @($requirement.implementationPaths)
        for ($pathIndex = 0; $pathIndex -lt $paths.Count; $pathIndex++) {
            Assert-ImplementationPath ([string]$paths[$pathIndex]) "$location.implementationPaths[$pathIndex]"
        }

        Assert-Condition (Test-IsArray $requirement.verification) "JSON_ARRAY" "$location.verification must be an array."
        $verifications = @($requirement.verification)
        Assert-Condition ($verifications.Count -gt 0) "JSON_EMPTY_ARRAY" "$location.verification must not be empty."
        for ($verificationIndex = 0; $verificationIndex -lt $verifications.Count; $verificationIndex++) {
            $verification = $verifications[$verificationIndex]
            $verificationLocation = "$location.verification[$verificationIndex]"
            $verificationProperties = @("id", "kind", "command", "expected", "result", "evidence")
            Assert-ObjectShape $verification $verificationProperties $verificationProperties $verificationLocation
            Assert-String $verification.id "$verificationLocation.id"
            Assert-Condition ($verification.id -cmatch '^VER-[0-9]{3}$') "VERIFICATION_ID" "$verificationLocation.id has an invalid format."
            Assert-Condition ($verificationIds.Add([string]$verification.id)) "DUPLICATE_ID" "$verificationLocation.id duplicates '$($verification.id)'."
            Assert-Enum $verification.kind @("test", "build", "inspection", "runtime") "$verificationLocation.kind"
            Assert-String $verification.command "$verificationLocation.command"
            Assert-String $verification.expected "$verificationLocation.expected"
            Assert-Enum $verification.result @("pending", "passed", "failed") "$verificationLocation.result"
            Assert-String $verification.evidence "$verificationLocation.evidence" -AllowEmpty
        }
    }

    Assert-ExceptionArray $Contract.assumptions "assumptions" "ASM"
    Assert-ExceptionArray $Contract.deviations "deviations" "DEV"
    Assert-ApprovalShape $Contract.approval "approval"

    $reviewProperties = @("reviewer", "comparedOriginalRequests", "verdict", "findings")
    Assert-ObjectShape $Contract.independentReview $reviewProperties $reviewProperties "independentReview"
    Assert-String $Contract.independentReview.reviewer "independentReview.reviewer"
    Assert-Condition ($Contract.independentReview.reviewer -cne $Contract.author) "REVIEWER_INDEPENDENCE" "independentReview.reviewer must differ from author."
    Assert-Condition ($Contract.independentReview.comparedOriginalRequests -is [bool]) "JSON_BOOLEAN" "independentReview.comparedOriginalRequests must be a boolean."
    Assert-Enum $Contract.independentReview.verdict @("pending", "pass", "fail") "independentReview.verdict"
    Assert-StringArray $Contract.independentReview.findings "independentReview.findings" -AllowEmpty
}

function Get-SemanticPayload {
    param($Contract)

    $requests = @()
    foreach ($request in @($Contract.originalRequests)) {
        $requests += [ordered]@{ source = [string]$request.source; text = [string]$request.text }
    }
    $scope = [ordered]@{
        goal = [string]$Contract.scope.goal
        inScope = @($Contract.scope.inScope | ForEach-Object { [string]$_ })
        outOfScope = @($Contract.scope.outOfScope | ForEach-Object { [string]$_ })
        mode = [string]$Contract.scope.mode
        periodOrFilter = [string]$Contract.scope.periodOrFilter
        firstVerification = [string]$Contract.scope.firstVerification
    }
    $requirements = @()
    foreach ($requirement in @($Contract.requirements)) {
        $verifications = @()
        foreach ($verification in @($requirement.verification)) {
            $verifications += [ordered]@{
                id = [string]$verification.id
                kind = [string]$verification.kind
                command = [string]$verification.command
                expected = [string]$verification.expected
            }
        }
        $requirements += [ordered]@{
            id = [string]$requirement.id
            text = [string]$requirement.text
            sourceRequestIndexes = @($requirement.sourceRequestIndexes | ForEach-Object { [int64]$_ })
            sourceQuote = [string]$requirement.sourceQuote
            acceptanceCriteria = @($requirement.acceptanceCriteria | ForEach-Object { [string]$_ })
            implementationPaths = @($requirement.implementationPaths | ForEach-Object { [string]$_ })
            verification = $verifications
        }
    }
    $assumptions = @()
    foreach ($item in @($Contract.assumptions)) {
        $assumptions += [ordered]@{ id = [string]$item.id; text = [string]$item.text; impact = [string]$item.impact }
    }
    $deviations = @()
    foreach ($item in @($Contract.deviations)) {
        $deviations += [ordered]@{ id = [string]$item.id; text = [string]$item.text; impact = [string]$item.impact }
    }
    return [ordered]@{
        originalRequests = $requests
        scope = $scope
        requirements = $requirements
        assumptions = $assumptions
        deviations = $deviations
    }
}

function ConvertTo-CanonicalJsonString {
    param([string]$Value)

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

function ConvertTo-CanonicalJson {
    param($Value)

    if ($null -eq $Value) {
        return 'null'
    }
    if ($Value -is [string]) {
        return ConvertTo-CanonicalJsonString ([string]$Value)
    }
    if ($Value -is [bool]) {
        if ($Value) { return 'true' }
        return 'false'
    }
    if ($Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or
        $Value -is [uint16] -or $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]) {
        return ([System.Convert]::ToString($Value, [System.Globalization.CultureInfo]::InvariantCulture))
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $members = @()
        foreach ($key in $Value.Keys) {
            $members += (ConvertTo-CanonicalJsonString ([string]$key)) + ':' + (ConvertTo-CanonicalJson $Value[$key])
        }
        return '{' + ($members -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) {
            $items += ConvertTo-CanonicalJson $item
        }
        return '[' + ($items -join ',') + ']'
    }
    Stop-Gate -Code "CANONICAL_JSON_TYPE" -Message "Semantic payload contains unsupported type '$($Value.GetType().FullName)'."
}

function Get-SemanticDigest {
    param($Contract)

    $payload = Get-SemanticPayload $Contract
    $canonicalJson = ConvertTo-CanonicalJson $payload
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($canonicalJson)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($bytes)
    }
    finally {
        $sha256.Dispose()
    }
    $hex = -join ($hash | ForEach-Object { $_.ToString("x2", [System.Globalization.CultureInfo]::InvariantCulture) })
    return "sha256:$hex"
}

function Assert-ApprovedApproval {
    param(
        $Approval,
        $Contract,
        [string]$Digest,
        [string]$Location,
        [switch]$AllowBootstrap
    )

    Assert-Condition ($Approval.semanticDigest -ceq $Digest) "APPROVAL_DIGEST_MISMATCH" "$Location.semanticDigest does not match the contract semantic digest '$Digest'."
    $normalEvidence = "APPROVE $($Contract.contractId) $Digest"
    if ($Approval.status -ceq "approved") {
        Assert-Condition ($Approval.evidenceText -ceq $normalEvidence) "APPROVAL_EVIDENCE" "$Location.evidenceText must be exactly '$normalEvidence'."
        return
    }
    if ($Approval.status -ceq "bootstrap-approved") {
        Assert-Condition ($AllowBootstrap.IsPresent) "BOOTSTRAP_DISABLED" "$Location uses bootstrap approval without -AllowBootstrapContract."
        Assert-Condition ($Contract.contractId -ceq $script:BootstrapContractId) "BOOTSTRAP_CONTRACT_ID" "Bootstrap approval is restricted to '$($script:BootstrapContractId)'."
        $matchesOriginal = @($Contract.originalRequests | Where-Object { $_.text -ceq $Approval.evidenceText }).Count -gt 0
        Assert-Condition $matchesOriginal "BOOTSTRAP_EVIDENCE" "$Location.evidenceText must exactly equal one verbatim original user request."
        return
    }
    Stop-Gate -Code "APPROVAL_STATUS" -Message "$Location.status must be 'approved'."
}

function Assert-ContractLifecycle {
    param(
        $Contract,
        [string]$Digest,
        [string]$ValidationMode,
        [switch]$DraftAllowed,
        [switch]$PendingAllowed,
        [switch]$BootstrapAllowed
    )

    if ($DraftAllowed.IsPresent -and $Contract.status -ceq "draft") {
        return
    }

    Assert-ApprovedApproval $Contract.approval $Contract $Digest "approval" -AllowBootstrap:$BootstrapAllowed
    foreach ($kind in @("assumptions", "deviations")) {
        foreach ($item in @($Contract.$kind)) {
            Assert-ApprovedApproval $item.approval $Contract $Digest "$kind.$($item.id).approval"
        }
    }

    if ($PendingAllowed.IsPresent) {
        Assert-Condition (@("approved", "completed") -ccontains [string]$Contract.status) "CONTRACT_STATUS" "Contract status must be 'approved' or 'completed' when pending evidence is allowed."
        foreach ($requirement in @($Contract.requirements)) {
            foreach ($verification in @($requirement.verification)) {
                Assert-Condition ($verification.result -cne "failed") "VERIFICATION_FAILED" "Verification '$($verification.id)' is failed."
                if ($verification.result -ceq "passed") {
                    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$verification.evidence)) "VERIFICATION_EVIDENCE" "Passed verification '$($verification.id)' must contain evidence."
                }
            }
        }
        if ($Contract.status -ceq "completed") {
            Assert-Condition ($Contract.independentReview.comparedOriginalRequests -eq $true) "REVIEW_INCOMPLETE" "Completed contract review must compare original requests."
            Assert-Condition ($Contract.independentReview.verdict -ceq "pass") "REVIEW_VERDICT" "Completed contract review verdict must be 'pass'."
            Assert-Condition (@($Contract.independentReview.findings).Count -eq 0) "REVIEW_FINDINGS" "Completed contract review must have no open findings."
        }
        return
    }

    Assert-Condition ($Contract.status -ceq "completed") "CONTRACT_STATUS" "Contract status must be 'completed' for $ValidationMode validation."
    foreach ($requirement in @($Contract.requirements)) {
        foreach ($verification in @($requirement.verification)) {
            Assert-Condition ($verification.result -ceq "passed") "VERIFICATION_RESULT" "Verification '$($verification.id)' must be 'passed'."
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$verification.evidence)) "VERIFICATION_EVIDENCE" "Passed verification '$($verification.id)' must contain evidence."
        }
    }
    Assert-Condition ($Contract.independentReview.comparedOriginalRequests -eq $true) "REVIEW_INCOMPLETE" "independentReview.comparedOriginalRequests must be true."
    Assert-Condition ($Contract.independentReview.verdict -ceq "pass") "REVIEW_VERDICT" "independentReview.verdict must be 'pass'."
    Assert-Condition (@($Contract.independentReview.findings).Count -eq 0) "REVIEW_FINDINGS" "independentReview.findings must be empty when verdict is 'pass'."
}

function Get-ContractAtRef {
    param([string]$Root, [string]$Ref, [string]$Path)

    Assert-Condition (Test-GitObjectExists -Root $Root -ObjectSpec "$Ref`:$Path") "CONTRACT_MISSING" "Contract '$Path' does not exist at '$Ref'."
    $json = Get-GitText -Root $Root -ObjectSpec "$Ref`:$Path"
    return Read-ContractJson $json $Path
}

function Get-ContractPathsAtRef {
    param([string]$Root, [string]$Ref)

    $paths = Get-GitLines -Root $Root -Arguments @("-c", "core.quotepath=false", "ls-tree", "-r", "--name-only", $Ref, "--", "Codex/Requirements/Contracts") -Operation "List contracts at '$Ref'"
    return @($paths | Where-Object { Test-IsContractRecordPath $_ })
}

function Test-TargetPathExists {
    param([string]$Root, [string]$ValidationMode, [string]$Path, [string]$TargetRef)

    if ($ValidationMode -ceq "WorkingTree") {
        $absolute = Join-Path $Root ($Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        return Test-Path -LiteralPath $absolute -PathType Leaf
    }
    if ($ValidationMode -ceq "Staged") {
        return Test-GitObjectExists -Root $Root -ObjectSpec ":$Path"
    }
    return Test-GitObjectExists -Root $Root -ObjectSpec "$TargetRef`:$Path"
}

function Assert-SameContractIdentity {
    param($Before, $After, [string]$Path)

    Assert-Condition ($Before.contractId -ceq $After.contractId) "CONTRACT_ID_CHANGED" "Contract '$Path' cannot change contractId."
    Assert-Condition ($Before.title -ceq $After.title) "CONTRACT_TITLE_CHANGED" "Contract '$Path' cannot change title during implementation."
    Assert-Condition ($Before.author -ceq $After.author) "CONTRACT_AUTHOR_CHANGED" "Contract '$Path' cannot change author during implementation."
}

function Add-CoverageContract {
    param([System.Collections.ArrayList]$List, $Contract)

    [void]$List.Add($Contract)
}

function Assert-Coverage {
    param([string[]]$GovernedPaths, [System.Collections.ArrayList]$Contracts)

    foreach ($path in $GovernedPaths) {
        $coveredBy = @()
        foreach ($contract in @($Contracts)) {
            foreach ($requirement in @($contract.requirements)) {
                foreach ($pattern in @($requirement.implementationPaths)) {
                    if (Test-PathCovered $path ([string]$pattern)) {
                        $coveredBy += "$($contract.contractId)/$($requirement.id)"
                    }
                }
            }
        }
        Assert-Condition ($coveredBy.Count -gt 0) "PATH_NOT_COVERED" "Changed path '$path' is not covered by any pre-approved requirement implementationPaths entry."
    }
}

function Test-IsBootstrapCandidate {
    param($Contract)

    return $AllowBootstrapContract.IsPresent -and
        $Contract.contractId -ceq $script:BootstrapContractId -and
        $Contract.approval.status -ceq "bootstrap-approved"
}

function Test-ChangeSet {
    param(
        [string]$Root,
        [string]$ValidationMode,
        [string]$BeforeRef,
        [string]$TargetRef,
        [switch]$PendingWorkingTreeAllowed
    )

    $changedPaths = @(Get-ChangedPaths $Root $ValidationMode $BeforeRef $TargetRef)
    if ($changedPaths.Count -eq 0) {
        return [ordered]@{ governedFiles = 0; contracts = 0 }
    }

    $contractPaths = @($changedPaths | Where-Object { Test-IsContractRecordPath $_ })
    $governedPaths = @($changedPaths | Where-Object { -not (Test-IsExemptPath $_) })
    $hasGovernedChanges = $governedPaths.Count -gt 0
    $coverageContracts = New-Object System.Collections.ArrayList

    foreach ($path in $contractPaths) {
        $baseExists = Test-GitObjectExists -Root $Root -ObjectSpec "$BeforeRef`:$path"
        $targetExists = Test-TargetPathExists $Root $ValidationMode $path $TargetRef
        Assert-Condition $targetExists "CONTRACT_DELETED" "Contract record '$path' cannot be deleted."

        $json = Get-ContractSource $Root $ValidationMode $path $TargetRef
        $candidate = Read-ContractJson $json $path
        Assert-ContractStructure $candidate $path
        $candidateDigest = Get-SemanticDigest $candidate

        if (-not $baseExists) {
            if (-not $hasGovernedChanges) {
                if ($ValidationMode -ceq "WorkingTree" -and $candidate.status -ceq "draft") {
                    continue
                }
                Assert-Condition ($candidate.status -ceq "approved") "APPROVAL_COMMIT_STATUS" "A new non-bootstrap contract must be committed as 'approved' before implementation."
                Assert-ContractLifecycle $candidate $candidateDigest $ValidationMode -PendingAllowed
                continue
            }

            Assert-Condition (Test-IsBootstrapCandidate $candidate) "CONTRACT_NOT_PREAPPROVED" "Governed changes require a contract already committed as approved; '$path' is new in the same change set."
            Assert-ContractLifecycle $candidate $candidateDigest $ValidationMode -BootstrapAllowed
            Add-CoverageContract $coverageContracts $candidate
            continue
        }

        $baseContract = Get-ContractAtRef $Root $BeforeRef $path
        Assert-ContractStructure $baseContract $path
        $baseDigest = Get-SemanticDigest $baseContract
        Assert-Condition ($baseContract.status -cne "completed") "CONTRACT_IMMUTABLE" "Completed contract record '$path' is immutable."
        Assert-Condition ($baseContract.status -ceq "approved") "CONTRACT_BASE_STATUS" "Existing contract '$path' must be approved before implementation."
        Assert-ContractLifecycle $baseContract $baseDigest $ValidationMode -PendingAllowed

        if (-not $hasGovernedChanges) {
            Assert-Condition ($candidate.status -ceq "approved") "COMPLETION_WITHOUT_IMPLEMENTATION" "Contract '$path' may become completed only in the same change set as its governed implementation."
            Assert-ContractLifecycle $candidate $candidateDigest $ValidationMode -PendingAllowed
            continue
        }

        Assert-SameContractIdentity $baseContract $candidate $path
        Assert-Condition ($candidateDigest -ceq $baseDigest) "SEMANTIC_CHANGE_AFTER_APPROVAL" "Contract '$path' semantics changed after approval; create an approval-only change first."
        Assert-Condition ($candidate.approval.semanticDigest -ceq $baseContract.approval.semanticDigest) "APPROVAL_CHANGED_DURING_IMPLEMENTATION" "Contract '$path' approval digest changed during implementation."

        if ($PendingWorkingTreeAllowed.IsPresent -and $candidate.status -ceq "approved") {
            Assert-ContractLifecycle $candidate $candidateDigest $ValidationMode -PendingAllowed
        }
        else {
            Assert-Condition ($candidate.status -ceq "completed") "IMPLEMENTATION_CONTRACT_STATUS" "Contract '$path' must transition from approved to completed with its implementation."
            Assert-ContractLifecycle $candidate $candidateDigest $ValidationMode
        }
        Add-CoverageContract $coverageContracts $candidate
    }

    if ($hasGovernedChanges -and $PendingWorkingTreeAllowed.IsPresent) {
        $changedContractSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
        foreach ($path in $contractPaths) { [void]$changedContractSet.Add($path) }
        foreach ($path in @(Get-ContractPathsAtRef $Root $BeforeRef)) {
            if ($changedContractSet.Contains($path)) {
                continue
            }
            $baseContract = Get-ContractAtRef $Root $BeforeRef $path
            Assert-ContractStructure $baseContract $path
            if ($baseContract.status -cne "approved") {
                continue
            }
            $baseDigest = Get-SemanticDigest $baseContract
            Assert-ContractLifecycle $baseContract $baseDigest $ValidationMode -PendingAllowed
            Add-CoverageContract $coverageContracts $baseContract
        }
    }

    if ($hasGovernedChanges) {
        Assert-Condition ($coverageContracts.Count -gt 0) "CONTRACT_REQUIRED" "Governed changes require a pre-approved contract transitioning to completed."
        Assert-Coverage $governedPaths $coverageContracts
    }

    return [ordered]@{ governedFiles = $governedPaths.Count; contracts = $contractPaths.Count }
}

function Invoke-Main {
    if ($AllowDraft.IsPresent -and $Mode -cne "Contract") {
        Stop-Gate -Code "PARAMETER_MODE" -Message "-AllowDraft is valid only in Contract mode."
    }
    if ($PrintSemanticDigest.IsPresent -and $Mode -cne "Contract") {
        Stop-Gate -Code "PARAMETER_MODE" -Message "-PrintSemanticDigest is valid only in Contract mode."
    }
    if ($AllowPendingEvidence.IsPresent -and @("Contract", "WorkingTree") -cnotcontains $Mode) {
        Stop-Gate -Code "PARAMETER_MODE" -Message "-AllowPendingEvidence is valid only in Contract or WorkingTree mode."
    }

    $root = Resolve-RepositoryRoot $RepositoryRoot
    if ($Mode -ceq "Range") {
        Assert-GitRef $root $BaseRef "BaseRef"
        Assert-GitRef $root $HeadRef "HeadRef"
    }
    elseif ($Mode -cne "Contract") {
        Assert-GitRef $root "HEAD" "HEAD"
    }

    if ($Mode -ceq "Contract") {
        $relativePath = Resolve-ContractRelativePath $root $ContractPath
        $json = Get-ContractSource $root "Contract" $relativePath $null
        $contract = Read-ContractJson $json $relativePath
        Assert-ContractStructure $contract $relativePath
        $digest = Get-SemanticDigest $contract
        Assert-ContractLifecycle $contract $digest $Mode -DraftAllowed:$AllowDraft -PendingAllowed:$AllowPendingEvidence -BootstrapAllowed:$AllowBootstrapContract
        if ($PrintSemanticDigest.IsPresent) {
            Write-Output $digest
        }
        Write-Host "REQUIREMENT_GATE_OK mode=Contract contract=$relativePath digest=$digest"
        return
    }

    if ($Mode -ceq "Range") {
        & git -C $root merge-base --is-ancestor $BaseRef $HeadRef 2>$null
        Assert-Condition ($LASTEXITCODE -eq 0) "RANGE_ANCESTRY" "BaseRef '$BaseRef' must be an ancestor of HeadRef '$HeadRef'."
        $commits = @(Get-GitLines -Root $root -Arguments @("rev-list", "--reverse", "--topo-order", "$BaseRef..$HeadRef") -Operation "Enumerate validation commits")
        $totalGoverned = 0
        $totalContracts = 0
        foreach ($commit in $commits) {
            $parentLine = @(Get-GitLines -Root $root -Arguments @("rev-list", "--parents", "-n", "1", $commit) -Operation "Resolve parent of '$commit'")
            $parts = $parentLine[0].Split(@(' '), [System.StringSplitOptions]::RemoveEmptyEntries)
            Assert-Condition ($parts.Count -ge 2) "RANGE_ROOT_COMMIT" "Range validation does not accept a root commit ('$commit')."
            $parent = $parts[1]
            $result = Test-ChangeSet $root "Range" $parent $commit
            $totalGoverned += [int]$result.governedFiles
            $totalContracts += [int]$result.contracts
        }
        Write-Host "REQUIREMENT_GATE_OK mode=Range commits=$($commits.Count) governedFiles=$totalGoverned contracts=$totalContracts"
        return
    }

    $beforeRef = "HEAD"
    $result = Test-ChangeSet $root $Mode $beforeRef $HeadRef -PendingWorkingTreeAllowed:($Mode -ceq "WorkingTree" -and $AllowPendingEvidence.IsPresent)
    Write-Host "REQUIREMENT_GATE_OK mode=$Mode governedFiles=$($result.governedFiles) contracts=$($result.contracts)"
}

try {
    Invoke-Main
    # A handled negative Git probe (for example `cat-file -e` against a path
    # absent from HEAD) must not leak its native exit code to an in-process
    # caller after the gate itself succeeded.
    $global:LASTEXITCODE = 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    if ($env:REQUIREMENT_GATE_DEBUG -eq "1") {
        [Console]::Error.WriteLine($_.ScriptStackTrace)
    }
    exit 1
}
