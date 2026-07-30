[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$TaskName,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$WatchdogTaskName,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$HeartbeatEventSource,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$HeartbeatEventLogName,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$')]
    [string]$CampaignId,

    [Parameter(Mandatory = $true)]
    [ValidateRange(10, 604800)]
    [int]$DurationSeconds,

    [ValidateRange(60, 3600)]
    [int]$StaleSeconds = 180,

    [ValidateRange(30, 3600)]
    [int]$StartupGraceSeconds = 180,

    [ValidateRange(300, 7200)]
    [int]$CompletionValidationGraceSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$nowUtc = [DateTimeOffset]::UtcNow
$supervisorHeartbeatEventId = 4100
$watchdogEventId = 4110
$action = 'none'
$detail = $null
$exitCode = 0

function ConvertFrom-HeartbeatEvent {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Eventing.Reader.EventRecord]$EventRecord
    )

    foreach ($property in @($EventRecord.Properties)) {
        if ($null -ne $property.Value -and
            -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            try {
                return [string]$property.Value | ConvertFrom-Json
            }
            catch {
            }
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

function Get-LatestSupervisorHeartbeat {
    param(
        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$MinimumCreatedAtUtc
    )

    $lookbackSeconds = [Math]::Max(
        [Math]::Max($StaleSeconds, $StartupGraceSeconds),
        $CompletionValidationGraceSeconds) + 600
    $queryStartUtc = [DateTimeOffset]::UtcNow.AddSeconds(-$lookbackSeconds)
    $queryErrors = @()
    $events = @(Get-WinEvent `
        -FilterHashtable @{
            LogName = $HeartbeatEventLogName
            ProviderName = $HeartbeatEventSource
            Id = $supervisorHeartbeatEventId
            StartTime = $queryStartUtc.UtcDateTime
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

            $state = ConvertFrom-HeartbeatEvent -EventRecord $eventRecord
            if ($null -eq $state -or
                [int]$state.SchemaVersion -ne 1 -or
                -not [string]::Equals([string]$state.Kind, 'SupervisorHeartbeat', [StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$state.CampaignId, $CampaignId, [StringComparison]::Ordinal) -or
                [int]$state.PlannedDurationSeconds -ne $DurationSeconds -or
                @('in_progress', 'validating_completion', 'retry_pending', 'completed') -notcontains [string]$state.Status -or
                [int]$state.SupervisorProcessId -le 0) {
                continue
            }

            $updatedAtUtc = [DateTimeOffset]::Parse(
                [string]$state.UpdatedAtUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind)
            $phaseStartedAtUtc = [DateTimeOffset]::Parse(
                [string]$state.PhaseStartedAtUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind)
            if ([Math]::Abs(($createdAtUtc - $updatedAtUtc).TotalSeconds) -gt 60 -or
                $phaseStartedAtUtc -lt $MinimumCreatedAtUtc -or
                $phaseStartedAtUtc -gt $updatedAtUtc.AddSeconds(60)) {
                continue
            }

            return [pscustomobject]@{
                State = $state
                EventCreatedAtUtc = $createdAtUtc
                UpdatedAtUtc = $updatedAtUtc
                PhaseStartedAtUtc = $phaseStartedAtUtc
            }
        }
        catch {
        }
    }

    return $null
}

function Write-WatchdogEvent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,

        [AllowNull()]
        [string]$Message
    )

    $state = [ordered]@{
        SchemaVersion = 1
        Kind = 'WatchdogStatus'
        CampaignId = $CampaignId
        TaskName = $TaskName
        WatchdogTaskName = $WatchdogTaskName
        CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Status = $Status
        Action = $action
        Detail = $Message
    }
    [System.Diagnostics.EventLog]::WriteEntry(
        $HeartbeatEventSource,
        ($state | ConvertTo-Json -Depth 5 -Compress),
        [System.Diagnostics.EventLogEntryType]::Information,
        $watchdogEventId)
}

function Restart-MainTask {
    Stop-ScheduledTask -TaskPath '\' -TaskName $TaskName -ErrorAction Stop
    $stopDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $currentTask = Get-ScheduledTask -TaskPath '\' -TaskName $TaskName -ErrorAction Stop
    } while ([string]::Equals([string]$currentTask.State, 'Running', [StringComparison]::OrdinalIgnoreCase) -and
        [DateTimeOffset]::UtcNow -lt $stopDeadline)

    if ([string]::Equals([string]$currentTask.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Main task $TaskName did not stop within 30 seconds."
    }

    Start-ScheduledTask -TaskPath '\' -TaskName $TaskName -ErrorAction Stop
}

try {
    if (-not [System.Diagnostics.EventLog]::SourceExists($HeartbeatEventSource) -or
        -not [string]::Equals(
            [System.Diagnostics.EventLog]::LogNameFromSourceName($HeartbeatEventSource, '.'),
            $HeartbeatEventLogName,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Heartbeat event source $HeartbeatEventSource is not registered in $HeartbeatEventLogName."
    }

    $task = Get-ScheduledTask -TaskPath '\' -TaskName $TaskName -ErrorAction Stop
    $taskInfo = Get-ScheduledTaskInfo -TaskPath '\' -TaskName $TaskName -ErrorAction Stop
    $withinStartupGrace = $false
    $lastRunAgeSeconds = $null
    $minimumHeartbeatCreatedAtUtc = $nowUtc.AddSeconds(-($CompletionValidationGraceSeconds + 600))
    if ($taskInfo.LastRunTime -ne [DateTime]::MinValue) {
        $lastRunUtc = [DateTimeOffset]($taskInfo.LastRunTime.ToUniversalTime())
        $lastRunAgeSeconds = ($nowUtc - $lastRunUtc).TotalSeconds
        $withinStartupGrace = $lastRunAgeSeconds -ge -300 -and
            $lastRunAgeSeconds -le $StartupGraceSeconds
        $minimumHeartbeatCreatedAtUtc = $lastRunUtc.AddSeconds(-5)
    }

    $heartbeatRecord = Get-LatestSupervisorHeartbeat -MinimumCreatedAtUtc $minimumHeartbeatCreatedAtUtc
    $heartbeat = if ($null -ne $heartbeatRecord) { $heartbeatRecord.State } else { $null }
    if ($null -ne $heartbeat -and
        [string]::Equals([string]$heartbeat.Status, 'completed', [StringComparison]::OrdinalIgnoreCase)) {
        if ([string]::Equals([string]$task.State, 'Running', [StringComparison]::OrdinalIgnoreCase) -and
            ($nowUtc - $heartbeatRecord.PhaseStartedAtUtc).TotalSeconds -ge -300 -and
            ($nowUtc - $heartbeatRecord.PhaseStartedAtUtc).TotalSeconds -le $CompletionValidationGraceSeconds) {
            $action = 'defer_to_completion_validator'
            Write-WatchdogEvent `
                -Status 'healthy' `
                -Message 'Main task is finishing its validated terminal completion within the bounded grace period.'
            exit 0
        }

        if (-not [string]::Equals([string]$task.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
            $action = 'disable_completed_tasks'
            Disable-ScheduledTask -TaskPath '\' -TaskName $TaskName -ErrorAction Stop | Out-Null
            Disable-ScheduledTask -TaskPath '\' -TaskName $WatchdogTaskName -ErrorAction Stop | Out-Null
            Write-WatchdogEvent `
                -Status 'completed' `
                -Message 'Supervisor reported hash-validated terminal completion; main and watchdog tasks disabled.'
            exit 0
        }

        $detail = "Main task remained Running more than $CompletionValidationGraceSeconds seconds after a completed heartbeat."
    }

    if ($null -ne $heartbeat -and
        [string]::Equals([string]$heartbeat.Status, 'validating_completion', [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals([string]$task.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
        if ($null -ne $lastRunAgeSeconds -and
            ($nowUtc - $heartbeatRecord.PhaseStartedAtUtc).TotalSeconds -ge -300 -and
            ($nowUtc - $heartbeatRecord.PhaseStartedAtUtc).TotalSeconds -le $CompletionValidationGraceSeconds) {
            $action = 'defer_to_completion_validator'
            Write-WatchdogEvent `
                -Status 'healthy' `
                -Message 'Main task is revalidating terminal evidence within the bounded grace period.'
            exit 0
        }

        $detail = "Main task remained in completion validation longer than $CompletionValidationGraceSeconds seconds."
    }

    if ([string]::Equals([string]$task.State, 'Disabled', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Main task $TaskName is disabled before campaign completion."
    }

    if (-not [string]::Equals([string]$task.State, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
        $action = 'start_main_task'
        Start-ScheduledTask -TaskPath '\' -TaskName $TaskName -ErrorAction Stop
        Write-WatchdogEvent -Status 'recovered' -Message "Main task state was $($task.State)."
        exit 0
    }

    $isStale = -not [string]::IsNullOrWhiteSpace($detail)
    if (-not $isStale -and $null -eq $heartbeatRecord) {
        if (-not $withinStartupGrace) {
            $isStale = $true
            $detail = 'Main task is running but no current matching Event Log heartbeat was found.'
        }
    }
    elseif (-not $isStale) {
        $heartbeatAgeSeconds = ($nowUtc - $heartbeatRecord.UpdatedAtUtc).TotalSeconds
        if ($heartbeatAgeSeconds -lt -300) {
            $isStale = $true
            $detail = 'Supervisor heartbeat is more than five minutes in the future.'
        }
        elseif ($heartbeatAgeSeconds -gt $StaleSeconds -and -not $withinStartupGrace) {
            $isStale = $true
            $detail = "Supervisor heartbeat is older than $StaleSeconds seconds."
        }
    }

    if ($isStale) {
        $action = 'restart_stale_main_task'
        Restart-MainTask
        Write-WatchdogEvent -Status 'recovered' -Message $detail
    }
    else {
        $healthyMessage = if ($null -eq $heartbeatRecord) {
            'Main task is within startup grace; no matching heartbeat is required yet.'
        } else {
            'Main task and Event Log heartbeat are current.'
        }
        Write-WatchdogEvent -Status 'healthy' -Message $healthyMessage
    }
}
catch {
    $exitCode = 1
    $detail = $_.Exception.GetType().Name + ': ' + $_.Exception.Message
    try {
        Write-WatchdogEvent -Status 'failed' -Message $detail
    }
    catch {
    }

    [Console]::Error.WriteLine($detail)
}

exit $exitCode
