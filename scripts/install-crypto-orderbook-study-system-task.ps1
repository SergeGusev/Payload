[CmdletBinding()]
param(
    [string]$SourceRunnerDirectory = 'D:\My\Business\PolyMarket\outputs\crypto-orderbook-prediction\runner-f98b8bda',

    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedServiceSha256 = '045FD2343AF0BDB7D34291B5BCE42020A77817B9CE692B457FA8C53776982E23',

    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedRunnerManifestSha256 = '89E83DA39A5C75DBFAC84FB99C9506AF379E6C96992FEF53AE345C11DEEA9EB0',

    [string]$SourceSupervisorPath,

    [string]$SourceWatchdogPath,

    [string]$RuntimeBase = 'C:\Program Files\PolyCopyTrader\OrderBookStudy',

    [string]$ControlRoot = 'C:\ProgramData\PolyCopyTrader\OrderBookStudy',

    [string]$OutputRoot = 'D:\PolyCopyTraderOrderBookStudy\data',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$TaskName = 'PolyCopyTrader-CryptoOrderBook-Cohort',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$WatchdogTaskName = 'PolyCopyTrader-CryptoOrderBook-Watchdog',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$CampaignId,

    [ValidateRange(10, 604800)]
    [int]$DurationSeconds = 259200,

    [ValidateRange(60, 3600)]
    [int]$WatchdogStaleSeconds = 180,

    [ValidateRange(30, 3600)]
    [int]$WatchdogStartupGraceSeconds = 180,

    [ValidateRange(300, 7200)]
    [int]$WatchdogCompletionValidationGraceSeconds = 1800,

    [ValidateRange(10, 3600)]
    [int]$CheckpointStartGraceSeconds = 600,

    [string]$LegacyTaskName = 'PolyCopyTrader-CryptoOrderBook-Cohort-f98b8bda-20260723',

    [switch]$ValidateOnly,

    [switch]$StartAfterInstall,

    [switch]$DisableLegacyTask,

    [switch]$ReplaceExistingTasks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
$powerShellPath = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$serviceFileName = 'PolyCopyTrader.Service.exe'
$supervisorFileName = 'run-crypto-orderbook-study-cohort.ps1'
$watchdogFileName = 'watch-crypto-orderbook-study-task.ps1'
$managedMarkerFileName = '.polycop-orderbook-managed.json'
$expectedRuntimeBase = 'C:\Program Files\PolyCopyTrader\OrderBookStudy'
$expectedControlRoot = 'C:\ProgramData\PolyCopyTrader\OrderBookStudy'
$expectedOutputBase = 'D:\PolyCopyTraderOrderBookStudy'
$expectedOutputRoot = 'D:\PolyCopyTraderOrderBookStudy\data'
if ([string]::IsNullOrWhiteSpace($SourceSupervisorPath)) {
    $SourceSupervisorPath = Join-Path $PSScriptRoot $supervisorFileName
}

if ([string]::IsNullOrWhiteSpace($SourceWatchdogPath)) {
    $SourceWatchdogPath = Join-Path $PSScriptRoot $watchdogFileName
}

if ([string]::Equals($TaskName, $WatchdogTaskName, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'TaskName and WatchdogTaskName must be distinct.'
}

if ($DisableLegacyTask -and
    ([string]::Equals($LegacyTaskName, $TaskName, [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($LegacyTaskName, $WatchdogTaskName, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'LegacyTaskName must be distinct from both new task names.'
}

$systemSid = New-Object System.Security.Principal.SecurityIdentifier -ArgumentList 'S-1-5-18'
$localServiceSid = New-Object System.Security.Principal.SecurityIdentifier -ArgumentList 'S-1-5-19'
$administratorsSid = New-Object System.Security.Principal.SecurityIdentifier -ArgumentList 'S-1-5-32-544'
$authenticatedUsersSid = New-Object System.Security.Principal.SecurityIdentifier -ArgumentList 'S-1-5-11'
$usersSid = New-Object System.Security.Principal.SecurityIdentifier -ArgumentList 'S-1-5-32-545'
$everyoneSid = New-Object System.Security.Principal.SecurityIdentifier -ArgumentList 'S-1-1-0'
$readerSid = $usersSid
$taskSnapshots = @{}
$legacyTaskSnapshot = $null
$heartbeatEventLogName = 'PolyCopyTrader-OrderBookStudy'
$heartbeatEventSource = 'PolyCopyTraderOrderBookStudy'
$heartbeatSupervisorEventId = 4100
$heartbeatWatchdogEventId = 4110
$heartbeatEventLogRegistryPath = 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\EventLog\' + $heartbeatEventLogName
$heartbeatEventLogMarkerName = 'PolyCopyTraderManagedBy'
$heartbeatEventLogMarkerValue = 'PolyCopyTrader.OrderBookStudy.v1'
$heartbeatEventLogMaximumBytes = 67108864
$heartbeatEventLogSddl = 'O:BAG:SYD:' +
    '(D;;0x7;;;AN)' +
    '(D;;0x7;;;BG)' +
    '(A;;0xf0007;;;SY)' +
    '(A;;0x7;;;BA)' +
    '(A;;0x3;;;LS)' +
    '(A;;0x1;;;' + $usersSid.Value + ')'
$heartbeatEventLogCreated = $false
if ($null -eq (Get-Command New-ScheduledTask -ErrorAction SilentlyContinue)) {
    $scheduledTasksModule = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\Modules\ScheduledTasks\ScheduledTasks.psd1'
    Import-Module -Name $scheduledTasksModule -ErrorAction Stop
}

function Resolve-SafeAbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [switch]$Directory
    )

    if (-not [System.IO.Path]::IsPathRooted($Path) -or $Path.Contains('"')) {
        throw "$Name must be an absolute path without quotation marks."
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $root = [System.IO.Path]::GetPathRoot($fullPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if ($Directory -and [string]::Equals($fullPath, $root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must not be a drive root."
    }

    return $fullPath
}

function Assert-NoReparsePoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $item = Get-Item -LiteralPath $Path -Force
    while ($null -ne $item) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Name contains a reparse point: $($item.FullName)"
        }

        $item = if ($item -is [System.IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
    }
}

function Assert-PowerShellSyntax {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $Path,
        [ref]$tokens,
        [ref]$errors) | Out-Null
    if (@($errors).Count -gt 0) {
        throw "PowerShell syntax validation failed for ${Path}: $($errors[0])"
    }
}

function Get-SecurityDescriptorBinaryHex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Sddl
    )

    $descriptor = New-Object System.Security.AccessControl.RawSecurityDescriptor -ArgumentList $Sddl
    $binary = New-Object byte[] $descriptor.BinaryLength
    $descriptor.GetBinaryForm($binary, 0)
    return ([BitConverter]::ToString($binary)).Replace('-', '')
}

function Get-HeartbeatEventLogConfiguration {
    $wevtutil = Join-Path $env:SystemRoot 'System32\wevtutil.exe'
    $xmlText = & $wevtutil gl $heartbeatEventLogName /f:xml
    if ($LASTEXITCODE -ne 0) {
        throw "wevtutil could not read $heartbeatEventLogName (exit $LASTEXITCODE)."
    }

    [xml]$configuration = ($xmlText -join [Environment]::NewLine)
    return $configuration.channel
}

function Assert-HeartbeatEventLogConfiguration {
    if (-not (Test-Path -LiteralPath $heartbeatEventLogRegistryPath -PathType Container)) {
        throw "Managed heartbeat event log is missing: $heartbeatEventLogName"
    }

    $marker = Get-ItemPropertyValue `
        -LiteralPath $heartbeatEventLogRegistryPath `
        -Name $heartbeatEventLogMarkerName `
        -ErrorAction SilentlyContinue
    if (-not [string]::Equals([string]$marker, $heartbeatEventLogMarkerValue, [StringComparison]::Ordinal)) {
        throw "Existing event log $heartbeatEventLogName is not marked as this installer's managed channel."
    }

    if (-not [System.Diagnostics.EventLog]::SourceExists($heartbeatEventSource) -or
        -not [string]::Equals(
            [System.Diagnostics.EventLog]::LogNameFromSourceName($heartbeatEventSource, '.'),
            $heartbeatEventLogName,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Heartbeat source $heartbeatEventSource is not registered in $heartbeatEventLogName."
    }

    $configuration = Get-HeartbeatEventLogConfiguration
    if (-not [string]::Equals([string]$configuration.enabled, 'true', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$configuration.logging.retention, 'false', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$configuration.logging.autoBackup, 'false', [StringComparison]::OrdinalIgnoreCase) -or
        [long]$configuration.logging.maxSize -ne $heartbeatEventLogMaximumBytes -or
        -not [string]::Equals(
            (Get-SecurityDescriptorBinaryHex -Sddl ([string]$configuration.channelAccess)),
            (Get-SecurityDescriptorBinaryHex -Sddl $heartbeatEventLogSddl),
            [StringComparison]::Ordinal)) {
        throw "Managed heartbeat event log configuration or channel ACL is not exact: $heartbeatEventLogName"
    }
}

function Install-HeartbeatEventLog {
    if (Test-Path -LiteralPath $heartbeatEventLogRegistryPath -PathType Container) {
        Assert-HeartbeatEventLogConfiguration
        return $false
    }

    $created = $false
    try {
        New-EventLog `
            -LogName $heartbeatEventLogName `
            -Source $heartbeatEventSource `
            -ErrorAction Stop
        $created = $true
        New-ItemProperty `
            -LiteralPath $heartbeatEventLogRegistryPath `
            -Name $heartbeatEventLogMarkerName `
            -PropertyType String `
            -Value $heartbeatEventLogMarkerValue `
            -Force | Out-Null

        $wevtutil = Join-Path $env:SystemRoot 'System32\wevtutil.exe'
        & $wevtutil sl $heartbeatEventLogName /e:true /ms:$heartbeatEventLogMaximumBytes /rt:false /ab:false /ca:$heartbeatEventLogSddl
        if ($LASTEXITCODE -ne 0) {
            throw "wevtutil failed to protect $heartbeatEventLogName (exit $LASTEXITCODE)."
        }

        Assert-HeartbeatEventLogConfiguration
        return $true
    }
    catch {
        $failure = $_.Exception.Message
        if ($created -and
            (Test-Path -LiteralPath $heartbeatEventLogRegistryPath -PathType Container)) {
            try {
                Remove-EventLog -LogName $heartbeatEventLogName -ErrorAction Stop
            }
            catch {
                throw "Heartbeat event-log setup failed: $failure Cleanup also failed: $($_.Exception.Message)"
            }
        }

        throw "Heartbeat event-log setup failed: $failure"
    }
}

function Remove-NewHeartbeatEventLog {
    if (-not $script:heartbeatEventLogCreated) {
        return $null
    }

    try {
        $marker = Get-ItemPropertyValue `
            -LiteralPath $heartbeatEventLogRegistryPath `
            -Name $heartbeatEventLogMarkerName `
            -ErrorAction SilentlyContinue
        if (-not [string]::Equals([string]$marker, $heartbeatEventLogMarkerValue, [StringComparison]::Ordinal)) {
            throw 'The newly created heartbeat event-log marker changed; automatic removal was refused.'
        }

        Remove-EventLog -LogName $heartbeatEventLogName -ErrorAction Stop
        if (Test-Path -LiteralPath $heartbeatEventLogRegistryPath -PathType Container) {
            throw 'The newly created heartbeat event log still exists after removal.'
        }

        $script:heartbeatEventLogCreated = $false
        return $null
    }
    catch {
        return $_.Exception.Message
    }
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hash)).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Get-DirectoryContentManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $prefix = $Path.TrimEnd('\') + '\'
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse -Force | ForEach-Object {
        [ordered]@{
            RelativePath = $_.FullName.Substring($prefix.Length).Replace('\', '/')
            Length = $_.Length
            Sha256 = (Get-Sha256 -Path $_.FullName).ToUpperInvariant()
        }
    } | Sort-Object RelativePath)
}

function Get-DirectoryManifestSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Manifest
    )

    [string[]]$lines = @($Manifest | ForEach-Object {
        '{0}|{1}|{2}' -f $_.RelativePath, $_.Length, $_.Sha256
    })
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    $payload = [string]::Join("`n", $lines)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload))
        return ([System.BitConverter]::ToString($hash)).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Assert-ExactDestinationPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not [string]::Equals(
        $Actual,
        [System.IO.Path]::GetFullPath($Expected).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name is outside the exact unattended-deployment allowlist. Expected: $Expected"
    }
}

