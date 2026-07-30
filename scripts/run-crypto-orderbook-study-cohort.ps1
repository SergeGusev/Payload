[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceExecutable,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [Parameter(Mandatory = $true)]
    [string]$ControlRoot,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$CampaignId,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$HeartbeatEventSource,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$HeartbeatEventLogName,

    [ValidateRange(10, 604800)]
    [int]$DurationSeconds = 259200,

    [ValidateRange(1, 300)]
    [int]$HeartbeatSeconds = 15,

    [ValidateRange(10, 3600)]
    [int]$RunDiscoveryTimeoutSeconds = 120,

    [ValidateRange(10, 3600)]
    [int]$CheckpointStartGraceSeconds = 600,

    [ValidateRange(10, 3600)]
    [int]$CheckpointStaleSeconds = 900,

    [ValidateRange(60, 86400)]
    [int]$AnalysisTimeoutSeconds = 21600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assets = @('btc', 'eth', 'sol')
$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
$servicePath = $null
$resolvedOutputRoot = $null
$resolvedControlRoot = $null
$campaignDirectory = $null
$campaignPath = $null
$lockStream = $null
$processJob = $null
$powerRequestSet = $false
$children = @()
$cohortId = $null
$cohortDirectory = $null
$manifestPath = $null
$cohortStatus = 'initializing'
$failureReason = $null
$exitCode = 1
$startedAtUtc = [DateTimeOffset]::UtcNow
$attemptNumber = 0
$campaign = $null
$campaignAttemptStarted = $false
$campaignAlreadyCompleted = $false
$heartbeatEventId = 4100
$lastHeartbeatEventAtUtc = [DateTimeOffset]::MinValue
$lastHeartbeatEventStatus = $null
$heartbeatPhaseStartedAtUtc = [DateTimeOffset]::MinValue
$heartbeatPhaseStatus = $null
$completionValidationHeartbeatAtUtc = [DateTimeOffset]::MinValue

function Resolve-SafeAbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [switch]$AllowCreate
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
    if ([string]::IsNullOrWhiteSpace($root) -or
        [string]::Equals($fullPath, $root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must not be a drive root."
    }

    if ($AllowCreate) {
        $null = New-Item -ItemType Directory -Path $fullPath -Force
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

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $partialPath = $Path + '.partial'
    $json = $Value | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($partialPath, $json, $utf8NoBom)
    Move-Item -LiteralPath $partialPath -Destination $Path -Force
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
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

function ConvertTo-NativeArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value.Contains('"')) {
        throw 'Native process arguments must not contain quotation marks.'
    }

    if ($Value.Length -eq 0 -or $Value -match '\s') {
        return '"' + $Value + '"'
    }

    return $Value
}

function Initialize-NativeRuntimeSupport {
    if ('PolyCopyTraderOrderBookNative' -as [type]) {
        return
    }

    $typeDefinition = @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class PolyCopyTraderOrderBookNative
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    public static void SetKeepAwake()
    {
        if (SetThreadExecutionState(EsContinuous | EsSystemRequired) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetThreadExecutionState failed.");
        }
    }

    public static void ClearKeepAwake()
    {
        if (SetThreadExecutionState(EsContinuous) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetThreadExecutionState cleanup failed.");
        }
    }
}

public sealed class PolyCopyTraderOrderBookProcessJob : IDisposable
{
    private const uint JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private SafeFileHandle handle;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        uint informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    public PolyCopyTraderOrderBookProcessJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == null || handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");
        }

        var information = new JobObjectExtendedLimitInformation();
        information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        uint length = checked((uint)Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation)));
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, ref information, length))
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "SetInformationJobObject failed.");
        }
    }

    public void AddProcess(Process process)
    {
        if (process == null)
        {
            throw new ArgumentNullException("process");
        }

        if (!AssignProcessToJobObject(handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed.");
        }
    }

    public void Dispose()
    {
        if (handle != null)
        {
            handle.Dispose();
            handle = null;
        }
    }
}
'@

    Add-Type -TypeDefinition $typeDefinition -Language CSharp
}

function Get-CurrentChildSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Child,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$NowUtc
    )

    $Child.Process.Refresh()
    $hasExited = $Child.Process.HasExited
    [ordered]@{
        Asset = $Child.Asset.ToUpperInvariant()
        ProcessId = $Child.Process.Id
        ProcessStartTimeUtc = $Child.ProcessStartTimeUtc.ToString('O')
        HasExited = $hasExited
        ExitCode = if ($hasExited) { $Child.Process.ExitCode } else { $null }
        Phase = $Child.Phase
        OutputDirectory = $Child.OutputDirectory
        RunDirectory = $Child.RunDirectory
        RunId = $Child.RunId
        RunStatus = $Child.RunStatus
        AnalysisStatus = $Child.AnalysisStatus
        CompletionValidated = $Child.CompletionValidated
        IndexPath = $Child.IndexPath
        CheckpointStatus = $Child.CheckpointStatus
        CheckpointUpdatedAtUtc = if ($null -ne $Child.CheckpointUpdatedAtUtc) {
            $Child.CheckpointUpdatedAtUtc.ToString('O')
        } else {
            $null
        }
        CheckpointAgeSeconds = if ($null -ne $Child.CheckpointUpdatedAtUtc) {
            [Math]::Round(($NowUtc - $Child.CheckpointUpdatedAtUtc).TotalSeconds, 3)
        } else {
            $null
        }
        WatchdogConsecutiveFailures = $Child.WatchdogConsecutiveFailures
        WatchdogFailure = $Child.WatchdogFailure
        StandardOutput = $Child.StandardOutput
        StandardError = $Child.StandardError
    }
}