function Assert-ManagedOrEmptyDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Role
    )

    $existingAncestor = $Path
    while (-not (Test-Path -LiteralPath $existingAncestor)) {
        $parent = [System.IO.Path]::GetDirectoryName($existingAncestor)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $existingAncestor, [StringComparison]::OrdinalIgnoreCase)) {
            throw "No existing ancestor could be resolved for protected destination: $Path"
        }

        $existingAncestor = $parent
    }
    Assert-NoReparsePoint -Path $existingAncestor -Name 'Protected destination ancestor'

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Protected destination exists but is not a directory: $Path"
    }

    Assert-NoReparsePoint -Path $Path -Name 'Protected destination'
    $markerPath = Join-Path $Path $managedMarkerFileName
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        try {
            $marker = [System.IO.File]::ReadAllText(
                $markerPath,
                [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            if ([int]$marker.SchemaVersion -ne 1 -or
                -not [string]::Equals([string]$marker.Role, $Role, [StringComparison]::Ordinal) -or
                -not [string]::Equals(
                    [System.IO.Path]::GetFullPath([string]$marker.Path).TrimEnd('\'),
                    $Path,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw 'marker identity does not match the requested protected destination.'
            }
        }
        catch {
            throw "Managed-directory marker is invalid at ${markerPath}: $($_.Exception.Message)"
        }

        return
    }

    if (@(Get-ChildItem -LiteralPath $Path -Force).Count -gt 0) {
        throw "Refusing to replace ACLs on unmanaged non-empty directory: $Path"
    }
}

function Write-ManagedDirectoryMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Role
    )

    $markerPath = Join-Path $Path $managedMarkerFileName
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        $marker = [ordered]@{
            SchemaVersion = 1
            Role = $Role
            Path = $Path
            CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        }
        [System.IO.File]::WriteAllText(
            $markerPath,
            ($marker | ConvertTo-Json -Depth 3),
            $utf8NoBom)
    }

    Set-ProtectedFileAcl -Path $markerPath
}

function Test-DirectoryContentEqual {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Expected,

        [Parameter(Mandatory = $true)]
        [object[]]$Actual
    )

    $expectedJson = $Expected | ConvertTo-Json -Depth 5 -Compress
    $actualJson = $Actual | ConvertTo-Json -Depth 5 -Compress
    return [string]::Equals($expectedJson, $actualJson, [StringComparison]::Ordinal)
}

function New-DirectoryAccessRule {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.Principal.SecurityIdentifier]$Sid,

        [Parameter(Mandatory = $true)]
        [System.Security.AccessControl.FileSystemRights]$Rights
    )

    $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    return New-Object System.Security.AccessControl.FileSystemAccessRule(
        $Sid,
        $Rights,
        $inheritance,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow)
}

function Set-ProtectedDirectoryAcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [System.Security.AccessControl.FileSystemRights]$LocalServiceRights
    )

    $security = New-Object System.Security.AccessControl.DirectorySecurity
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner($administratorsSid)
    $security.AddAccessRule((New-DirectoryAccessRule -Sid $systemSid -Rights ([System.Security.AccessControl.FileSystemRights]::FullControl)))
    $security.AddAccessRule((New-DirectoryAccessRule -Sid $administratorsSid -Rights ([System.Security.AccessControl.FileSystemRights]::FullControl)))
    $security.AddAccessRule((New-DirectoryAccessRule -Sid $localServiceSid -Rights $LocalServiceRights))
    if ($readerSid -ne $administratorsSid -and $readerSid -ne $systemSid -and $readerSid -ne $localServiceSid) {
        $security.AddAccessRule((New-DirectoryAccessRule -Sid $readerSid -Rights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)))
    }

    Set-Acl -LiteralPath $Path -AclObject $security
}

function Set-ProtectedFileAcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $security = New-Object System.Security.AccessControl.FileSecurity
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner($administratorsSid)
    foreach ($entry in @(
        @($systemSid, [System.Security.AccessControl.FileSystemRights]::FullControl),
        @($administratorsSid, [System.Security.AccessControl.FileSystemRights]::FullControl),
        @($localServiceSid, [System.Security.AccessControl.FileSystemRights]::ReadAndExecute),
        @($readerSid, [System.Security.AccessControl.FileSystemRights]::ReadAndExecute))) {
        $security.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            $entry[0],
            $entry[1],
            [System.Security.AccessControl.AccessControlType]::Allow)))
    }

    Set-Acl -LiteralPath $Path -AclObject $security
}

function Get-CanonicalAllowRights {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.AccessControl.FileSystemRights]$Rights
    )

    # Windows canonicalizes allow ACEs by adding Synchronize when the rule is
    # materialized. Compare with that round-tripped representation.
    return [System.Security.AccessControl.FileSystemRights](
        [int64]$Rights -bor
        [int64][System.Security.AccessControl.FileSystemRights]::Synchronize)
}

function Assert-ProtectedAcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [System.Security.AccessControl.FileSystemRights]$ExpectedLocalServiceRights
    )

    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw "ACL inheritance is still enabled: $Path"
    }

    $owner = $acl.GetOwner([System.Security.Principal.SecurityIdentifier])
    if ($owner -ne $administratorsSid) {
        throw "Protected path owner is not BUILTIN\Administrators: $Path"
    }

    $rules = @($acl.GetAccessRules(
        $true,
        $true,
        [System.Security.Principal.SecurityIdentifier]))
    $expectedRules = @{
        $systemSid.Value = Get-CanonicalAllowRights -Rights ([System.Security.AccessControl.FileSystemRights]::FullControl)
        $administratorsSid.Value = Get-CanonicalAllowRights -Rights ([System.Security.AccessControl.FileSystemRights]::FullControl)
        $localServiceSid.Value = Get-CanonicalAllowRights -Rights $ExpectedLocalServiceRights
    }
    if (-not $expectedRules.ContainsKey($readerSid.Value)) {
        $expectedRules[$readerSid.Value] = Get-CanonicalAllowRights -Rights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
    }

    $item = Get-Item -LiteralPath $Path -Force
    $expectedInheritance = if ($item -is [System.IO.DirectoryInfo]) {
        [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
            [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    } else {
        [System.Security.AccessControl.InheritanceFlags]::None
    }
    $seenSids = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Value
        if ($rule.IsInherited -or
            $rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow -or
            -not $expectedRules.ContainsKey($sid) -or
            -not $seenSids.Add($sid) -or
            [int64]$rule.FileSystemRights -ne [int64]$expectedRules[$sid] -or
            $rule.InheritanceFlags -ne $expectedInheritance -or
            $rule.PropagationFlags -ne [System.Security.AccessControl.PropagationFlags]::None) {
            throw "Protected path has an unexpected ACL entry for ${sid}: $Path"
        }
    }

    if ($seenSids.Count -ne $expectedRules.Count) {
        throw "Protected path ACL is missing one or more exact allowlist entries: $Path"
    }
}

function ConvertTo-NativeArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value.Contains('"')) {
        throw 'Scheduled-task arguments must not contain quotation marks.'
    }

    if ($Value.Length -eq 0 -or $Value -match '\s') {
        return '"' + $Value + '"'
    }

    return $Value
}

function Join-NativeArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Values
    )

    return ($Values | ForEach-Object { ConvertTo-NativeArgument -Value $_ }) -join ' '
}

function Assert-ExpectedExistingTask {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedPrincipal,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedActionPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedArguments
    )

    $existing = Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        return
    }

    if (-not [string]::Equals([string]$existing.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Existing task $Name must be Disabled before the installer can replace or reuse it. Current state: $($existing.State)."
    }

    $existingAction = @($existing.Actions)
    if ($existingAction.Count -ne 1) {
        throw "Existing task $Name must have exactly one action before it can be replaced or reused."
    }

    if (-not [string]::Equals([string]$existing.Principal.UserId, $ExpectedPrincipal, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$existing.Principal.LogonType, 'ServiceAccount', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Existing task $Name does not use the expected passwordless service-account principal and cannot be restored safely."
    }

    if (-not [string]::Equals([string]$existingAction[0].Execute, $powerShellPath, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$existingAction[0].Arguments, $ExpectedArguments, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$existingAction[0].WorkingDirectory, $runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([string]$existingAction[0].Arguments).Contains((ConvertTo-NativeArgument -Value $ExpectedActionPath))) {
        if (-not $ReplaceExistingTasks) {
            throw "Existing task $Name does not match this protected deployment. Use -ReplaceExistingTasks only after reviewing it."
        }

        $runtimePrefix = $resolvedRuntimeBase.TrimEnd('\') + '\'
        if (-not ([string]$existingAction[0].Arguments).Contains($runtimePrefix)) {
            throw "Existing task $Name is outside the protected runtime allowlist and will not be replaced."
        }
    }
}

function Assert-RegisteredTask {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedPrincipal,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedScriptPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedArguments,

        [Parameter(Mandatory = $true)]
        [bool]$ExpectedWakeToRun,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Main', 'Watchdog')]
        [string]$Kind
    )

    $task = Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop
    $actions = @($task.Actions)
    $triggers = @($task.Triggers)
    if ($actions.Count -ne 1 -or
        $triggers.Count -ne 1 -or
        -not [string]::Equals([string]$task.Principal.UserId, $ExpectedPrincipal, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$task.Principal.LogonType, 'ServiceAccount', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$task.Principal.RunLevel, 'Highest', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$actions[0].Execute, $powerShellPath, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$actions[0].Arguments, $ExpectedArguments, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$actions[0].WorkingDirectory, $runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([string]$actions[0].Arguments).Contains((ConvertTo-NativeArgument -Value $ExpectedScriptPath)) -or
        [bool]$task.Settings.WakeToRun -ne $ExpectedWakeToRun -or
        [bool]$task.Settings.DisallowStartIfOnBatteries -or
        [bool]$task.Settings.StopIfGoingOnBatteries -or
        -not [bool]$task.Settings.StartWhenAvailable -or
        -not [string]::Equals([string]$task.Settings.MultipleInstances, 'IgnoreNew', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Registered task $Name failed post-registration verification."
    }

    if ($Kind -eq 'Main') {
        if (-not [string]::Equals([string]$triggers[0].CimClass.CimClassName, 'MSFT_TaskBootTrigger', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$triggers[0].Delay, 'PT1M', [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$task.Settings.ExecutionTimeLimit, 'PT0S', [StringComparison]::Ordinal) -or
            [int]$task.Settings.RestartCount -ne 255 -or
            -not [string]::Equals([string]$task.Settings.RestartInterval, 'PT1M', [StringComparison]::Ordinal)) {
            throw "Registered main task $Name has invalid boot or restart settings."
        }
    }
    elseif (-not [string]::Equals([string]$triggers[0].Repetition.Interval, 'PT1M', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$task.Settings.ExecutionTimeLimit, 'PT30M', [StringComparison]::Ordinal) -or
        [int]$task.Settings.RestartCount -ne 3 -or
        -not [string]::Equals([string]$task.Settings.RestartInterval, 'PT1M', [StringComparison]::Ordinal)) {
        throw "Registered watchdog task $Name has invalid repetition or restart settings."
    }
}

function ConvertFrom-ManagedEventRecord {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Eventing.Reader.EventRecord]$EventRecord
    )

    foreach ($property in @($EventRecord.Properties)) {
        if ($null -eq $property.Value -or
            [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            continue
        }

        try {
            return [string]$property.Value | ConvertFrom-Json
        }
        catch {
        }
    }

    try {
        if (-not [string]::IsNullOrWhiteSpace([string]$EventRecord.Message)) {
            return [string]$EventRecord.Message | ConvertFrom-Json
        }
    }
    catch {
    }

    return $null
}

function Get-LatestManagedEvent {
    param(
        [Parameter(Mandatory = $true)]
        [int]$EventId,

        [Parameter(Mandatory = $true)]
        [string]$Kind,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCampaignId,

        [string]$ExpectedTaskName,

        [string]$ExpectedWatchdogTaskName,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$MinimumCreatedAtUtc
    )

    $queryErrors = @()
    $events = @(Get-WinEvent `
        -FilterHashtable @{
            LogName = $heartbeatEventLogName
            ProviderName = $heartbeatEventSource
            Id = $EventId
            StartTime = $MinimumCreatedAtUtc.UtcDateTime
        } `
        -MaxEvents 256 `
        -ErrorAction SilentlyContinue `
        -ErrorVariable queryErrors)
    $unexpectedQueryErrors = @($queryErrors | Where-Object {
        -not ([string]$_.FullyQualifiedErrorId).StartsWith('NoMatchingEventsFound', [StringComparison]::OrdinalIgnoreCase)
    })
    if ($unexpectedQueryErrors.Count -gt 0) {
        throw $unexpectedQueryErrors[0]
    }

    foreach ($eventRecord in $events) {
        try {
            if ($null -eq $eventRecord.TimeCreated) {
                continue
            }

            $createdAtUtc = [DateTimeOffset]($eventRecord.TimeCreated.ToUniversalTime())
            if ($createdAtUtc -lt $MinimumCreatedAtUtc) {
                continue
            }

            $state = ConvertFrom-ManagedEventRecord -EventRecord $eventRecord
            if ($null -eq $state -or
                [int]$state.SchemaVersion -ne 1 -or
                -not [string]::Equals([string]$state.Kind, $Kind, [StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$state.CampaignId, $ExpectedCampaignId, [StringComparison]::Ordinal) -or
                (-not [string]::IsNullOrWhiteSpace($ExpectedTaskName) -and
                    -not [string]::Equals([string]$state.TaskName, $ExpectedTaskName, [StringComparison]::Ordinal)) -or
                (-not [string]::IsNullOrWhiteSpace($ExpectedWatchdogTaskName) -and
                    -not [string]::Equals([string]$state.WatchdogTaskName, $ExpectedWatchdogTaskName, [StringComparison]::Ordinal))) {
                continue
            }

            return [pscustomobject]@{
                State = $state
                EventCreatedAtUtc = $createdAtUtc
                RecordId = [long]$eventRecord.RecordId
            }
        }
        catch {
        }
    }

    return $null
}

function Get-StartedCohortEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExpectedCampaignId,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedMainTaskName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedWatchdogTaskName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSupervisorScriptPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedServicePath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedOutputRoot,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$VerificationStartedAtUtc
    )

    $heartbeatRecord = Get-LatestManagedEvent `
        -EventId $heartbeatSupervisorEventId `
        -Kind 'SupervisorHeartbeat' `
        -ExpectedCampaignId $ExpectedCampaignId `
        -MinimumCreatedAtUtc $VerificationStartedAtUtc.AddSeconds(-5)
    if ($null -eq $heartbeatRecord) {
        throw 'The protected event log has no current supervisor heartbeat.'
    }

    $heartbeat = $heartbeatRecord.State
    if (-not [string]::Equals([string]$heartbeat.CampaignId, $ExpectedCampaignId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$heartbeat.Status, 'in_progress', [StringComparison]::OrdinalIgnoreCase) -or
        [int]$heartbeat.PlannedDurationSeconds -ne $DurationSeconds -or
        [string]::IsNullOrWhiteSpace([string]$heartbeat.ActiveCohortId) -or
        [int]$heartbeat.SupervisorProcessId -le 0) {
        throw 'Protected supervisor heartbeat identity or active status is invalid.'
    }

    $heartbeatAgeSeconds = ([DateTimeOffset]::UtcNow - $heartbeatRecord.EventCreatedAtUtc).TotalSeconds
    if ($heartbeatAgeSeconds -lt -300 -or $heartbeatAgeSeconds -gt $WatchdogStaleSeconds) {
        throw 'Protected supervisor heartbeat event is not current.'
    }

    $children = @($heartbeat.Children)
    $assets = @($children | ForEach-Object { [string]$_.Asset } | Sort-Object -Unique)
    if ($children.Count -ne 3 -or ($assets -join ',') -ne 'BTC,ETH,SOL') {
        throw 'Protected supervisor heartbeat does not contain exactly BTC, ETH, and SOL.'
    }

    $runIds = @($children | ForEach-Object { [string]$_.RunId } | Sort-Object -Unique)
    if ($runIds.Count -ne 3 -or @($runIds | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw 'Protected supervisor heartbeat does not contain three distinct collector run IDs.'
    }

    foreach ($child in $children) {
        if ([bool]$child.HasExited -or
            [int]$child.ProcessId -le 0 -or
            -not [string]::Equals([string]$child.Phase, 'collecting', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace([string]$child.RunId) -or
            -not [string]::Equals([string]$child.RunStatus, 'in_progress', [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$child.CheckpointStatus, 'in_progress', [StringComparison]::OrdinalIgnoreCase)) {
            throw "$($child.Asset) collector has not reached a healthy collecting checkpoint."
        }

        $checkpointUtc = [DateTimeOffset]::Parse(
            [string]$child.CheckpointUpdatedAtUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
        $checkpointAgeSeconds = ([DateTimeOffset]::UtcNow - $checkpointUtc).TotalSeconds
        if ($checkpointAgeSeconds -lt -300 -or $checkpointAgeSeconds -gt $WatchdogStaleSeconds) {
            throw "$($child.Asset) checkpoint reported by the protected heartbeat is not current."
        }
    }

    $processIds = @([int]$heartbeat.SupervisorProcessId) + @($children | ForEach-Object { [int]$_.ProcessId })
    if (@($processIds | Sort-Object -Unique).Count -ne 4) {
        throw 'Protected supervisor heartbeat does not identify four distinct campaign processes.'
    }

    foreach ($processId in $processIds) {
        $process = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $processId" -ErrorAction Stop
        if ($null -eq $process) {
            throw "Expected campaign process $processId is not running."
        }

        $processCreatedAtUtc = [DateTimeOffset]($process.CreationDate.ToUniversalTime())
        if ($processCreatedAtUtc -lt $VerificationStartedAtUtc.AddSeconds(-5) -or
            $processCreatedAtUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
            throw "Campaign process $processId was not created by the current installation start."
        }

        $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid -ErrorAction Stop
        if ([uint32]$owner.ReturnValue -ne 0 -or
            -not [string]::Equals([string]$owner.Sid, 'S-1-5-19', [StringComparison]::Ordinal)) {
            throw "Campaign process $processId is not owned by LOCAL SERVICE."
        }

        $isSupervisor = $processId -eq [int]$heartbeat.SupervisorProcessId
        if ($isSupervisor) {
            if (-not [string]::Equals([string]$process.ExecutablePath, $powerShellPath, [StringComparison]::OrdinalIgnoreCase) -or
                -not ([string]$process.CommandLine).Contains((ConvertTo-NativeArgument -Value $ExpectedSupervisorScriptPath)) -or
                -not ([string]$process.CommandLine).Contains($ExpectedCampaignId)) {
                throw 'LOCAL SERVICE supervisor process path or command line is invalid.'
            }
        }
        else {
            $child = @($children | Where-Object { [int]$_.ProcessId -eq $processId })
            if ($child.Count -ne 1) {
                throw "LOCAL SERVICE collector process $processId is not mapped to exactly one heartbeat child."
            }

            $asset = ([string]$child[0].Asset).ToLowerInvariant()
            $expectedAssetOutputDirectory = Join-Path $ExpectedOutputRoot (
                Join-Path $asset (Join-Path 'cohorts' (Join-Path ([string]$heartbeat.ActiveCohortId) 'runs')))
            $expectedCollectorArguments = Join-NativeArguments -Values @(
                '--crypto-orderbook-prediction-study',
                '--crypto-orderbook-study-mode', 'collect',
                '--crypto-orderbook-study-asset', $asset,
                '--crypto-orderbook-study-source', 'json',
                '--crypto-orderbook-study-output-dir', $expectedAssetOutputDirectory,
                '--crypto-orderbook-study-duration-seconds',
                $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
            $expectedCollectorCommandLine = (ConvertTo-NativeArgument -Value $ExpectedServicePath) +
                ' ' + $expectedCollectorArguments
            $observedCollectorCommandLine = ([string]$process.CommandLine).TrimEnd()

            if (-not [string]::Equals([string]$process.ExecutablePath, $ExpectedServicePath, [StringComparison]::OrdinalIgnoreCase) -or
                -not [string]::Equals($observedCollectorCommandLine, $expectedCollectorCommandLine, [StringComparison]::Ordinal)) {
                throw "LOCAL SERVICE $($child[0].Asset) collector process $processId path or exact command line is invalid."
            }
        }
    }

    $watchdogRecord = Get-LatestManagedEvent `
        -EventId $heartbeatWatchdogEventId `
        -Kind 'WatchdogStatus' `
        -ExpectedCampaignId $ExpectedCampaignId `
        -ExpectedTaskName $ExpectedMainTaskName `
        -ExpectedWatchdogTaskName $ExpectedWatchdogTaskName `
        -MinimumCreatedAtUtc $VerificationStartedAtUtc.AddSeconds(-5)
    if ($null -eq $watchdogRecord) {
        throw 'The protected event log has no current SYSTEM watchdog status.'
    }

    $watchdogState = $watchdogRecord.State
    if (-not [string]::Equals([string]$watchdogState.CampaignId, $ExpectedCampaignId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$watchdogState.TaskName, $ExpectedMainTaskName, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$watchdogState.WatchdogTaskName, $ExpectedWatchdogTaskName, [StringComparison]::Ordinal) -or
        @('healthy', 'recovered') -notcontains [string]$watchdogState.Status) {
        throw 'Protected SYSTEM watchdog event identity or status is invalid.'
    }

    $watchdogAgeSeconds = ([DateTimeOffset]::UtcNow - $watchdogRecord.EventCreatedAtUtc).TotalSeconds
    if ($watchdogAgeSeconds -lt -300 -or $watchdogAgeSeconds -gt 180) {
        throw 'Protected SYSTEM watchdog event is not current.'
    }

    $watchdogTask = Get-ScheduledTask -TaskPath '\' -TaskName $ExpectedWatchdogTaskName -ErrorAction Stop
    $watchdogTaskInfo = Get-ScheduledTaskInfo -TaskPath '\' -TaskName $ExpectedWatchdogTaskName -ErrorAction Stop
    $watchdogLastRunAgeSeconds = if ($watchdogTaskInfo.LastRunTime -eq [DateTime]::MinValue) {
        [double]::PositiveInfinity
    } else {
        ([DateTimeOffset]::UtcNow - [DateTimeOffset]($watchdogTaskInfo.LastRunTime.ToUniversalTime())).TotalSeconds
    }
    if ([string]::Equals([string]$watchdogTask.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase) -or
        [int]$watchdogTaskInfo.LastTaskResult -ne 0 -or
        $watchdogLastRunAgeSeconds -lt -300 -or
        $watchdogLastRunAgeSeconds -gt 180) {
        throw 'SYSTEM watchdog scheduled-task execution is not current and successful.'
    }

    return [pscustomobject]@{
        SupervisorProcessId = [int]$heartbeat.SupervisorProcessId
        CollectorProcessIds = @($children | ForEach-Object { [int]$_.ProcessId })
        HeartbeatEventRecordId = $heartbeatRecord.RecordId
        HeartbeatEventCreatedAtUtc = $heartbeatRecord.EventCreatedAtUtc.ToString('O')
        WatchdogEventRecordId = $watchdogRecord.RecordId
        WatchdogEventCreatedAtUtc = $watchdogRecord.EventCreatedAtUtc.ToString('O')
        WatchdogLastTaskResult = [int]$watchdogTaskInfo.LastTaskResult
    }
}

function Stop-AndDisableRegisteredTasks {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Names
    )

    $failures = New-Object 'System.Collections.Generic.List[string]'
    $orderedNames = @(@($WatchdogTaskName, $TaskName) | Where-Object { $Names -contains $_ })
    foreach ($name in $orderedNames) {
        try {
            $taskBeforeDisable = Get-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop
            $wasRunning = [string]::Equals(
                [string]$taskBeforeDisable.State,
                'Running',
                [StringComparison]::OrdinalIgnoreCase)
            Disable-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop | Out-Null
            $taskAfterDisable = Get-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop
            if ($wasRunning -or
                [string]::Equals([string]$taskAfterDisable.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
                Stop-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop
                $stopDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
                do {
                    Start-Sleep -Milliseconds 500
                    $taskAfterDisable = Get-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop
                } while ([string]::Equals([string]$taskAfterDisable.State, 'Running', [StringComparison]::OrdinalIgnoreCase) -and
                    [DateTimeOffset]::UtcNow -lt $stopDeadline)
            }

            if (-not [string]::Equals([string]$taskAfterDisable.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase)) {
                throw "state is $($taskAfterDisable.State), not Disabled."
            }
        }
        catch {
            $failures.Add("${name}: $($_.Exception.Message)")
        }
    }

    return @($failures)
}

function Restore-ScheduledTaskSnapshots {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Names
    )

    $failures = New-Object 'System.Collections.Generic.List[string]'
    foreach ($stopFailure in @(Stop-AndDisableRegisteredTasks -Names $Names)) {
        $failures.Add($stopFailure)
    }

    foreach ($name in $Names) {
        try {
            if ($script:taskSnapshots.ContainsKey($name)) {
                $snapshot = $script:taskSnapshots[$name]
                Register-ScheduledTask `
                    -TaskPath '\' `
                    -TaskName $name `
                    -Xml $snapshot.Xml `
                    -Force | Out-Null
                if ($snapshot.WasEnabled) {
                    Enable-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop | Out-Null
                }
                else {
                    Disable-ScheduledTask -TaskPath '\' -TaskName $name -ErrorAction Stop | Out-Null
                }
            }
            else {
                Unregister-ScheduledTask `
                    -TaskPath '\' `
                    -TaskName $name `
                    -Confirm:$false `
                    -ErrorAction Stop
            }
        }
        catch {
            $failures.Add("restore ${name}: $($_.Exception.Message)")
        }
    }

    return @($failures)
}

$resolvedSourceRunner = Resolve-SafeAbsolutePath -Path $SourceRunnerDirectory -Name 'SourceRunnerDirectory' -Directory
$resolvedSupervisorSource = Resolve-SafeAbsolutePath -Path $SourceSupervisorPath -Name 'SourceSupervisorPath'
$resolvedWatchdogSource = Resolve-SafeAbsolutePath -Path $SourceWatchdogPath -Name 'SourceWatchdogPath'
$resolvedRuntimeBase = Resolve-SafeAbsolutePath -Path $RuntimeBase -Name 'RuntimeBase' -Directory
$resolvedControlRoot = Resolve-SafeAbsolutePath -Path $ControlRoot -Name 'ControlRoot' -Directory
$resolvedOutputBase = Resolve-SafeAbsolutePath -Path $expectedOutputBase -Name 'OutputBase' -Directory
$resolvedOutputRoot = Resolve-SafeAbsolutePath -Path $OutputRoot -Name 'OutputRoot' -Directory
Assert-ExactDestinationPath -Actual $resolvedRuntimeBase -Expected $expectedRuntimeBase -Name 'RuntimeBase'
Assert-ExactDestinationPath -Actual $resolvedControlRoot -Expected $expectedControlRoot -Name 'ControlRoot'
Assert-ExactDestinationPath -Actual $resolvedOutputBase -Expected $expectedOutputBase -Name 'OutputBase'
Assert-ExactDestinationPath -Actual $resolvedOutputRoot -Expected $expectedOutputRoot -Name 'OutputRoot'

if (-not (Test-Path -LiteralPath $resolvedSourceRunner -PathType Container)) {
    throw "Source runner directory does not exist: $resolvedSourceRunner"
}

$sourceServicePath = Join-Path $resolvedSourceRunner $serviceFileName
foreach ($requiredFile in @($sourceServicePath, $resolvedSupervisorSource, $resolvedWatchdogSource, $powerShellPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required file does not exist: $requiredFile"
    }

    Assert-NoReparsePoint -Path $requiredFile -Name 'Required source file'
}

$sensitiveFiles = @(Get-ChildItem -LiteralPath $resolvedSourceRunner -File -Recurse -Force | Where-Object {
    $_.Name -match '(?i)(^|[._-])(secret|private|credential|password|token)([._-]|$)' -or
    $_.Extension -match '(?i)^\.(key|pem|pfx|p12|env)$'
})
if ($sensitiveFiles.Count -gt 0) {
    throw 'The source runner directory contains a secret-like file name and cannot be deployed to the unattended runtime.'
}

$runnerReparsePoints = @(Get-ChildItem -LiteralPath $resolvedSourceRunner -Recurse -Force | Where-Object {
    ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
})
if ($runnerReparsePoints.Count -gt 0) {
    throw "The source runner contains a reparse point and cannot be deployed: $($runnerReparsePoints[0].FullName)"
}

Assert-PowerShellSyntax -Path $resolvedSupervisorSource
Assert-PowerShellSyntax -Path $resolvedWatchdogSource
$actualServiceSha256 = (Get-Sha256 -Path $sourceServicePath).ToUpperInvariant()
if (-not [string]::Equals(
    $actualServiceSha256,
    $ExpectedServiceSha256.ToUpperInvariant(),
    [StringComparison]::Ordinal)) {
    throw "Service SHA-256 mismatch. Expected $ExpectedServiceSha256; actual $actualServiceSha256."
}

$supervisorSha256 = (Get-Sha256 -Path $resolvedSupervisorSource).ToUpperInvariant()
$watchdogSha256 = (Get-Sha256 -Path $resolvedWatchdogSource).ToUpperInvariant()
$sourceRunnerManifest = @(Get-DirectoryContentManifest -Path $resolvedSourceRunner)
if ($sourceRunnerManifest.Count -eq 0) {
    throw 'Source runner directory is empty.'
}

$runnerManifestSha256 = (Get-DirectoryManifestSha256 -Manifest $sourceRunnerManifest).ToUpperInvariant()
if (-not [string]::Equals(
    $runnerManifestSha256,
    $ExpectedRunnerManifestSha256.ToUpperInvariant(),
    [StringComparison]::Ordinal)) {
    throw "Runner manifest SHA-256 mismatch. Expected $ExpectedRunnerManifestSha256; actual $runnerManifestSha256."
}

if ([string]::IsNullOrWhiteSpace($CampaignId)) {
    $CampaignId = 'crypto-orderbook-' + $runnerManifestSha256.Substring(0, 12).ToLowerInvariant() + '-' +
        $supervisorSha256.Substring(0, 8).ToLowerInvariant() + '-' +
        $watchdogSha256.Substring(0, 8).ToLowerInvariant() + '-' +
        $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
}

$runtimeVersion = $runnerManifestSha256.Substring(0, 12).ToLowerInvariant() + '-' +
    $supervisorSha256.Substring(0, 12).ToLowerInvariant() + '-' +
    $watchdogSha256.Substring(0, 12).ToLowerInvariant()
$runtimeDirectory = Join-Path $resolvedRuntimeBase $runtimeVersion
$runtimeRunnerDirectory = Join-Path $runtimeDirectory 'runner'
$runtimeServicePath = Join-Path $runtimeRunnerDirectory $serviceFileName
$runtimeSupervisorPath = Join-Path $runtimeDirectory $supervisorFileName
$runtimeWatchdogPath = Join-Path $runtimeDirectory $watchdogFileName

$mainArguments = Join-NativeArguments -Values @(
    '-NoProfile',
    '-NonInteractive',
    '-ExecutionPolicy', 'Bypass',
    '-File', $runtimeSupervisorPath,
    '-ServiceExecutable', $runtimeServicePath,
    '-OutputRoot', $resolvedOutputRoot,
    '-ControlRoot', $resolvedControlRoot,
    '-CampaignId', $CampaignId,
    '-HeartbeatEventSource', $heartbeatEventSource,
    '-HeartbeatEventLogName', $heartbeatEventLogName,
    '-DurationSeconds', $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-CheckpointStartGraceSeconds', $CheckpointStartGraceSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
$watchdogArguments = Join-NativeArguments -Values @(
    '-NoProfile',
    '-NonInteractive',
    '-ExecutionPolicy', 'Bypass',
    '-File', $runtimeWatchdogPath,
    '-TaskName', $TaskName,
    '-WatchdogTaskName', $WatchdogTaskName,
    '-HeartbeatEventSource', $heartbeatEventSource,
    '-HeartbeatEventLogName', $heartbeatEventLogName,
    '-CampaignId', $CampaignId,
    '-DurationSeconds', $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-StaleSeconds', $WatchdogStaleSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-StartupGraceSeconds', $WatchdogStartupGraceSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-CompletionValidationGraceSeconds', $WatchdogCompletionValidationGraceSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))

$mainAction = New-ScheduledTaskAction `
    -Execute $powerShellPath `
    -Argument $mainArguments `
    -WorkingDirectory $runtimeDirectory
$mainPrincipal = New-ScheduledTaskPrincipal `
    -UserId 'NT AUTHORITY\LOCAL SERVICE' `
    -LogonType ServiceAccount `
    -RunLevel Highest
$mainTrigger = New-ScheduledTaskTrigger -AtStartup
$mainTrigger.Delay = 'PT1M'
$mainSettings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -WakeToRun `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -RestartCount 255 `
    -RestartInterval (New-TimeSpan -Minutes 1)
$mainDefinition = New-ScheduledTask `
    -Action $mainAction `
    -Principal $mainPrincipal `
    -Trigger $mainTrigger `
    -Settings $mainSettings `
    -Description 'Unattended read-only BTC/ETH/SOL prospective order-book collection campaign.'

$watchdogAction = New-ScheduledTaskAction `
    -Execute $powerShellPath `
    -Argument $watchdogArguments `
    -WorkingDirectory $runtimeDirectory
$watchdogPrincipal = New-ScheduledTaskPrincipal `
    -UserId 'NT AUTHORITY\SYSTEM' `
    -LogonType ServiceAccount `
    -RunLevel Highest
$watchdogTrigger = New-ScheduledTaskTrigger `
    -Once `
    -At ((Get-Date).AddMinutes(2)) `
    -RepetitionInterval (New-TimeSpan -Minutes 1)
$watchdogSettings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -WakeToRun `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)
$watchdogDefinition = New-ScheduledTask `
    -Action $watchdogAction `
    -Principal $watchdogPrincipal `
    -Trigger $watchdogTrigger `
    -Settings $watchdogSettings `
    -Description 'Protected heartbeat watchdog for the BTC/ETH/SOL order-book collection campaign.'

$summary = [ordered]@{
    SchemaVersion = 1
    Mode = if ($ValidateOnly) { 'validate_only' } else { 'install' }
    CampaignId = $CampaignId
    DurationSeconds = $DurationSeconds
    CheckpointStartGraceSeconds = $CheckpointStartGraceSeconds
    WatchdogCompletionValidationGraceSeconds = $WatchdogCompletionValidationGraceSeconds
    SourceServiceSha256 = $actualServiceSha256
    SourceRunnerManifestSha256 = $runnerManifestSha256
    SourceSupervisorSha256 = $supervisorSha256
    SourceWatchdogSha256 = $watchdogSha256
    SourceRunnerFileCount = $sourceRunnerManifest.Count
    RuntimeDirectory = $runtimeDirectory
    RuntimeService = $runtimeServicePath
    ControlRoot = $resolvedControlRoot
    OutputRoot = $resolvedOutputRoot
    HeartbeatEventLog = [ordered]@{
        Name = $heartbeatEventLogName
        Source = $heartbeatEventSource
        MaximumSizeBytes = $heartbeatEventLogMaximumBytes
        ChannelAccess = $heartbeatEventLogSddl
    }
    MainTask = [ordered]@{
        Name = $TaskName
        Principal = [string]$mainDefinition.Principal.UserId
        LogonType = [string]$mainDefinition.Principal.LogonType
        Trigger = 'AtStartup;Delay=PT1M'
        WakeToRun = [bool]$mainDefinition.Settings.WakeToRun
        ExecutionTimeLimit = [string]$mainDefinition.Settings.ExecutionTimeLimit
        RestartCount = [int]$mainDefinition.Settings.RestartCount
        RestartInterval = [string]$mainDefinition.Settings.RestartInterval
        Action = $powerShellPath
        Arguments = $mainArguments
    }
    WatchdogTask = [ordered]@{
        Name = $WatchdogTaskName
        Principal = [string]$watchdogDefinition.Principal.UserId
        LogonType = [string]$watchdogDefinition.Principal.LogonType
        RepetitionInterval = [string]$watchdogDefinition.Triggers[0].Repetition.Interval
        WakeToRun = [bool]$watchdogDefinition.Settings.WakeToRun
        ExecutionTimeLimit = [string]$watchdogDefinition.Settings.ExecutionTimeLimit
        Action = $powerShellPath
        Arguments = $watchdogArguments
    }
}

if ($ValidateOnly) {
    $summary | ConvertTo-Json -Depth 8
    exit 0
}

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal -ArgumentList $identity
if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Installation requires an elevated Administrator token. Re-run Windows PowerShell with UAC elevation, or use -ValidateOnly.'
}

if ($DisableLegacyTask -and -not $StartAfterInstall) {
    throw 'The legacy task can be disabled only after -StartAfterInstall produces verified replacement checkpoints.'
}

if ($DisableLegacyTask) {
    $legacyTaskPreflight = Get-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction SilentlyContinue
    if ($null -ne $legacyTaskPreflight) {
        $legacyPreflightActions = @($legacyTaskPreflight.Actions)
        if ($legacyPreflightActions.Count -ne 1 -or
            -not [string]::Equals([string]$legacyTaskPreflight.Principal.LogonType, 'Interactive', [StringComparison]::OrdinalIgnoreCase) -or
            -not ([string]$legacyPreflightActions[0].Arguments).Contains($resolvedSupervisorSource) -or
            -not ([string]$legacyPreflightActions[0].Arguments).Contains($sourceServicePath)) {
            throw "Legacy task $LegacyTaskName does not match the exact migration allowlist."
        }

        if ([string]::Equals([string]$legacyTaskPreflight.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Legacy task $LegacyTaskName is already running; migration stopped before changing the machine."
        }

        $legacyTaskSnapshot = [pscustomobject]@{
            Xml = Export-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop
            WasEnabled = -not [string]::Equals(
                [string]$legacyTaskPreflight.State,
                'Disabled',
                [StringComparison]::OrdinalIgnoreCase)
        }
    }
}

Assert-ExpectedExistingTask `
    -Name $TaskName `
    -ExpectedPrincipal 'LOCAL SERVICE' `
    -ExpectedActionPath $runtimeSupervisorPath `
    -ExpectedArguments $mainArguments
Assert-ExpectedExistingTask `
    -Name $WatchdogTaskName `
    -ExpectedPrincipal 'SYSTEM' `
    -ExpectedActionPath $runtimeWatchdogPath `
    -ExpectedArguments $watchdogArguments

foreach ($taskToSnapshot in @($TaskName, $WatchdogTaskName)) {
    $existingTaskToSnapshot = Get-ScheduledTask -TaskPath '\' -TaskName $taskToSnapshot -ErrorAction SilentlyContinue
    if ($null -ne $existingTaskToSnapshot) {
        $taskSnapshots[$taskToSnapshot] = [pscustomobject]@{
            Xml = Export-ScheduledTask -TaskPath '\' -TaskName $taskToSnapshot -ErrorAction Stop
            WasEnabled = -not [string]::Equals(
                [string]$existingTaskToSnapshot.State,
                'Disabled',
                [StringComparison]::OrdinalIgnoreCase)
        }
    }
}

if (Test-Path -LiteralPath $heartbeatEventLogRegistryPath -PathType Container) {
    Assert-HeartbeatEventLogConfiguration
}

$managedDirectories = @(
    [pscustomobject]@{
        Path = $resolvedRuntimeBase
        Role = 'runtime_base'
        LocalServiceRights = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
    },
    [pscustomobject]@{
        Path = $resolvedControlRoot
        Role = 'control_root'
        LocalServiceRights = [System.Security.AccessControl.FileSystemRights]::Modify
    },
    [pscustomobject]@{
        Path = $resolvedOutputBase
        Role = 'output_base'
        LocalServiceRights = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
    },
    [pscustomobject]@{
        Path = $resolvedOutputRoot
        Role = 'output_root'
        LocalServiceRights = [System.Security.AccessControl.FileSystemRights]::Modify
    })

# Complete the non-mutating destination preview before creating a directory or replacing an ACL.
foreach ($managedDirectory in $managedDirectories) {
    Assert-ManagedOrEmptyDirectory -Path $managedDirectory.Path -Role $managedDirectory.Role
}

foreach ($managedDirectory in $managedDirectories) {
    if (-not (Test-Path -LiteralPath $managedDirectory.Path -PathType Container)) {
        $null = New-Item -ItemType Directory -Path $managedDirectory.Path -Force
    }

    Assert-NoReparsePoint -Path $managedDirectory.Path -Name 'Created protected destination'

    Set-ProtectedDirectoryAcl `
        -Path $managedDirectory.Path `
        -LocalServiceRights $managedDirectory.LocalServiceRights
    Write-ManagedDirectoryMarker -Path $managedDirectory.Path -Role $managedDirectory.Role
}

$deploymentManifest = [ordered]@{
    SchemaVersion = 1
    RuntimeVersion = $runtimeVersion
    CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    ServiceSha256 = $actualServiceSha256
    RunnerManifestSha256 = $runnerManifestSha256
    SupervisorSha256 = $supervisorSha256
    WatchdogSha256 = $watchdogSha256
    RunnerFiles = $sourceRunnerManifest
}
$runtimeIsComplete = $false
$runtimeValidationFailure = $null
if (Test-Path -LiteralPath $runtimeDirectory -PathType Container) {
    try {
        Assert-NoReparsePoint -Path $runtimeDirectory -Name 'Existing versioned runtime'
        $runtimeReparsePoints = @(Get-ChildItem -LiteralPath $runtimeDirectory -Recurse -Force | Where-Object {
            ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
        })
        if ($runtimeReparsePoints.Count -gt 0) {
            throw "runtime contains a reparse point: $($runtimeReparsePoints[0].FullName)"
        }

        $existingDeploymentPath = Join-Path $runtimeDirectory 'deployment.json'
        $existingDeployment = [System.IO.File]::ReadAllText(
            $existingDeploymentPath,
            [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $existingRunnerManifest = @(Get-DirectoryContentManifest -Path $runtimeRunnerDirectory)
        if ([int]$existingDeployment.SchemaVersion -ne 1 -or
            -not [string]::Equals([string]$existingDeployment.RuntimeVersion, $runtimeVersion, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$existingDeployment.ServiceSha256, $actualServiceSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$existingDeployment.RunnerManifestSha256, $runnerManifestSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$existingDeployment.SupervisorSha256, $supervisorSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$existingDeployment.WatchdogSha256, $watchdogSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-DirectoryContentEqual -Expected $sourceRunnerManifest -Actual $existingRunnerManifest) -or
            -not [string]::Equals((Get-Sha256 -Path $runtimeServicePath), $actualServiceSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals((Get-Sha256 -Path $runtimeSupervisorPath), $supervisorSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals((Get-Sha256 -Path $runtimeWatchdogPath), $watchdogSha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'deployment marker, manifest, or content hash does not match.'
        }

        $runtimeIsComplete = $true
    }
    catch {
        $runtimeValidationFailure = $_.Exception.Message
    }
}
elseif (Test-Path -LiteralPath $runtimeDirectory) {
    throw "Versioned runtime path exists but is not a directory: $runtimeDirectory"
}

if (-not $runtimeIsComplete -and (Test-Path -LiteralPath $runtimeDirectory -PathType Container)) {
    $referencingTasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
        @($_.Actions | Where-Object {
            ([string]$_.Execute).IndexOf($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            ([string]$_.Arguments).IndexOf($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            ([string]$_.WorkingDirectory).IndexOf($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0
        }).Count -gt 0
    })
    if ($referencingTasks.Count -gt 0) {
        throw "Incomplete runtime is referenced by scheduled task $($referencingTasks[0].TaskName) and was not moved. Validation failure: $runtimeValidationFailure"
    }


    $referencingProcesses = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop | Where-Object {
        ([string]$_.ExecutablePath).IndexOf($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ([string]$_.CommandLine).IndexOf($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
    if ($referencingProcesses.Count -gt 0) {
        throw "Incomplete runtime is referenced by live process $($referencingProcesses[0].ProcessId) and was not moved. Validation failure: $runtimeValidationFailure"
    }

    $quarantineDirectory = Join-Path $resolvedRuntimeBase (
        $runtimeVersion + '.incomplete-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' +
        [Guid]::NewGuid().ToString('N').Substring(0, 8))
    Move-Item -LiteralPath $runtimeDirectory -Destination $quarantineDirectory
    $summary['QuarantinedIncompleteRuntime'] = $quarantineDirectory
    $summary['QuarantineReason'] = $runtimeValidationFailure
}

if (-not (Test-Path -LiteralPath $runtimeDirectory -PathType Container)) {
    $stagingDirectory = Join-Path $resolvedRuntimeBase (
        $runtimeVersion + '.staging-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    $stagingRunnerDirectory = Join-Path $stagingDirectory 'runner'
    $stagingServicePath = Join-Path $stagingRunnerDirectory $serviceFileName
    $stagingSupervisorPath = Join-Path $stagingDirectory $supervisorFileName
    $stagingWatchdogPath = Join-Path $stagingDirectory $watchdogFileName
    $stagingDeploymentPath = Join-Path $stagingDirectory 'deployment.json'
    try {
        $null = New-Item -ItemType Directory -Path $stagingDirectory
        Set-ProtectedDirectoryAcl `
            -Path $stagingDirectory `
            -LocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
        $null = New-Item -ItemType Directory -Path $stagingRunnerDirectory
        foreach ($sourceItem in @(Get-ChildItem -LiteralPath $resolvedSourceRunner -Force)) {
            Copy-Item -LiteralPath $sourceItem.FullName -Destination $stagingRunnerDirectory -Recurse
        }

        Copy-Item -LiteralPath $resolvedSupervisorSource -Destination $stagingSupervisorPath
        Copy-Item -LiteralPath $resolvedWatchdogSource -Destination $stagingWatchdogPath
        [System.IO.File]::WriteAllText(
            $stagingDeploymentPath,
            ($deploymentManifest | ConvertTo-Json -Depth 8),
            $utf8NoBom)
        foreach ($directory in @(Get-ChildItem -LiteralPath $stagingDirectory -Directory -Recurse -Force) + @((Get-Item -LiteralPath $stagingDirectory))) {
            Set-ProtectedDirectoryAcl `
                -Path $directory.FullName `
                -LocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
        }

        foreach ($file in @(Get-ChildItem -LiteralPath $stagingDirectory -File -Recurse -Force)) {
            Set-ProtectedFileAcl -Path $file.FullName
        }

        $stagedRunnerManifest = @(Get-DirectoryContentManifest -Path $stagingRunnerDirectory)
        if (-not (Test-DirectoryContentEqual -Expected $sourceRunnerManifest -Actual $stagedRunnerManifest) -or
            -not [string]::Equals((Get-Sha256 -Path $stagingServicePath), $actualServiceSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals((Get-Sha256 -Path $stagingSupervisorPath), $supervisorSha256, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals((Get-Sha256 -Path $stagingWatchdogPath), $watchdogSha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Staged runtime content does not match the pinned source.'
        }

        Assert-ProtectedAcl `
            -Path $stagingDirectory `
            -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
        foreach ($stagingItem in @(Get-ChildItem -LiteralPath $stagingDirectory -Recurse -Force)) {
            Assert-ProtectedAcl `
                -Path $stagingItem.FullName `
                -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
        }

        Move-Item -LiteralPath $stagingDirectory -Destination $runtimeDirectory
        $summary['RuntimeCreatedFromVerifiedStaging'] = $true
    }
    catch {
        throw "Versioned runtime staging failed; protected staging was preserved at ${stagingDirectory}: $($_.Exception.Message)"
    }
}
else {
    $summary['RuntimeCreatedFromVerifiedStaging'] = $false
}

$deployedRunnerManifest = @(Get-DirectoryContentManifest -Path $runtimeRunnerDirectory)
if (-not (Test-DirectoryContentEqual -Expected $sourceRunnerManifest -Actual $deployedRunnerManifest) -or
    -not [string]::Equals(
        (Get-Sha256 -Path $runtimeServicePath),
        $actualServiceSha256,
        [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals(
        (Get-Sha256 -Path $runtimeSupervisorPath),
        $supervisorSha256,
        [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals(
        (Get-Sha256 -Path $runtimeWatchdogPath),
        $watchdogSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Protected runtime copy does not match the source hashes and file manifest.'
}

Assert-ProtectedAcl `
    -Path $resolvedRuntimeBase `
    -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
Assert-ProtectedAcl `
    -Path $runtimeDirectory `
    -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
foreach ($runtimeItem in @(Get-ChildItem -LiteralPath $runtimeDirectory -Recurse -Force)) {
    Assert-ProtectedAcl `
        -Path $runtimeItem.FullName `
        -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
}

Assert-ProtectedAcl `
    -Path $resolvedControlRoot `
    -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::Modify)
Assert-ProtectedAcl `
    -Path $resolvedOutputBase `
    -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
Assert-ProtectedAcl `
    -Path $resolvedOutputRoot `
    -ExpectedLocalServiceRights ([System.Security.AccessControl.FileSystemRights]::Modify)

$wevtutil = Join-Path $env:SystemRoot 'System32\wevtutil.exe'
& $wevtutil sl 'Microsoft-Windows-TaskScheduler/Operational' /e:true /ms:67108864 /rt:false /ab:false
if ($LASTEXITCODE -ne 0) {
    throw "wevtutil failed to enable Task Scheduler Operational logging with exit code $LASTEXITCODE."
}

$taskSchedulerLog = Get-WinEvent -ListLog 'Microsoft-Windows-TaskScheduler/Operational'
if (-not $taskSchedulerLog.IsEnabled) {
    throw 'Task Scheduler Operational logging is still disabled after configuration.'
}

$heartbeatEventLogCreated = Install-HeartbeatEventLog
$summary['HeartbeatEventLogCreated'] = $heartbeatEventLogCreated
$summary['HeartbeatEventLogAclVerified'] = $true

$registeredTaskNames = New-Object 'System.Collections.Generic.List[string]'
try {
    foreach ($taskNameToRevalidate in @($TaskName, $WatchdogTaskName)) {
        $currentTask = Get-ScheduledTask -TaskPath '\' -TaskName $taskNameToRevalidate -ErrorAction SilentlyContinue
        if ($taskSnapshots.ContainsKey($taskNameToRevalidate)) {
            if ($null -eq $currentTask -or
                -not [string]::Equals([string]$currentTask.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase) -or
                -not [string]::Equals(
                    (Export-ScheduledTask -TaskPath '\' -TaskName $taskNameToRevalidate -ErrorAction Stop),
                    [string]$taskSnapshots[$taskNameToRevalidate].Xml,
                    [StringComparison]::Ordinal)) {
                throw "Existing task $taskNameToRevalidate changed after preflight; registration was aborted."
            }
        }
        elseif ($null -ne $currentTask) {
            throw "Task $taskNameToRevalidate appeared after preflight; registration was aborted."
        }
    }

    Register-ScheduledTask `
        -TaskPath '\' `
        -TaskName $TaskName `
        -InputObject $mainDefinition `
        -Force | Out-Null
    $registeredTaskNames.Add($TaskName)
    Disable-ScheduledTask -TaskPath '\' -TaskName $TaskName -ErrorAction Stop | Out-Null

    Register-ScheduledTask `
        -TaskPath '\' `
        -TaskName $WatchdogTaskName `
        -InputObject $watchdogDefinition `
        -Force | Out-Null
    $registeredTaskNames.Add($WatchdogTaskName)
    Disable-ScheduledTask -TaskPath '\' -TaskName $WatchdogTaskName -ErrorAction Stop | Out-Null

    Assert-RegisteredTask `
        -Name $TaskName `
        -ExpectedPrincipal 'LOCAL SERVICE' `
        -ExpectedScriptPath $runtimeSupervisorPath `
        -ExpectedArguments $mainArguments `
        -ExpectedWakeToRun $true `
        -Kind Main
    Assert-RegisteredTask `
        -Name $WatchdogTaskName `
        -ExpectedPrincipal 'SYSTEM' `
        -ExpectedScriptPath $runtimeWatchdogPath `
        -ExpectedArguments $watchdogArguments `
        -ExpectedWakeToRun $true `
        -Kind Watchdog

    foreach ($registeredTaskName in $registeredTaskNames) {
        $registeredTask = Get-ScheduledTask -TaskPath '\' -TaskName $registeredTaskName -ErrorAction Stop
        if (-not [string]::Equals([string]$registeredTask.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Task $registeredTaskName was not disabled during registration verification."
        }
    }
}
catch {
    $registrationFailure = $_.Exception.Message
    $rollbackFailures = @(Restore-ScheduledTaskSnapshots -Names @($registeredTaskNames))
    $eventLogCleanupFailure = Remove-NewHeartbeatEventLog
    if ($null -ne $eventLogCleanupFailure) {
        $rollbackFailures += 'Heartbeat event-log cleanup: ' + $eventLogCleanupFailure
    }
    $rollbackDetail = if ($rollbackFailures.Count -eq 0) {
        'Every changed task was restored to its exact prior state or removed if newly created.'
    } else {
        'Rollback errors: ' + ($rollbackFailures -join '; ')
    }
    throw "Scheduled-task registration verification failed: $registrationFailure $rollbackDetail"
}

if ($StartAfterInstall) {
    try {
        $verificationStartedAtUtc = [DateTimeOffset]::UtcNow
        Enable-ScheduledTask -TaskPath '\' -TaskName $TaskName | Out-Null
        Enable-ScheduledTask -TaskPath '\' -TaskName $WatchdogTaskName | Out-Null
        Start-ScheduledTask -TaskPath '\' -TaskName $TaskName
        Start-ScheduledTask -TaskPath '\' -TaskName $WatchdogTaskName
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        $startedTask = $null
        do {
            Start-Sleep -Milliseconds 500
            $startedTask = Get-ScheduledTask -TaskPath '\' -TaskName $TaskName
        } while (-not [string]::Equals([string]$startedTask.State, 'Running', [StringComparison]::OrdinalIgnoreCase) -and
            [DateTimeOffset]::UtcNow -lt $deadline)
        if (-not [string]::Equals([string]$startedTask.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
            $taskInfo = Get-ScheduledTaskInfo -TaskPath '\' -TaskName $TaskName
            throw "Main task did not reach Running state. State=$($startedTask.State); LastTaskResult=$($taskInfo.LastTaskResult)."
        }

        $evidenceWaitSeconds = [Math]::Min(
            [Math]::Max($CheckpointStartGraceSeconds + 60, 360),
            3660)
        $evidenceDeadline = [DateTimeOffset]::UtcNow.AddSeconds($evidenceWaitSeconds)
        $startEvidence = $null
        $lastEvidenceFailure = 'No evidence check ran.'
        do {
            Start-Sleep -Seconds 1
            try {
                $startEvidence = Get-StartedCohortEvidence `
                    -ExpectedCampaignId $CampaignId `
                    -ExpectedMainTaskName $TaskName `
                    -ExpectedWatchdogTaskName $WatchdogTaskName `
                    -ExpectedSupervisorScriptPath $runtimeSupervisorPath `
                    -ExpectedServicePath $runtimeServicePath `
                    -ExpectedOutputRoot $resolvedOutputRoot `
                    -VerificationStartedAtUtc $verificationStartedAtUtc
                $lastEvidenceFailure = $null
            }
            catch {
                $lastEvidenceFailure = $_.Exception.Message
            }
        } while ($null -eq $startEvidence -and [DateTimeOffset]::UtcNow -lt $evidenceDeadline)

        if ($null -eq $startEvidence) {
            $taskInfo = Get-ScheduledTaskInfo -TaskPath '\' -TaskName $TaskName
            throw "Main task did not produce a verified three-asset checkpoint within $evidenceWaitSeconds seconds. LastTaskResult=$($taskInfo.LastTaskResult); evidence=$lastEvidenceFailure"
        }

        $summary['Started'] = $true
        $summary['StartEvidence'] = $startEvidence
    }
    catch {
        $startupFailure = $_.Exception.Message
        $rollbackFailures = @(Restore-ScheduledTaskSnapshots -Names @($registeredTaskNames))
        $eventLogCleanupFailure = Remove-NewHeartbeatEventLog
        if ($null -ne $eventLogCleanupFailure) {
            $rollbackFailures += 'Heartbeat event-log cleanup: ' + $eventLogCleanupFailure
        }

        $rollbackDetail = if ($rollbackFailures.Count -eq 0) {
            'Both changed tasks were restored to their exact prior state or removed if newly created.'
        } else {
            'Rollback errors: ' + ($rollbackFailures -join '; ')
        }
        throw "Startup verification failed: $startupFailure $rollbackDetail"
    }
}
else {
    Disable-ScheduledTask -TaskPath '\' -TaskName $TaskName | Out-Null
    Disable-ScheduledTask -TaskPath '\' -TaskName $WatchdogTaskName | Out-Null
    foreach ($disabledTaskName in @($TaskName, $WatchdogTaskName)) {
        $disabledTask = Get-ScheduledTask -TaskPath '\' -TaskName $disabledTaskName -ErrorAction Stop
        if (-not [string]::Equals([string]$disabledTask.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Task $disabledTaskName was expected to remain disabled after deployment."
        }
    }

    $summary['Started'] = $false
    $summary['TasksDisabled'] = $true
}

if ($DisableLegacyTask) {
    try {
        $legacyTask = Get-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction SilentlyContinue
        if ($null -ne $legacyTask) {
            $legacyAction = @($legacyTask.Actions)
            if ($legacyAction.Count -ne 1 -or
                -not [string]::Equals([string]$legacyTask.Principal.LogonType, 'Interactive', [StringComparison]::OrdinalIgnoreCase) -or
                -not ([string]$legacyAction[0].Arguments).Contains($resolvedSupervisorSource) -or
                -not ([string]$legacyAction[0].Arguments).Contains($sourceServicePath)) {
                throw "Legacy task $LegacyTaskName does not match the exact migration allowlist and was not disabled."
            }

            if ($null -eq $legacyTaskSnapshot -or
                -not [string]::Equals(
                    (Export-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop),
                    [string]$legacyTaskSnapshot.Xml,
                    [StringComparison]::Ordinal)) {
                throw "Legacy task $LegacyTaskName changed after preflight and was not disabled."
            }

            if ([string]::Equals([string]$legacyTask.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
                Stop-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop
                $legacyStopDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
                do {
                    Start-Sleep -Milliseconds 500
                    $legacyTask = Get-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop
                } while ([string]::Equals([string]$legacyTask.State, 'Running', [StringComparison]::OrdinalIgnoreCase) -and
                    [DateTimeOffset]::UtcNow -lt $legacyStopDeadline)
                if ([string]::Equals([string]$legacyTask.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Legacy task $LegacyTaskName started during migration and did not stop within 30 seconds."
                }
            }

            Disable-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName | Out-Null
            $disabledLegacyTask = Get-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop
            if (-not [string]::Equals([string]$disabledLegacyTask.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Legacy task $LegacyTaskName did not reach Disabled state."
            }

            $summary['LegacyTaskDisabled'] = $true
        }
        else {
            $summary['LegacyTaskDisabled'] = $false
        }
    }
    catch {
        $legacyMigrationFailure = $_.Exception.Message
        $legacyRestoreFailure = $null
        if ($null -ne $legacyTaskSnapshot) {
            try {
                $legacyTaskToRestore = Get-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop
                if (-not [string]::Equals(
                    (Export-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop),
                    [string]$legacyTaskSnapshot.Xml,
                    [StringComparison]::Ordinal)) {
                    throw 'Legacy task definition changed; state restoration was refused.'
                }

                if ([bool]$legacyTaskSnapshot.WasEnabled) {
                    Enable-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop | Out-Null
                }
                else {
                    Disable-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop | Out-Null
                }

                $restoredLegacyTask = Get-ScheduledTask -TaskPath '\' -TaskName $LegacyTaskName -ErrorAction Stop
                $restoredEnabled = -not [string]::Equals(
                    [string]$restoredLegacyTask.State,
                    'Disabled',
                    [StringComparison]::OrdinalIgnoreCase)
                if ($restoredEnabled -ne [bool]$legacyTaskSnapshot.WasEnabled) {
                    throw 'Legacy task enabled state was not restored.'
                }
            }
            catch {
                $legacyRestoreFailure = $_.Exception.Message
            }
        }

        $rollbackDetail = if ($null -eq $legacyRestoreFailure) {
            'The legacy enabled state was restored; the verified new tasks remain active.'
        } else {
            'Legacy-state restoration also failed: ' + $legacyRestoreFailure + '. The verified new tasks remain active.'
        }
        throw "Legacy-task cleanup failed: $legacyMigrationFailure $rollbackDetail"
    }
}

$summary['OperationalLogEnabled'] = $true
$summary['RuntimeAclVerified'] = $true
$summary['ControlAclVerified'] = $true
$summary['OutputBaseAclVerified'] = $true
$summary['OutputAclVerified'] = $true
$summary | ConvertTo-Json -Depth 8