function Write-CohortManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,

        [AllowNull()]
        [string]$Failure
    )

    if ([string]::IsNullOrWhiteSpace($manifestPath)) {
        return
    }

    $nowUtc = [DateTimeOffset]::UtcNow
    $manifest = [ordered]@{
        SchemaVersion = 2
        CampaignId = $CampaignId
        AttemptNumber = $attemptNumber
        CohortId = $cohortId
        Status = $Status
        StartedAtUtc = $startedAtUtc.ToString('O')
        UpdatedAtUtc = $nowUtc.ToString('O')
        PlannedDurationSeconds = $DurationSeconds
        HeartbeatSeconds = $HeartbeatSeconds
        RunDiscoveryTimeoutSeconds = $RunDiscoveryTimeoutSeconds
        CheckpointStartGraceSeconds = $CheckpointStartGraceSeconds
        CheckpointStaleSeconds = $CheckpointStaleSeconds
        AnalysisTimeoutSeconds = $AnalysisTimeoutSeconds
        ServiceExecutable = $servicePath
        OutputRoot = $resolvedOutputRoot
        ControlRoot = $resolvedControlRoot
        SupervisorProcessId = $PID
        HostName = [Environment]::MachineName
        PowerKeepAwake = $powerRequestSet
        FailureReason = $Failure
        Children = @($children | ForEach-Object {
            Get-CurrentChildSnapshot -Child $_ -NowUtc $nowUtc
        })
    }
    Write-JsonAtomic -Path $manifestPath -Value $manifest
}

function Write-SupervisorHeartbeatEvent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$UpdatedAtUtc
    )

    if ([string]::IsNullOrWhiteSpace($HeartbeatEventSource)) {
        return
    }

    if (-not [string]::Equals($Status, $script:heartbeatPhaseStatus, [StringComparison]::OrdinalIgnoreCase)) {
        $script:heartbeatPhaseStatus = $Status
        $script:heartbeatPhaseStartedAtUtc = $UpdatedAtUtc
    }

    if (@('in_progress', 'validating_completion') -contains $Status -and
        [string]::Equals($Status, $script:lastHeartbeatEventStatus, [StringComparison]::OrdinalIgnoreCase) -and
        ($UpdatedAtUtc - $script:lastHeartbeatEventAtUtc).TotalSeconds -lt 30) {
        return
    }

    $heartbeat = [ordered]@{
        SchemaVersion = 1
        Kind = 'SupervisorHeartbeat'
        CampaignId = $CampaignId
        Status = $Status
        AttemptCount = $attemptNumber
        ActiveCohortId = $cohortId
        PlannedDurationSeconds = $DurationSeconds
        PhaseStartedAtUtc = $script:heartbeatPhaseStartedAtUtc.ToString('O')
        UpdatedAtUtc = $UpdatedAtUtc.ToString('O')
        SupervisorProcessId = $PID
        Children = @($children | ForEach-Object {
            $_.Process.Refresh()
            [ordered]@{
                Asset = $_.Asset.ToUpperInvariant()
                ProcessId = $_.Process.Id
                HasExited = $_.Process.HasExited
                Phase = $_.Phase
                RunId = $_.RunId
                RunStatus = $_.RunStatus
                CheckpointStatus = $_.CheckpointStatus
                CheckpointUpdatedAtUtc = if ($null -ne $_.CheckpointUpdatedAtUtc) {
                    $_.CheckpointUpdatedAtUtc.ToString('O')
                } else {
                    $null
                }
            }
        })
    }
    [System.Diagnostics.EventLog]::WriteEntry(
        $HeartbeatEventSource,
        ($heartbeat | ConvertTo-Json -Depth 6 -Compress),
        [System.Diagnostics.EventLogEntryType]::Information,
        $heartbeatEventId)
    $script:lastHeartbeatEventAtUtc = $UpdatedAtUtc
    $script:lastHeartbeatEventStatus = $Status
}

function Write-CampaignState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,

        [AllowNull()]
        [string]$Failure,

        [AllowNull()]
        [object]$CompletedAtUtc
    )

    $nowUtc = [DateTimeOffset]::UtcNow
    $state = [ordered]@{
        SchemaVersion = 1
        CampaignId = $CampaignId
        Status = $Status
        AttemptCount = $attemptNumber
        ActiveCohortId = $cohortId
        ActiveCohortManifest = $manifestPath
        PlannedDurationSeconds = $DurationSeconds
        ServiceExecutable = $servicePath
        OutputRoot = $resolvedOutputRoot
        ControlRoot = $resolvedControlRoot
        StartedAtUtc = if ($null -ne $campaign -and $null -ne $campaign.StartedAtUtc) {
            [string]$campaign.StartedAtUtc
        } else {
            $startedAtUtc.ToString('O')
        }
        LastAttemptStartedAtUtc = $startedAtUtc.ToString('O')
        UpdatedAtUtc = $nowUtc.ToString('O')
        CompletedAtUtc = if ($null -ne $CompletedAtUtc) {
            ([DateTimeOffset]$CompletedAtUtc).ToString('O')
        } else {
            $null
        }
        LastFailureReason = $Failure
        SupervisorProcessId = $PID
        HostName = [Environment]::MachineName
    }
    Write-JsonAtomic -Path $campaignPath -Value $state
    $script:campaign = [pscustomobject]$state
    Write-SupervisorHeartbeatEvent -Status $Status -UpdatedAtUtc $nowUtc
}

function Find-ChildRun {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Child
    )

    if ($null -ne $Child.RunDirectory) {
        return $null
    }

    $matches = @()
    foreach ($directory in @(Get-ChildItem -LiteralPath $Child.OutputDirectory -Directory -Force)) {
        if ($Child.ExistingRunNames.Contains($directory.Name)) {
            continue
        }

        if (($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            return "Run directory is a reparse point: $($directory.FullName)"
        }

        $runManifestPath = Join-Path $directory.FullName 'run.json'
        if (-not (Test-Path -LiteralPath $runManifestPath -PathType Leaf)) {
            continue
        }

        try {
            $run = Read-JsonFile -Path $runManifestPath
            $runStartedAtUtc = [DateTimeOffset]::Parse(
                [string]$run.StartedAtUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind)
            $expectedOutputDirectory = [System.IO.Path]::GetFullPath($directory.FullName)
            $manifestOutputDirectory = [System.IO.Path]::GetFullPath([string]$run.OutputDirectory)
            if ([string]::Equals([string]$run.RunId, $directory.Name, [StringComparison]::Ordinal) -and
                [string]::Equals([string]$run.AssetSymbol, $Child.Asset.ToUpperInvariant(), [StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals([string]$run.CommandMode, 'collect', [StringComparison]::OrdinalIgnoreCase) -and
                [int]$run.DurationSeconds -eq $DurationSeconds -and
                [string]::Equals($expectedOutputDirectory, $manifestOutputDirectory, [StringComparison]::OrdinalIgnoreCase) -and
                $runStartedAtUtc -ge $Child.ProcessStartTimeUtc.AddSeconds(-5)) {
                $matches += [pscustomobject]@{
                    Directory = $directory.FullName
                    Manifest = $run
                }
            }
        }
        catch {
            return "Run manifest is unreadable for $($Child.Asset.ToUpperInvariant()): $($_.Exception.Message)"
        }
    }

    if ($matches.Count -gt 1) {
        return "More than one new run directory matched $($Child.Asset.ToUpperInvariant())."
    }

    if ($matches.Count -eq 1) {
        $match = $matches[0]
        $Child.RunDirectory = $match.Directory
        $Child.RunId = [string]$match.Manifest.RunId
        $Child.RunStatus = [string]$match.Manifest.Status
        $Child.IndexPath = Join-Path $match.Directory 'events.index.json'
        $Child.Phase = 'collecting'
    }

    return $null
}

function Update-ChildHealth {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Child,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$NowUtc
    )

    $Child.Process.Refresh()
    if ($Child.Process.HasExited) {
        $Child.Phase = if ($Child.Process.ExitCode -eq 0) { 'completed' } else { 'failed' }
        $Child.WatchdogConsecutiveFailures = 0
        $Child.WatchdogFailure = $null
        return $null
    }

    $healthFailure = Find-ChildRun -Child $Child
    if ($null -eq $healthFailure -and $null -eq $Child.RunDirectory) {
        if (($NowUtc - $Child.ProcessStartTimeUtc).TotalSeconds -gt $RunDiscoveryTimeoutSeconds) {
            $healthFailure = "$($Child.Asset.ToUpperInvariant()) did not create a valid run manifest within $RunDiscoveryTimeoutSeconds seconds."
        }
    }

    if ($null -eq $healthFailure -and $null -ne $Child.RunDirectory) {
        try {
            Assert-NoReparsePoint -Path $Child.RunDirectory -Name "$($Child.Asset.ToUpperInvariant()) run directory"
            $run = Read-JsonFile -Path (Join-Path $Child.RunDirectory 'run.json')
            if (-not [string]::Equals([string]$run.RunId, $Child.RunId, [StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$run.AssetSymbol, $Child.Asset.ToUpperInvariant(), [StringComparison]::OrdinalIgnoreCase)) {
                throw 'run identity changed after discovery.'
            }

            $Child.RunStatus = [string]$run.Status
            if ([string]::Equals($Child.RunStatus, 'in_progress', [StringComparison]::OrdinalIgnoreCase)) {
                $Child.Phase = 'collecting'
            }
            else {
                $Child.Phase = 'analyzing'
                if ($null -eq $Child.AnalysisStartedAtUtc) {
                    $Child.AnalysisStartedAtUtc = $NowUtc
                }
            }

            $analysisPath = Join-Path $Child.RunDirectory 'analysis.json'
            if (Test-Path -LiteralPath $analysisPath -PathType Leaf) {
                $analysis = Read-JsonFile -Path $analysisPath
                $Child.AnalysisStatus = [string]$analysis.Status
            }

            if ($Child.Phase -eq 'collecting') {
                if (Test-Path -LiteralPath $Child.IndexPath -PathType Leaf) {
                    $index = Read-JsonFile -Path $Child.IndexPath
                    if (-not [string]::Equals([string]$index.RunId, $Child.RunId, [StringComparison]::Ordinal) -or
                        -not [string]::Equals([string]$index.AssetSymbol, $Child.Asset.ToUpperInvariant(), [StringComparison]::OrdinalIgnoreCase)) {
                        throw 'event index identity does not match the active run.'
                    }

                    $checkpointUtc = [DateTimeOffset]::Parse(
                        [string]$index.UpdatedAtUtc,
                        [Globalization.CultureInfo]::InvariantCulture,
                        [Globalization.DateTimeStyles]::RoundtripKind)
                    $Child.CheckpointStatus = [string]$index.Status
                    $Child.CheckpointUpdatedAtUtc = $checkpointUtc
                    if (($NowUtc - $checkpointUtc).TotalSeconds -gt $CheckpointStaleSeconds) {
                        $healthFailure = "$($Child.Asset.ToUpperInvariant()) checkpoint is older than $CheckpointStaleSeconds seconds."
                    }
                }
                elseif (($NowUtc - $Child.ProcessStartTimeUtc).TotalSeconds -gt $CheckpointStartGraceSeconds) {
                    $healthFailure = "$($Child.Asset.ToUpperInvariant()) created no finalized checkpoint within $CheckpointStartGraceSeconds seconds."
                }
            }
            elseif ($null -ne $Child.AnalysisStartedAtUtc -and
                ($NowUtc - $Child.AnalysisStartedAtUtc).TotalSeconds -gt $AnalysisTimeoutSeconds) {
                $healthFailure = "$($Child.Asset.ToUpperInvariant()) analysis exceeded $AnalysisTimeoutSeconds seconds."
            }
        }
        catch {
            $healthFailure = "$($Child.Asset.ToUpperInvariant()) health evidence is invalid: $($_.Exception.Message)"
        }
    }

    if ($null -eq $healthFailure) {
        $Child.WatchdogConsecutiveFailures = 0
        $Child.WatchdogFailure = $null
        return $null
    }

    $Child.WatchdogConsecutiveFailures++
    $Child.WatchdogFailure = $healthFailure
    if ($Child.WatchdogConsecutiveFailures -ge 2) {
        return $healthFailure
    }

    return $null
}

function Get-ChildCompletionFailure {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Child,

        [scriptblock]$ProgressHeartbeat
    )

    if ($Child.CompletionValidated) {
        return $null
    }

    if ($null -eq $Child.RunDirectory) {
        $discoveryFailure = Find-ChildRun -Child $Child
        if ($null -ne $discoveryFailure) {
            return $discoveryFailure
        }
    }

    if ($null -eq $Child.RunDirectory) {
        return "$($Child.Asset.ToUpperInvariant()) exited without a valid run directory."
    }

    $Child.Phase = 'validating'
    $completionFailure = Get-RunCompletionEvidenceFailure `
        -Asset $Child.Asset `
        -RunDirectory $Child.RunDirectory `
        -RunId $Child.RunId `
        -ProgressHeartbeat $ProgressHeartbeat
    if ($null -ne $completionFailure) {
        return $completionFailure
    }

    $run = Read-JsonFile -Path (Join-Path $Child.RunDirectory 'run.json')
    $index = Read-JsonFile -Path (Join-Path $Child.RunDirectory 'events.index.json')
    $analysis = Read-JsonFile -Path (Join-Path $Child.RunDirectory 'analysis.json')
    $Child.RunStatus = [string]$run.Status
    $Child.AnalysisStatus = [string]$analysis.Status
    $Child.CheckpointStatus = [string]$index.Status
    $Child.CheckpointUpdatedAtUtc = [DateTimeOffset]::Parse(
        [string]$index.UpdatedAtUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    $Child.Phase = 'completed'
    $Child.CompletionValidated = $true
    return $null
}

function Get-RunCompletionEvidenceFailure {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Asset,

        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RunId,

        [scriptblock]$ProgressHeartbeat
    )

    try {
        Assert-NoReparsePoint -Path $RunDirectory -Name "$($Asset.ToUpperInvariant()) completed run directory"
        $normalizedRunDirectory = [System.IO.Path]::GetFullPath($RunDirectory).TrimEnd('\')
        $run = Read-JsonFile -Path (Join-Path $RunDirectory 'run.json')
        if ([int]$run.SchemaVersion -ne 2 -or
            -not [string]::Equals([string]$run.RunId, $RunId, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$run.AssetSymbol, $Asset.ToUpperInvariant(), [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$run.CommandMode, 'collect', [StringComparison]::OrdinalIgnoreCase) -or
            [int]$run.DurationSeconds -ne $DurationSeconds -or
            -not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$run.OutputDirectory).TrimEnd('\'),
                $normalizedRunDirectory,
                [StringComparison]::OrdinalIgnoreCase)) {
            return "$($Asset.ToUpperInvariant()) completion run identity is invalid."
        }

        $runStatus = [string]$run.Status
        if (-not [string]::Equals($runStatus, 'completed', [StringComparison]::OrdinalIgnoreCase) -and
            -not [string]::Equals($runStatus, 'completed_with_gaps', [StringComparison]::OrdinalIgnoreCase)) {
            return "$($Asset.ToUpperInvariant()) run ended with status $runStatus."
        }

        if (-not [string]::Equals([string]$run.EventsFile, 'events.index.json', [StringComparison]::Ordinal)) {
            return "$($Asset.ToUpperInvariant()) run did not bind the expected event index."
        }

        $indexPath = Join-Path $RunDirectory 'events.index.json'
        $index = Read-JsonFile -Path $indexPath
        $segments = @($index.Segments)
        if ([int]$index.SchemaVersion -ne 2 -or
            -not [string]::Equals([string]$index.RunId, $RunId, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$index.AssetSymbol, $Asset.ToUpperInvariant(), [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals([string]$index.Status, 'completed', [StringComparison]::OrdinalIgnoreCase) -or
            [long]$index.TotalEvents -le 0 -or
            $segments.Count -le 0) {
            return "$($Asset.ToUpperInvariant()) completion event index is invalid."
        }

        if ($null -ne $ProgressHeartbeat) {
            & $ProgressHeartbeat
        }
        $actualIndexSha256 = Get-Sha256 -Path $indexPath
        if ([string]$run.EventsSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            -not [string]::Equals(
                $actualIndexSha256,
                [string]$run.EventsSha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            return "$($Asset.ToUpperInvariant()) event-index SHA-256 does not match run.json."
        }

        $segmentEventCount = [long]0
        $segmentFileNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $segmentSequences = New-Object 'System.Collections.Generic.HashSet[int]'
        foreach ($segment in $segments) {
            $fileName = [string]$segment.FileName
            $sequence = [int]$segment.Sequence
            $eventCount = [long]$segment.EventCount
            if ([string]::IsNullOrWhiteSpace($fileName) -or
                [System.IO.Path]::IsPathRooted($fileName) -or
                $fileName.Contains('\') -or
                $fileName.Contains('/') -or
                $fileName.Contains(':') -or
                $sequence -le 0 -or
                $eventCount -le 0 -or
                -not $segmentFileNames.Add($fileName) -or
                -not $segmentSequences.Add($sequence) -or
                [string]$segment.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
                return "$($Asset.ToUpperInvariant()) event segment metadata is invalid."
            }

            $segmentPath = Join-Path $RunDirectory $fileName
            if (-not (Test-Path -LiteralPath $segmentPath -PathType Leaf)) {
                return "$($Asset.ToUpperInvariant()) event segment is missing: $fileName"
            }

            Assert-NoReparsePoint -Path $segmentPath -Name "$($Asset.ToUpperInvariant()) event segment"
            if ($null -ne $ProgressHeartbeat) {
                & $ProgressHeartbeat
            }
            if (-not [string]::Equals(
                (Get-Sha256 -Path $segmentPath),
                [string]$segment.Sha256,
                [StringComparison]::OrdinalIgnoreCase)) {
                return "$($Asset.ToUpperInvariant()) event segment SHA-256 mismatch: $fileName"
            }

            if ($null -ne $ProgressHeartbeat) {
                & $ProgressHeartbeat
            }

            $segmentEventCount += $eventCount
        }

        if ($segmentEventCount -ne [long]$index.TotalEvents) {
            return "$($Asset.ToUpperInvariant()) event segment counts do not match TotalEvents."
        }

        $analysisPath = Join-Path $RunDirectory 'analysis.json'
        if (-not (Test-Path -LiteralPath $analysisPath -PathType Leaf)) {
            return "$($Asset.ToUpperInvariant()) exited without analysis.json."
        }

        $analysis = Read-JsonFile -Path $analysisPath
        $validAnalysisStatuses = @(
            'InsufficientData',
            'NoObservedPointEstimateLift',
            'ExploratoryPointEstimateLiftVsMajorityOnly',
            'ExploratoryPointEstimateLiftVsBothBaselines')
        if ($validAnalysisStatuses -notcontains [string]$analysis.Status -or
            -not [string]::Equals([string]$analysis.AssetSymbol, $Asset.ToUpperInvariant(), [StringComparison]::OrdinalIgnoreCase)) {
            return "$($Asset.ToUpperInvariant()) analysis identity or status is invalid."
        }

        return $null
    }
    catch {
        return "$($Asset.ToUpperInvariant()) completion evidence is unreadable: $($_.Exception.Message)"
    }
}

function Get-CompletedCampaignEvidenceFailure {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$CampaignState,

        [scriptblock]$ProgressHeartbeat
    )

    try {
        $completedManifestPath = [System.IO.Path]::GetFullPath([string]$CampaignState.ActiveCohortManifest)
        $cohortRoot = [System.IO.Path]::GetFullPath((Join-Path $resolvedOutputRoot 'cohorts')).TrimEnd('\') + '\'
        if (-not $completedManifestPath.StartsWith($cohortRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $completedManifestPath -PathType Leaf)) {
            return 'Completed cohort manifest is missing or outside OutputRoot.'
        }

        Assert-NoReparsePoint -Path $completedManifestPath -Name 'Completed cohort manifest'
        $completedManifest = Read-JsonFile -Path $completedManifestPath
        $completedCohortId = [string]$completedManifest.CohortId
        if ([int]$completedManifest.SchemaVersion -ne 2 -or
            -not [string]::Equals([string]$completedManifest.CampaignId, $CampaignId, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$completedManifest.Status, 'completed', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace($completedCohortId) -or
            -not [string]::Equals([string]$CampaignState.ActiveCohortId, $completedCohortId, [StringComparison]::Ordinal) -or
            [int]$CampaignState.AttemptCount -ne [int]$completedManifest.AttemptNumber -or
            -not [string]::Equals(
                (Split-Path -Leaf (Split-Path -Parent $completedManifestPath)),
                $completedCohortId,
                [StringComparison]::Ordinal) -or
            -not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$completedManifest.OutputRoot),
                $resolvedOutputRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            return 'Completed cohort manifest identity or status is invalid.'
        }

        $completedChildren = @($completedManifest.Children)
        $completedAssets = @($completedChildren | ForEach-Object { [string]$_.Asset } | Sort-Object -Unique)
        if ($completedChildren.Count -ne 3 -or
            ($completedAssets -join ',') -ne 'BTC,ETH,SOL') {
            return 'Completed cohort does not contain exactly BTC, ETH, and SOL.'
        }

        foreach ($completedChild in $completedChildren) {
            if (-not [string]::Equals([string]$completedChild.Phase, 'completed', [StringComparison]::OrdinalIgnoreCase)) {
                return "$($completedChild.Asset) completed child phase is invalid."
            }

            $completedAsset = ([string]$completedChild.Asset).ToLowerInvariant()
            $expectedRunPrefix = [System.IO.Path]::GetFullPath(
                (Join-Path $resolvedOutputRoot (Join-Path $completedAsset (Join-Path 'cohorts' (Join-Path $completedCohortId 'runs'))))).TrimEnd('\') + '\'
            $completedRunDirectory = [System.IO.Path]::GetFullPath([string]$completedChild.RunDirectory)
            if (-not $completedRunDirectory.StartsWith($expectedRunPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not [string]::Equals(
                    (Split-Path -Leaf $completedRunDirectory),
                    [string]$completedChild.RunId,
                    [StringComparison]::Ordinal)) {
                return "$($completedChild.Asset) completed run is outside its isolated cohort output directory."
            }

            $runFailure = Get-RunCompletionEvidenceFailure `
                -Asset ([string]$completedChild.Asset) `
                -RunDirectory $completedRunDirectory `
                -RunId ([string]$completedChild.RunId) `
                -ProgressHeartbeat $ProgressHeartbeat
            if ($null -ne $runFailure) {
                return $runFailure
            }
        }

        return $null
    }
    catch {
        return 'Completed campaign evidence is unreadable: ' + $_.Exception.Message
    }
}

try {
    $servicePath = Resolve-SafeAbsolutePath -Path $ServiceExecutable -Name 'ServiceExecutable'
    if (-not (Test-Path -LiteralPath $servicePath -PathType Leaf)) {
        throw "PolyCopyTrader service executable was not found: $servicePath"
    }

    $resolvedOutputRoot = Resolve-SafeAbsolutePath -Path $OutputRoot -Name 'OutputRoot' -AllowCreate
    $resolvedControlRoot = Resolve-SafeAbsolutePath -Path $ControlRoot -Name 'ControlRoot' -AllowCreate
    Assert-NoReparsePoint -Path $servicePath -Name 'ServiceExecutable'
    Assert-NoReparsePoint -Path $resolvedOutputRoot -Name 'OutputRoot'
    Assert-NoReparsePoint -Path $resolvedControlRoot -Name 'ControlRoot'
    if ([string]::IsNullOrWhiteSpace($HeartbeatEventSource) -ne
        [string]::IsNullOrWhiteSpace($HeartbeatEventLogName)) {
        throw 'HeartbeatEventSource and HeartbeatEventLogName must be supplied together.'
    }

    if (-not [string]::IsNullOrWhiteSpace($HeartbeatEventSource)) {
        if (-not [System.Diagnostics.EventLog]::SourceExists($HeartbeatEventSource) -or
            -not [string]::Equals(
                [System.Diagnostics.EventLog]::LogNameFromSourceName($HeartbeatEventSource, '.'),
                $HeartbeatEventLogName,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Heartbeat event source $HeartbeatEventSource is not registered in $HeartbeatEventLogName."
        }
    }

    $campaignDirectory = Join-Path $resolvedControlRoot (Join-Path 'campaigns' $CampaignId)
    $null = New-Item -ItemType Directory -Path $campaignDirectory -Force
    Assert-NoReparsePoint -Path $campaignDirectory -Name 'Campaign control directory'
    $campaignPath = Join-Path $campaignDirectory 'campaign.json'
    $lockPath = Join-Path $campaignDirectory 'supervisor.lock'
    try {
        $lockStream = [System.IO.FileStream]::new(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        throw "Campaign $CampaignId already has an active supervisor."
    }

    if (Test-Path -LiteralPath $campaignPath -PathType Leaf) {
        $campaign = Read-JsonFile -Path $campaignPath
        if ([int]$campaign.SchemaVersion -ne 1 -or
            -not [string]::Equals([string]$campaign.CampaignId, $CampaignId, [StringComparison]::Ordinal) -or
            [int]$campaign.PlannedDurationSeconds -ne $DurationSeconds -or
            -not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$campaign.ServiceExecutable),
                $servicePath,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$campaign.OutputRoot),
                $resolvedOutputRoot,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$campaign.ControlRoot),
                $resolvedControlRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Campaign $CampaignId state does not match this supervisor configuration."
        }

        if ([string]::Equals([string]$campaign.Status, 'completed', [StringComparison]::OrdinalIgnoreCase)) {
            $attemptNumber = [int]$campaign.AttemptCount
            $cohortId = [string]$campaign.ActiveCohortId
            $completedRevalidationHeartbeat = {
                Write-SupervisorHeartbeatEvent `
                    -Status 'validating_completion' `
                    -UpdatedAtUtc ([DateTimeOffset]::UtcNow)
            }
            & $completedRevalidationHeartbeat
            $completedEvidenceFailure = Get-CompletedCampaignEvidenceFailure `
                -CampaignState $campaign `
                -ProgressHeartbeat $completedRevalidationHeartbeat
            if ($null -eq $completedEvidenceFailure) {
                Write-SupervisorHeartbeatEvent `
                    -Status 'completed' `
                    -UpdatedAtUtc ([DateTimeOffset]::UtcNow)
                [Console]::Out.WriteLine("Campaign $CampaignId is already completed; no new cohort was started.")
                $campaignAlreadyCompleted = $true
                $exitCode = 0
                return
            }

            $failureReason = 'Previous completed campaign evidence is invalid: ' + $completedEvidenceFailure
        }

        $attemptNumber = [int]$campaign.AttemptCount + 1
    }
    else {
        $attemptNumber = 1
    }

    Initialize-NativeRuntimeSupport
    [PolyCopyTraderOrderBookNative]::SetKeepAwake()
    $powerRequestSet = $true
    $processJob = New-Object PolyCopyTraderOrderBookProcessJob

    $startedAtUtc = [DateTimeOffset]::UtcNow
    $cohortId = 'crypto-orderbook-cohort-' +
        $startedAtUtc.ToString('yyyyMMdd-HHmmss') + '-' +
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $cohortDirectory = Join-Path $resolvedOutputRoot (Join-Path 'cohorts' $cohortId)
    $logDirectory = Join-Path $cohortDirectory 'logs'
    $null = New-Item -ItemType Directory -Path $logDirectory -Force
    Assert-NoReparsePoint -Path $cohortDirectory -Name 'Cohort directory'
    $manifestPath = Join-Path $cohortDirectory 'cohort.json'
    $cohortStatus = 'in_progress'
    Write-CampaignState -Status 'in_progress' -Failure $failureReason -CompletedAtUtc $null
    $campaignAttemptStarted = $true

    foreach ($asset in $assets) {
        $assetOutputDirectory = Join-Path $resolvedOutputRoot (Join-Path $asset (Join-Path 'cohorts' (Join-Path $cohortId 'runs')))
        $null = New-Item -ItemType Directory -Path $assetOutputDirectory -Force
        Assert-NoReparsePoint -Path $assetOutputDirectory -Name "$($asset.ToUpperInvariant()) output directory"
        $existingRunNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($existingRun in @(Get-ChildItem -LiteralPath $assetOutputDirectory -Directory -Force)) {
            $null = $existingRunNames.Add($existingRun.Name)
        }

        $stdoutPath = Join-Path $logDirectory ($asset + '.stdout.log')
        $stderrPath = Join-Path $logDirectory ($asset + '.stderr.log')
        $argumentTokens = @(
            '--crypto-orderbook-prediction-study',
            '--crypto-orderbook-study-mode', 'collect',
            '--crypto-orderbook-study-asset', $asset,
            '--crypto-orderbook-study-source', 'json',
            '--crypto-orderbook-study-output-dir', $assetOutputDirectory,
            '--crypto-orderbook-study-duration-seconds',
            $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
        )
        $argumentLine = ($argumentTokens | ForEach-Object {
            ConvertTo-NativeArgument -Value ([string]$_)
        }) -join ' '
        $process = Start-Process `
            -FilePath $servicePath `
            -ArgumentList $argumentLine `
            -WorkingDirectory ([System.IO.Path]::GetDirectoryName($servicePath)) `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -PassThru
        try {
            $processJob.AddProcess($process)
        }
        catch {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw
        }

        $children += [pscustomobject]@{
            Asset = $asset
            Process = $process
            ProcessStartTimeUtc = [DateTimeOffset]($process.StartTime.ToUniversalTime())
            ExistingRunNames = $existingRunNames
            OutputDirectory = $assetOutputDirectory
            StandardOutput = $stdoutPath
            StandardError = $stderrPath
            Phase = 'starting'
            RunDirectory = $null
            RunId = $null
            RunStatus = $null
            AnalysisStatus = $null
            AnalysisStartedAtUtc = $null
            CompletionValidated = $false
            IndexPath = $null
            CheckpointStatus = $null
            CheckpointUpdatedAtUtc = $null
            WatchdogConsecutiveFailures = 0
            WatchdogFailure = $null
        }
    }

    Write-CohortManifest -Status $cohortStatus -Failure $null
    $completionProgressHeartbeat = {
        $progressNowUtc = [DateTimeOffset]::UtcNow
        if (($progressNowUtc - $script:completionValidationHeartbeatAtUtc).TotalSeconds -ge $HeartbeatSeconds) {
            Write-CohortManifest -Status 'in_progress' -Failure $null
            Write-CampaignState -Status 'in_progress' -Failure $null -CompletedAtUtc $null
            $script:completionValidationHeartbeatAtUtc = $progressNowUtc
        }
    }
    while ($true) {
        Start-Sleep -Seconds $HeartbeatSeconds
        Assert-NoReparsePoint -Path $resolvedOutputRoot -Name 'OutputRoot'
        Assert-NoReparsePoint -Path $resolvedControlRoot -Name 'ControlRoot'
        $nowUtc = [DateTimeOffset]::UtcNow
        $watchdogFailures = @()
        foreach ($child in $children) {
            $watchdogFailure = Update-ChildHealth -Child $child -NowUtc $nowUtc
            if ($null -ne $watchdogFailure) {
                $watchdogFailures += $watchdogFailure
            }
        }

        $failedChildren = @($children | Where-Object {
            $_.Process.Refresh()
            $_.Process.HasExited -and $_.Process.ExitCode -ne 0
        })
        if ($failedChildren.Count -gt 0) {
            $cohortStatus = 'failed'
            $failureReason = ($failedChildren | ForEach-Object {
                $_.Asset.ToUpperInvariant() + ' exited with code ' + $_.Process.ExitCode
            }) -join '; '
        }
        elseif ($watchdogFailures.Count -gt 0) {
            $cohortStatus = 'failed'
            $failureReason = $watchdogFailures -join '; '
        }
        else {
            $invalidCompletedChildren = @($children | Where-Object {
                $_.Process.Refresh()
                $_.Process.HasExited -and $_.Process.ExitCode -eq 0
            } | ForEach-Object {
                Get-ChildCompletionFailure -Child $_ -ProgressHeartbeat $completionProgressHeartbeat
            } | Where-Object { $null -ne $_ })
            if ($invalidCompletedChildren.Count -gt 0) {
                $cohortStatus = 'failed'
                $failureReason = $invalidCompletedChildren -join '; '
            }
        }

        Write-CohortManifest -Status $cohortStatus -Failure $failureReason
        Write-CampaignState -Status 'in_progress' -Failure $failureReason -CompletedAtUtc $null
        if ($cohortStatus -eq 'failed') {
            break
        }

        $runningChildren = @($children | Where-Object {
            $_.Process.Refresh()
            -not $_.Process.HasExited
        })
        if ($runningChildren.Count -eq 0) {
            $completionFailures = @($children | ForEach-Object {
                Get-ChildCompletionFailure -Child $_ -ProgressHeartbeat $completionProgressHeartbeat
            } | Where-Object { $null -ne $_ })
            if ($completionFailures.Count -gt 0) {
                $cohortStatus = 'failed'
                $failureReason = $completionFailures -join '; '
            }
            else {
                $cohortStatus = 'completed'
                $exitCode = 0
            }

            break
        }
    }
}
catch {
    $cohortStatus = 'failed'
    $failureReason = $_.Exception.GetType().Name + ': ' + $_.Exception.Message
}
finally {
    if ($null -ne $processJob) {
        $processJob.Dispose()
        $processJob = $null
    }

    foreach ($child in $children) {
        try {
            $child.Process.Refresh()
            if (-not $child.Process.HasExited) {
                $null = $child.Process.WaitForExit(10000)
                $child.Process.Refresh()
            }
        }
        catch {
        }
    }

    if ($campaignAlreadyCompleted) {
    }
    elseif ($cohortStatus -eq 'completed') {
        Write-CohortManifest -Status 'completed' -Failure $null
        Write-CampaignState `
            -Status 'completed' `
            -Failure $null `
            -CompletedAtUtc ([DateTimeOffset]::UtcNow)
    }
    elseif ($campaignAttemptStarted) {
        Write-CohortManifest -Status 'failed' -Failure $failureReason
        Write-CampaignState -Status 'retry_pending' -Failure $failureReason -CompletedAtUtc $null
    }

    if ($powerRequestSet) {
        try {
            [PolyCopyTraderOrderBookNative]::ClearKeepAwake()
        }
        catch {
            if ([string]::IsNullOrWhiteSpace($failureReason)) {
                $failureReason = $_.Exception.Message
            }
        }

        $powerRequestSet = $false
    }

    if ($null -ne $lockStream) {
        $lockStream.Dispose()
        $lockStream = $null
    }
}

if (-not [string]::IsNullOrWhiteSpace($failureReason)) {
    [Console]::Error.WriteLine($failureReason)
}

exit $exitCode
